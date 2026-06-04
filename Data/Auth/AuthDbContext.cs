using Microsoft.EntityFrameworkCore;
using TunnelSecurity.Data.Auth.Models;

namespace TunnelSecurity.Data.Auth
{
    public class AuthDbContext : DbContext
    {
        public AuthDbContext(DbContextOptions<AuthDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; } = null!;
        public DbSet<Role> Roles { get; set; } = null!;
        public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
        public DbSet<FunctionGroup> FunctionGroups { get; set; } = null!;
        public DbSet<RoleFunctionGroup> RoleFunctionGroups { get; set; } = null!;
        public DbSet<AuditLog> AuditLogs { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<User>(b =>
            {
                b.HasKey(u => u.Id);
                b.HasIndex(u => u.Username).IsUnique();
                b.Property(u => u.FailedLoginAttempts).HasDefaultValue(0);
                b.HasOne(u => u.Role)
                    .WithMany(r => r.Users)
                    .HasForeignKey(u => u.RoleId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<Role>(b =>
            {
                b.HasKey(r => r.Id);
                b.HasIndex(r => r.Name).IsUnique();
                b.HasIndex(r => r.Code).IsUnique();
            });

            builder.Entity<FunctionGroup>(b =>
            {
                b.HasKey(f => f.Id);
                b.HasIndex(f => f.Code).IsUnique();
                b.HasIndex(f => f.Name).IsUnique();
            });

            builder.Entity<RoleFunctionGroup>(b =>
            {
                b.HasKey(rfg => new { rfg.RoleId, rfg.FunctionGroupId });
                b.HasOne(rfg => rfg.Role)
                    .WithMany(r => r.RoleFunctionGroups)
                    .HasForeignKey(rfg => rfg.RoleId)
                    .OnDelete(DeleteBehavior.Cascade);
                b.HasOne(rfg => rfg.FunctionGroup)
                    .WithMany(fg => fg.RoleFunctionGroups)
                    .HasForeignKey(rfg => rfg.FunctionGroupId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<AuditLog>(b =>
            {
                b.HasKey(log => log.Id);
                b.Property(log => log.Action).IsRequired();
                b.Property(log => log.TargetType).IsRequired();
                b.Property(log => log.TargetId).IsRequired();
                b.HasOne(log => log.ActorUser)
                    .WithMany()
                    .HasForeignKey(log => log.ActorUserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<RefreshToken>(b =>
            {
                b.HasKey(t => t.Id);
                b.HasOne(t => t.User).WithMany(u => u.RefreshTokens).HasForeignKey(t => t.UserId);
                b.HasIndex(t => t.TokenHash).IsUnique(false);
            });
        }
    }
}
