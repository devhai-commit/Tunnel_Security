namespace TunnelSecurity.Auth.DTOs
{
    public class LoginResponse
    {
        public string AccessToken { get; set; } = null!;
        public string RefreshToken { get; set; } = null!;
        public DateTimeOffset ExpiresAt { get; set; }
    }
}