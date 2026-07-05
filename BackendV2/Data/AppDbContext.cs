using BackendV2.Models;
using Microsoft.EntityFrameworkCore;

namespace BackendV2.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Node> Nodes => Set<Node>();
    public DbSet<Camera> Cameras => Set<Camera>();
    public DbSet<Sensor> Sensors => Set<Sensor>();
    public DbSet<Reading> Readings => Set<Reading>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Node>(e =>
        {
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<Camera>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne<Node>().WithMany().HasForeignKey(x => x.NodeId);
        });

        modelBuilder.Entity<Sensor>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne<Node>().WithMany().HasForeignKey(x => x.NodeId);
        });

        modelBuilder.Entity<Reading>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne<Sensor>().WithMany().HasForeignKey(x => x.SensorId);
            e.HasIndex(x => new { x.SensorId, x.Timestamp });
        });
    }
}
