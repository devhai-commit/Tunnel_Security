using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Runs daily at 2 AM UTC.
/// - TimescaleDB: uses drop_chunks() for efficient chunk-level deletion (no row scanning).
/// - SQLite: deletes old CameraSnapshot and VideoClip files older than the retention window.
/// Note: TimescaleDB compression policy (set in TimescaleInitializer) handles
///       automatic compression — this service handles hard deletion only.
/// </summary>
public class DataRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataRetentionService> _logger;
    private readonly int _retentionDays;

    public DataRetentionService(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<DataRetentionService> logger)
    {
        _retentionDays = configuration.GetValue("DataRetention:Days", 90);
        _scopeFactory  = scopeFactory;
        _logger        = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait until next 2:00 AM UTC
        var now     = DateTime.UtcNow;
        var nextRun = now.Date.AddDays(1).AddHours(2);
        await Task.Delay(nextRun - now, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunRetentionAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task RunRetentionAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);

        using var scope = _scopeFactory.CreateScope();

        // ── TimescaleDB retention (drop_chunks is O(1) — drops whole chunk files) ──
        var ts = scope.ServiceProvider.GetService<TimeSeriesDbContext>();
        if (ts != null)
        {
            try
            {
                // drop_chunks deletes all chunks whose time range ends before the cutoff
                var sql = $"SELECT drop_chunks('{0}', TIMESTAMPTZ '{cutoff:O}');";
                foreach (var table in new[] { "sensor_readings", "sensor_frames_raw",
                                              "node_heartbeats",  "camera_events" })
                {
                    await ts.Database.ExecuteSqlRawAsync(
                        $"SELECT drop_chunks('{table}', older_than => TIMESTAMPTZ '{cutoff:yyyy-MM-ddTHH:mm:ssZ}');",
                        ct);
                    _logger.LogInformation("[Retention] Dropped chunks older than {Cutoff} from {Table}",
                        cutoff, table);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Retention] TimescaleDB drop_chunks failed: {Msg}", ex.Message);
            }
        }

        // ── SQLite retention (CameraSnapshots / VideoClips metadata) ─────────
        var db = scope.ServiceProvider.GetRequiredService<TunnelDbContext>();

        var deletedSnapshots = await db.CameraSnapshots
            .Where(s => s.Timestamp < cutoff)
            .ExecuteDeleteAsync(ct);

        var deletedClips = await db.VideoClips
            .Where(v => v.StartTime < cutoff)
            .ExecuteDeleteAsync(ct);

        _logger.LogInformation(
            "[Retention] Deleted {Snaps} snapshots, {Clips} clips older than {Days} days",
            deletedSnapshots, deletedClips, _retentionDays);
    }
}
