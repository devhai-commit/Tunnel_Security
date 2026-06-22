using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TunnelSecurity.Data.Auth;

public class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AuthDbContext>();

        optionsBuilder.UseSqlServer(
            "Server=.\\SQLEXPRESS;Database=TunnelSecurity;Trusted_Connection=True;Encrypt=False;TrustServerCertificate=True;MultipleActiveResultSets=True"
        );

        return new AuthDbContext(optionsBuilder.Options);
    }
}
