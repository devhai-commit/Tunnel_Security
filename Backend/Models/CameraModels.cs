namespace Backend.Models;

public enum CameraProtocol
{
    RTSP,
    HTTP,
    WebSocket
}

public enum CameraStatus
{
    Online,
    Offline,
    Error
}

/// <summary>
/// Camera gắn trên nút giám sát
/// </summary>
public class CameraDevice
{
    public string Id { get; set; } = default!;
    public string? NodeId { get; set; }
    public string Name { get; set; } = default!;
    public string? StreamUrl { get; set; }
    public CameraProtocol Protocol { get; set; } = CameraProtocol.RTSP;
    public CameraStatus Status { get; set; } = CameraStatus.Offline;

    // Thông số kỹ thuật
    public string? Resolution { get; set; }  // e.g. "1280x720"
    public int? Fps { get; set; }
    public string? Codec { get; set; }       // e.g. "H.264", "H.265"

    // Tính năng
    public bool IrEnabled { get; set; }
    public bool HdrEnabled { get; set; }
    public bool IsRecording { get; set; }

    public DateTime? LastFrameTime { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Ảnh chụp từ camera tại một thời điểm
/// </summary>
public class CameraSnapshot
{
    public long Id { get; set; }
    public string CameraId { get; set; } = default!;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string FilePath { get; set; } = default!;
    public string? ThumbnailPath { get; set; }
    public string? DetectionType { get; set; }
    public double? Confidence { get; set; }
    public string? Metadata { get; set; } // JSON metadata
}

/// <summary>
/// Đoạn video được ghi lại (thường do alert kích hoạt)
/// </summary>
public class VideoClip
{
    public long Id { get; set; }
    public string CameraId { get; set; } = default!;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string FilePath { get; set; } = default!;
    public long SizeBytes { get; set; }
    public string? TriggerReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
