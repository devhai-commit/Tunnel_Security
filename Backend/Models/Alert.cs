namespace Backend.Models;

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public enum AlertStatus
{
    Active,
    Acknowledged,
    Resolved,
    Closed
}

public enum AlertCategory
{
    Sensor,
    Camera,
    Device,
    System,
    Other
}

public class Alert
{
    public long Id { get; set; }

    public string Title { get; set; } = default!;
    public string? Description { get; set; }

    public AlertCategory Category { get; set; } = AlertCategory.Sensor;
    public AlertSeverity Severity { get; set; }
    public AlertStatus Status { get; set; } = AlertStatus.Active;

    // FK references (string IDs matching topology tables)
    public string? NodeId { get; set; }
    public string? SensorId { get; set; }
    public string? CameraId { get; set; }
    public string? StationId { get; set; }

    // Trigger context
    public double? SensorValue { get; set; }
    public double? Threshold { get; set; }
    public string? Unit { get; set; }

    // Legacy field kept for compatibility
    public string Message { get; set; } = default!;

    // Timestamps & attribution
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? ClosedBy { get; set; }

    // Navigation
    public List<AlertNote> Notes { get; set; } = new();
}
