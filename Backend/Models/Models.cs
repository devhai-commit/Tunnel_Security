namespace Backend.Models;

#region Enums

public enum JoinRequestStatus
{
    Pending,
    Accepted,
    Rejected,
    Expired
}

public enum NodeStatus
{
    Online,
    Warning,
    Critical,
    Offline,
    Maintenance
}

public enum SensorType
{
    Radar,          // Radar biến dạng (mm)
    Vibration,      // Rung động (mm/s)
    SmokeFire,      // Khói/Lửa (%)
    Temperature,    // Nhiệt độ (°C)
    Humidity,       // Độ ẩm (%)
    Gas,            // Khí gas (ppm)
    Pressure,       // Áp suất
    WaterLevel,     // Mực nước
    Motion          // Chuyển động
}

#endregion

#region Station (Trạm)

/// <summary>
/// Trạm giám sát - chứa nhiều tuyến cống
/// </summary>
public class Station
{
    public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string District { get; set; } = default!;
    
    // Tọa độ trung tâm trạm
    public double CenterLng { get; set; }
    public double CenterLat { get; set; }
    
    // Bounding box của khu vực trạm quản lý
    public double MinLng { get; set; }
    public double MinLat { get; set; }
    public double MaxLng { get; set; }
    public double MaxLat { get; set; }
    
    // Danh sách các tuyến cống thuộc trạm này
    public List<Line> Lines { get; set; } = new();
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

#endregion

#region Line (Tuyến cống)

/// <summary>
/// Tuyến cống - mỗi trạm có nhiều tuyến, mỗi tuyến có nhiều nút dọc theo
/// </summary>
public class Line
{
    public string Id { get; set; } = default!;
    public string StationId { get; set; } = default!;
    public string Code { get; set; } = default!; // L1, L2, L3
    public string Name { get; set; } = default!; // "Tuyến cống số 1"
    public string? Description { get; set; }
    
    // Tọa độ đầu và cuối tuyến (để vẽ đường thẳng)
    public double StartLng { get; set; }
    public double StartLat { get; set; }
    public double EndLng { get; set; }
    public double EndLat { get; set; }
    
    // Chiều dài tuyến (m)
    public double? Length { get; set; }
    
    // Trạng thái
    public string Status { get; set; } = "active"; // active, inactive, maintenance
    
    // Danh sách các nút dọc theo tuyến này
    public List<Node> Nodes { get; set; } = new();
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

#endregion

#region Node (Nút giám sát)

/// <summary>
/// Nút giám sát - nằm dọc theo tuyến cống, chứa nhiều sensor
/// </summary>
public class Node
{
    public string Id { get; set; } = default!;
    public string LineId { get; set; } = default!;
    public string Code { get; set; } = default!; // N1, N2, N3
    public string Name { get; set; } = default!; // "Nút 1"
    public string? Description { get; set; }
    
    // Tọa độ nút (nằm trên tuyến)
    public double Lng { get; set; }
    public double Lat { get; set; }
    
    // Vị trí trên canvas/map (pixel)
    public double? MapX { get; set; }
    public double? MapY { get; set; }
    
    // Trạng thái nút
    public NodeStatus Status { get; set; } = NodeStatus.Offline;
    public DateTime? LastOnline { get; set; }
    
    /// <summary>Wire-protocol byte ID của node (1–10) — dùng trong binary frame</summary>
    public byte? NodeByteId { get; set; }

    /// <summary>Khoảng cách từ đầu tuyến đến nút (m)</summary>
    public double? DistanceM { get; set; }

    // Thông tin thiết bị
    public string? HardwareId { get; set; }
    public string? Mac { get; set; }
    public string? IpAddress { get; set; }
    public string? FirmwareVersion { get; set; }
    public bool IsHub { get; set; } // Nút hub/gateway
    
    // Pin & tín hiệu
    public double? BatteryLevel { get; set; }
    public int? RSSI { get; set; }
    
    // Danh sách các sensor thuộc nút này
    public List<Sensor> Sensors { get; set; } = new();
    
    // Camera gắn trên nút (nếu có)
    public string? CameraId { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

#endregion

#region Sensor (Cảm biến)

/// <summary>
/// Cảm biến - gắn trên nút giám sát
/// </summary>
public class Sensor
{
    public string Id { get; set; } = default!;
    public string NodeId { get; set; } = default!;

    /// <summary>Wire-protocol byte ID của sensor trong node (1–7)</summary>
    public byte? SensorByteId { get; set; }

    public SensorType Type { get; set; }
    public string Name { get; set; } = default!;
    public string Unit { get; set; } = string.Empty; // °C, %, mm, mm/s

    // Ngưỡng cảnh báo
    public double? WarningThreshold { get; set; }
    public double? CriticalThreshold { get; set; }

    // Giá trị hiện tại (cache — cập nhật mỗi reading)
    public double? CurrentValue { get; set; }
    public string? CurrentLevel { get; set; } // "Normal" | "Warning" | "Critical"
    public DateTime? LastReading { get; set; }

    // Cấu hình
    public bool IsEnabled { get; set; } = true;
    public int SamplingRate { get; set; } = 1; // Hz
    public double? SamplingRateHz { get; set; } // precise rate nếu cần

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

#endregion

#region Device Join Request

/// <summary>
/// Yêu cầu gia nhập mạng từ thiết bị phần cứng chưa được cấp phép.
/// Thiết bị gửi JOIN_REQUEST qua WebSocket, Server lưu vào DB và chờ phê duyệt.
/// </summary>
public class DevicePendingJoin
{
    public int Id { get; set; }

    /// <summary>Địa chỉ MAC của thiết bị, dạng "AA:BB:CC:DD:EE:FF"</summary>
    public string MacAddress { get; set; } = string.Empty;

    /// <summary>Hardware ID (uint32) nhận từ frame JOIN_REQUEST</summary>
    public uint HardwareId { get; set; }

    /// <summary>Phiên bản firmware, dạng "major.minor.patch"</summary>
    public string FirmwareVersion { get; set; } = string.Empty;

    public JoinRequestStatus Status { get; set; } = JoinRequestStatus.Pending;

    /// <summary>NodeByteId (1–10) được gán khi phê duyệt</summary>
    public byte? AssignedNodeByteId { get; set; }

    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RespondedAt { get; set; }
    public string? RejectionReason { get; set; }
}

#endregion
