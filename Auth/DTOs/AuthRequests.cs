namespace TunnelSecurity.Auth.DTOs
{
    public record RegisterRequest(string Username, string Email, string Password);
    public record LoginRequest(string UsernameOrEmail, string Password);
    public record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, string? RefreshToken = null);
}