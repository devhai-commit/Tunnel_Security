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
        modelBuilder.HasPostgresEnum<ReadingLevel>("reading_level");
        modelBuilder.HasPostgresEnum<NodeStatus>("node_status");
        modelBuilder.HasPostgresEnum<CameraEventType>("cam_event_type");

        // ── sensor_readings ──────────────────────────────────────────────────
        // TimescaleDB requires all unique constraints to include the partition
        // column (time), so we use a composite PK (id, time).
        modelBuilder.Entity<SensorReadingTs>(e =>
        {
            e.HasKey(x => new { x.Id, x.Time });
            e.Property(x => x.Id).UseIdentityByDefaultColumn();
            e.Property(x => x.Time).HasColumnType("timestamp with time zone").IsRequired();

            e.HasIndex(x => new { x.SensorId, x.Time });
            e.HasIndex(x => new { x.NodeId,   x.Time });
        });

        // ── sensor_frames_raw ────────────────────────────────────────────────
        modelBuilder.Entity<SensorFrameRaw>(e =>
        {
            e.HasKey(x => new { x.Id, x.Time });
            e.Property(x => x.Id).UseIdentityByDefaultColumn();
            e.Property(x => x.Time).HasColumnType("timestamp with time zone").IsRequired();
            e.Property(x => x.RawHex).HasMaxLength(29);
        });

        // ── node_heartbeats ──────────────────────────────────────────────────
        modelBuilder.Entity<NodeHeartbeatTs>(e =>
        {
            e.HasKey(x => new { x.Id, x.Time });
            e.Property(x => x.Id).UseIdentityByDefaultColumn();
            e.Property(x => x.Time).HasColumnType("timestamp with time zone").IsRequired();
            e.HasIndex(x => new { x.NodeId, x.Time });
        });

        // ── camera_events ─────────────────────────────────────────────────────
        modelBuilder.Entity<CameraEventTs>(e =>
        {
            e.HasKey(x => new { x.Id, x.Time });
            e.Property(x => x.Id).UseIdentityByDefaultColumn();
            e.Property(x => x.Time).HasColumnType("timestamp with time zone").IsRequired();
            e.HasIndex(x => new { x.CameraId, x.Time });
        });
    }
}
