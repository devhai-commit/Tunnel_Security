namespace TunnelSecurity.Auth
{
    public class JwtSettings
    {
        public string Secret { get; set; } = null!;
        public string Issuer { get; set; } = null!;
        public string Audience { get; set; } = null!;
        public int AccessTokenMinutes { get; set; } = 15;
        public int RefreshTokenDays { get; set; } = 30;
    }
}