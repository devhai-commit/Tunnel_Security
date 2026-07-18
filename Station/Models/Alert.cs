using System;
using System.Collections.Generic;

namespace Station.Models
{
    public enum AlertState
    {
        Unprocessed = 0,
        Acknowledged = 1,
        InProgress = 2,
        Resolved = 3,
        Closed = 4
    }

    public enum AlertSeverity
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Critical = 3
    }

    public enum AlertCategory
    {
        Temperature,     // Nhiệt độ
        Humidity,        // Độ ẩm
        Radar,           // Radar phát hiện người
        Infrared,        // Cảm biến hồng ngoại (PIR)
        Light,           // Cảm biến ánh sáng
        Accelerometer,   // Cảm biến gia tốc
        WaterLevel,      // Mực nước
        Intrusion,       // Xâm nhập (camera AI)
        Equipment,       // Thiết bị
        Connection,      // Kết nối
        Other            // Khác
    }

    public class Alert
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public AlertCategory Category { get; set; }
        public AlertSeverity Severity { get; set; }
        public AlertState State { get; set; }
        
        // Location info
        public string LineId { get; set; } = string.Empty;
        public string LineName { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string NodeName { get; set; } = string.Empty;
        
        // Source
        public string? SensorId { get; set; }
        public string? SensorName { get; set; }
        public string? SensorType { get; set; }
        public double? SensorValue { get; set; }
        public string? SensorUnit { get; set; }
        public double? Threshold { get; set; }
        
        // Camera
        public string? CameraId { get; set; }
        public string? SnapshotPath { get; set; }
        public string? VideoClipPath { get; set; }
        
        // Timestamps
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? AcknowledgedAt { get; set; }
        public DateTimeOffset? ResolvedAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        
        // Processing info
        public string? AcknowledgedBy { get; set; }
        public string? ResolvedBy { get; set; }
        public string? ClosedBy { get; set; }
        public string? Note { get; set; }
        public List<AlertNote> Notes { get; set; } = new();
        
        // Coordinates for map
        public double? Lng { get; set; }
        public double? Lat { get; set; }
    }

    public class AlertNote
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Content { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
    }

    public class AlertStatistics
    {
        public int TotalAlerts { get; set; }
        public int UnprocessedCount { get; set; }
        public int AcknowledgedCount { get; set; }
        public int InProgressCount { get; set; }
        public int ResolvedCount { get; set; }
        public int ClosedCount { get; set; }
        
        public int CriticalCount { get; set; }
        public int HighCount { get; set; }
        public int MediumCount { get; set; }
        public int LowCount { get; set; }
        
        // Statistics by period
        public int TodayCount { get; set; }
        public int ThisWeekCount { get; set; }
        public int ThisMonthCount { get; set; }
        
        // By category
        public Dictionary<AlertCategory, int> ByCategory { get; set; } = new();
        public Dictionary<string, int> ByLine { get; set; } = new();
    }
}
