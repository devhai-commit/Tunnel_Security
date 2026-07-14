namespace BackendV2.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Username { get; set; }
    public string? PasswordHash { get; set; }
    public string? FullName { get; set; }
    public Guid? RoleId { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedLoginAttempts { get; set; }
    public DateTimeOffset? LastFailedLoginAt { get; set; }
    public DateTimeOffset? LockoutEndAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Role? Role { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
