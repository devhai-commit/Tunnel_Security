namespace Backend.Models;

/// <summary>
/// Ghi chú / bình luận trên một cảnh báo — ánh xạ bảng 'alert_notes' (SQLite)
/// </summary>
public class AlertNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK → alerts.id</summary>
    public long AlertId { get; set; }

    public string Content { get; set; } = default!;

    /// <summary>FK → users.id; null = hệ thống tự tạo</summary>
    public string? AuthorId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
