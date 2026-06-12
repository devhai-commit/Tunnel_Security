using Backend.Config;
using Backend.Data;
using Backend.Hubs;
using Backend.Models;
using Backend.Models.TimeSeries;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Checks device health periodically using PeriodicTimer (drift-free).
/// Determines online/offline state from the most recent sensor_readings row
/// (queried from TimescaleDB when available, falls back to SQLite Sensor.LastReading).
/// Writes heartbeat rows to node_heartbeats hypertable.
/// </summary>
public sealed class DeviceHealthService : BackgroundService
{
    private readonly HealthCheckConfig _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<SensorHub> _hub;
    private readonly ILogger<DeviceHealthService> _logger;

    public DeviceHealthService(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        IHubContext<SensorHub> hub,
        ILogger<DeviceHealthService> logger)
    {
        _config       = configuration.GetSection("Devices:HealthCheck").Get<HealthCheckConfig>() ?? new HealthCheckConfig();
        _scopeFactory = scopeFactory;
        _hub          = hub;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[DeviceHealth] Started — interval={Interval}s offline={Timeout}s",
            _config.IntervalSeconds, _config.OfflineTimeoutSeconds);

        // Prime on startup, then tick on interval.
        await CheckAllNodesAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_config.IntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await CheckAllNodesAsync(stoppingToken);
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
    }

    private async Task CheckAllNodesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TunnelDbContext>();
        var ts = scope.ServiceProvider.GetService<TimeSeriesDbContext>();

        var nodes            = await db.Nodes.ToListAsync(ct);
        var offlineThreshold = DateTime.UtcNow.AddSeconds(-_config.OfflineTimeoutSeconds);
        var changed          = new List<object>();

        foreach (var node in nodes)
        {
            DateTime? lastReadingTime = null;

            if (ts is not null)
            {
                try
                {
                    lastReadingTime = await ts.SensorReadings
                        .Where(r => r.NodeId == node.Id)
                        .OrderByDescending(r => r.Time)
                        .Select(r => (DateTime?)r.Time)
                        .FirstOrDefaultAsync(ct);
                }
                catch { /* TimescaleDB unavailable */ }
            }

            if (lastReadingTime is null)
            {
                lastReadingTime = await db.Sensors
                    .Where(s => s.NodeId == node.Id && s.LastReading != null)
                    .MaxAsync(s => (DateTime?)s.LastReading, ct);
            }

            var newStatus = lastReadingTime is null || lastReadingTime < offlineThreshold
                ? NodeStatus.Offline
                : node.Status == NodeStatus.Offline ? NodeStatus.Online : node.Status;

            if (node.Status != newStatus)
            {
                var prev = node.Status;
                node.Status    = newStatus;
                node.LastOnline = newStatus == NodeStatus.Online ? DateTime.UtcNow : node.LastOnline;
                changed.Add(new
                {
                    node.Id,
                    node.Name,
                    PreviousStatus = prev.ToString(),
                    CurrentStatus  = newStatus.ToString(),
                    node.LastOnline
                });
            }

            if (ts is not null)
            {
                ts.NodeHeartbeats.Add(new NodeHeartbeatTs
                {
                    Time            = DateTime.UtcNow,
                    NodeId          = node.Id,
                    NodeByteId      = (short)(node.NodeByteId ?? 0),
                    Status          = newStatus,
                    BatteryLevel    = node.BatteryLevel,
                    Rssi            = node.RSSI,
                    IpAddress       = node.IpAddress,
                    FirmwareVersion = node.FirmwareVersion
                });
            }
        }

        await db.SaveChangesAsync(ct);

        if (ts is not null)
        {
            try   { await ts.SaveChangesAsync(ct); }
            catch (Exception ex)
            {
                _logger.LogDebug("[DeviceHealth] TimescaleDB heartbeat write failed: {Msg}", ex.Message);
            }
        }

        foreach (var change in changed)
        {
            await _hub.Clients.All.SendAsync("DeviceStatusChanged", change, ct);
            _logger.LogInformation("[DeviceHealth] Status changed: {@Change}", change);
        }
    }
}
