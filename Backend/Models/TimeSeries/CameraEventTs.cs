using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.TimeSeries;

public enum CameraEventType
{
    MotionDetected,
    PersonDetected,
    VehicleDetected,
    Intrusion,
    FireDetected,
    SmokeDetected,
    Anomaly
}

/// <summary>
/// TimescaleDB hypertable — camera_events
/// One row per CV-detection event (person, vehicle, intrusion, fire/smoke…).
/// Partitioned by 'time'.
/// </summary>
[Table("camera_events")]
public class CameraEventTs
{
    [Column("id")]
    public long Id { get; set; }

    [Column("time")]
    public DateTime Time { get; set; } = DateTime.UtcNow;

    [Column("camera_id")]
    public string CameraId { get; set; } = default!;

    [Column("node_id")]
    public string? NodeId { get; set; }

    [Column("event_type")]
    public CameraEventType EventType { get; set; }

    /// <summary>Model confidence score 0–1</summary>
    [Column("confidence")]
    public double? Confidence { get; set; }

    /// <summary>Detected object class label (e.g. "person", "car")</summary>
    [Column("object_class")]
    public string? ObjectClass { get; set; }

    [Column("is_intrusion")]
    public bool IsIntrusion { get; set; }

    /// <summary>True if this event generated an Alert row</summary>
    [Column("generated_alert")]
    public bool GeneratedAlert { get; set; }

    /// <summary>Bounding box in normalised coordinates (0–1)</summary>
    [Column("bbox_x")] public float? BboxX { get; set; }
    [Column("bbox_y")] public float? BboxY { get; set; }
    [Column("bbox_w")] public float? BboxW { get; set; }
    [Column("bbox_h")] public float? BboxH { get; set; }

    /// <summary>Relative path to the snapshot image for this event</summary>
    [Column("image_path")]
    public string? ImagePath { get; set; }

    /// <summary>Soft reference to alerts.id — not a FK to allow independent deletion</summary>
    [Column("alert_id")]
    public string? AlertId { get; set; }
}
