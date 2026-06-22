using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Microsoft.AspNetCore.Identity;
using TunnelSecurity.Data.Auth;
using TunnelSecurity.Data.Auth.Models;
using TunnelSecurity.Auth;
using TunnelSecurity.Data.Auth.Services;

namespace TunnelSecurity.Backend.Extensions
{
    public static class AuthExtensions
    {
        public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration configuration)
        {
            // DB
            services.AddDbContext<AuthDbContext>(options =>
                options.UseSqlServer(
                    configuration.GetConnectionString("DefaultConnection")
                    ?? configuration.GetConnectionString("AuthDb")
                    ?? throw new InvalidOperationException("Connection string is not configured.")));

            // jwt settings
            services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
            services.Configure<LoginSecuritySettings>(configuration.GetSection("LoginSecurity"));

            var jwt = configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();

            // Validate JWT configuration early to avoid obscure runtime errors
            if (string.IsNullOrWhiteSpace(jwt.Secret))
                throw new InvalidOperationException("JWT secret is not configured. Set configuration key 'Jwt:Secret' (use a strong random value).");

            // require a minimum key length (at least 128 bits recommended)
            var key = Encoding.UTF8.GetBytes(jwt.Secret);
            if (key.Length < 16)
                throw new InvalidOperationException("JWT secret is too short. Use a secret at least 16 bytes long.");

            // password hasher - use application User model
            services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

            services.AddScoped<TunnelSecurity.Data.Auth.Services.IAuthService, TunnelSecurity.Data.Auth.Services.AuthService>();

            // authentication
            var signingKey = new SymmetricSecurityKey(key);

            var env = configuration.GetValue<string>("ASPNETCORE_ENVIRONMENT") ?? "Production";
            var requireHttps = !string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = requireHttps;
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = !string.IsNullOrEmpty(jwt.Issuer),
                    ValidateAudience = !string.IsNullOrEmpty(jwt.Audience),
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = signingKey,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

            return services;
        }
    }
}
