using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data;

/// <summary>
/// SQLite DbContext — relational/config topology.
/// Time-series data (sensor_readings, node_heartbeats, etc.) live in
/// TimeSeriesDbContext (PostgreSQL + TimescaleDB).
/// </summary>
public class TunnelDbContext : DbContext
{
    public TunnelDbContext(DbContextOptions<TunnelDbContext> options) : base(options) { }

    // ── Topology ──────────────────────────────────────────────────────────────
    public DbSet<Station>      Stations  => Set<Station>();
    public DbSet<Line>         Lines     => Set<Line>();
    public DbSet<Node>         Nodes     => Set<Node>();
    public DbSet<Sensor>       Sensors   => Set<Sensor>();

    // ── Camera (snapshots & clips remain here) ────────────────────────────────
    public DbSet<CameraDevice>   Cameras         => Set<CameraDevice>();
    public DbSet<CameraSnapshot> CameraSnapshots => Set<CameraSnapshot>();
    public DbSet<VideoClip>      VideoClips      => Set<VideoClip>();

    // ── Alerts ────────────────────────────────────────────────────────────────
    public DbSet<Alert>     Alerts     => Set<Alert>();
    public DbSet<AlertNote> AlertNotes => Set<AlertNote>();

    // ── Users ─────────────────────────────────────────────────────────────────
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Station → Lines (1:N)
        modelBuilder.Entity<Station>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.StationId);
        });

        // Line → Nodes (1:N)
        modelBuilder.Entity<Line>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasMany(x => x.Nodes).WithOne().HasForeignKey(n => n.LineId);
        });

        // Node → Sensors (1:N)
        modelBuilder.Entity<Node>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasMany(x => x.Sensors).WithOne().HasForeignKey(s => s.NodeId);
        });

        modelBuilder.Entity<Sensor>(e =>
        {
            e.HasKey(x => x.Id);
        });

        // CameraDevice
        modelBuilder.Entity<CameraDevice>(e =>
        {
            e.HasKey(x => x.Id);
        });

        // CameraSnapshot — indexed by camera + time
        modelBuilder.Entity<CameraSnapshot>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CameraId, x.Timestamp });
        });

        // VideoClip
        modelBuilder.Entity<VideoClip>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CameraId, x.StartTime });
        });

        // Alert → AlertNotes (1:N)
        modelBuilder.Entity<Alert>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasMany(x => x.Notes).WithOne().HasForeignKey(n => n.AlertId);
            e.HasIndex(x => new { x.SensorId, x.CreatedAt });
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<AlertNote>(e =>
        {
            e.HasKey(x => x.Id);
        });

        // User — unique username
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
        });
    }
}
