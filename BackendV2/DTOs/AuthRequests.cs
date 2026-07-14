namespace BackendV2.DTOs;

public class RegisterRequest
{
    public string Username { get; set; } = null!;
    public string FullName { get; set; } = string.Empty;
    public string Password { get; set; } = null!;
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string? UsernameOrEmail { get; set; }
    public string Password { get; set; } = null!;
}

public class RefreshRequest
{
    public string RefreshToken { get; set; } = null!;
}

public class RevokeRequest
{
    public string RefreshToken { get; set; } = null!;
}
