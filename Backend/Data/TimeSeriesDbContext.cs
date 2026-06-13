using Backend.Models;
using Backend.Models.TimeSeries;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data;

/// <summary>
/// DbContext targeting PostgreSQL + TimescaleDB.
/// Manages the four time-series hypertables defined in the ERD:
///   sensor_readings, sensor_frames_raw, node_heartbeats, camera_events
/// </summary>
public class TimeSeriesDbContext : DbContext
{
    public TimeSeriesDbContext(DbContextOptions<TimeSeriesDbContext> options) : base(options) { }

    public DbSet<SensorReadingTs> SensorReadings  => Set<SensorReadingTs>();
    public DbSet<SensorFrameRaw>  SensorFramesRaw => Set<SensorFrameRaw>();
    public DbSet<NodeHeartbeatTs> NodeHeartbeats  => Set<NodeHeartbeatTs>();
    public DbSet<CameraEventTs>   CameraEvents    => Set<CameraEventTs>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── sensor_readings ──────────────────────────────────────────────────
        modelBuilder.Entity<SensorReadingTs>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityByDefaultColumn();
            e.Property(x => x.Time).HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.Level).HasConversion<string>();

            // Index for the most common query: sensor + time range
            e.HasIndex(x => new { x.SensorId, x.Time });
            e.HasIndex(x => new { x.NodeId,   x.Time });
        });

        // ── sensor_frames_raw ────────────────────────────────────────────────
        modelBuilder.Entity<SensorFrameRaw>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityByDefaultColumn();
            e.Property(x => x.Time).HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.RawHex).HasMaxLength(29);
        });

        // ── node_heartbeats ──────────────────────────────────────────────────
        modelBuilder.Entity<NodeHeartbeatTs>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityByDefaultColumn();
            e.Property(x => x.Time).HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.Status).HasConversion<string>();

            e.HasIndex(x => new { x.NodeId, x.Time });
        });

        // ── camera_events ─────────────────────────────────────────────────────
        modelBuilder.Entity<CameraEventTs>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityByDefaultColumn();
            e.Property(x => x.Time).HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.EventType).HasConversion<string>();

            e.HasIndex(x => new { x.CameraId, x.Time });
        });
    }
}
