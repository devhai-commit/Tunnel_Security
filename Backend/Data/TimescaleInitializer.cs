using Microsoft.EntityFrameworkCore;

namespace Backend.Data;

/// <summary>
/// Runs after EnsureCreated() to convert plain PostgreSQL tables into
/// TimescaleDB hypertables (partitioned by time).
///
/// Safe to call multiple times — uses IF NOT EXISTS checks.
/// If TimescaleDB extension is not installed the call is silently skipped
/// so the app can still start (just without time-series partitioning).
/// </summary>
public static class TimescaleInitializer
{
    private static readonly (string table, string timeCol, string chunkInterval)[] Hypertables =
    {
        ("sensor_readings",   "time", "1 day"),
        ("sensor_frames_raw", "time", "1 day"),
        ("node_heartbeats",   "time", "1 day"),
        ("camera_events",     "time", "1 day"),
    };

    public static async Task InitializeAsync(TimeSeriesDbContext db, ILogger logger)
    {
        // 1. Create tables if they don't exist (idempotent)
        await db.Database.EnsureCreatedAsync();

        // 2. Enable TimescaleDB extension (requires superuser the first time)
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE EXTENSION IF NOT EXISTS timescaledb CASCADE;");
        }
        catch (Exception ex)
        {
            logger.LogWarning("[Timescale] Could not enable extension: {Msg} — " +
                              "hypertable partitioning skipped", ex.Message);
            return;
        }

        // 3. Convert each table to a hypertable
        foreach (var (table, timeCol, chunkInterval) in Hypertables)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync($"""
                    SELECT create_hypertable(
                        '{table}', '{timeCol}',
                        chunk_time_interval => INTERVAL '{chunkInterval}',
                        if_not_exists       => TRUE,
                        migrate_data        => TRUE
                    );
                    """);

                logger.LogInformation("[Timescale] Hypertable ready: {Table} (chunk={Chunk})",
                    table, chunkInterval);
            }
            catch (Exception ex)
            {
                logger.LogWarning("[Timescale] create_hypertable({Table}) failed: {Msg}",
                    table, ex.Message);
            }
        }

        // 4. Enable compression on sensor_readings (7-day policy)
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE sensor_readings SET (
                    timescaledb.compress,
                    timescaledb.compress_orderby    = 'time DESC',
                    timescaledb.compress_segmentby  = 'sensor_id'
                );
                SELECT add_compression_policy('sensor_readings',
                    INTERVAL '7 days', if_not_exists => TRUE);
                """);

            logger.LogInformation("[Timescale] Compression policy set on sensor_readings");
        }
        catch (Exception ex)
        {
            logger.LogDebug("[Timescale] Compression setup skipped: {Msg}", ex.Message);
        }
    }
}
