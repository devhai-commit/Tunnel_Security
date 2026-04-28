namespace Backend.Models;

public enum UserRole
{
    Admin,
    Operator,
    Viewer
}

/// <summary>
/// Người dùng hệ thống — ánh xạ bảng 'users' (SQLite)
/// </summary>
public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Username { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public string FullName { get; set; } = default!;
    public UserRole Role { get; set; } = UserRole.Viewer;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
