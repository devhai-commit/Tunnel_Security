using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.TimeSeries;

public enum ReadingLevel
{
    Normal,
    Warning,
    Critical
}

/// <summary>
/// TimescaleDB hypertable — sensor_readings
/// Partitioned by 'time' column (1-day chunks).
/// Mapped from binary wire frame or simulated tick.
/// </summary>
[Table("sensor_readings")]
public class SensorReadingTs
{
    /// <summary>Surrogate PK — auto-assigned by PostgreSQL BIGSERIAL</summary>
    [Column("id")]
    public long Id { get; set; }

    /// <summary>Partition key — TIMESTAMPTZ; TimescaleDB chunks by this column</summary>
    [Column("time")]
    public DateTime Time { get; set; } = DateTime.UtcNow;

    [Column("node_id")]
    public string NodeId { get; set; } = default!;

    [Column("sensor_id")]
    public string SensorId { get; set; } = default!;

    /// <summary>Wire-protocol byte ID of the node (1–10)</summary>
    [Column("node_byte_id")]
    public short NodeByteId { get; set; }

    /// <summary>Wire-protocol byte ID of the sensor (1–7)</summary>
    [Column("sensor_byte_id")]
    public short SensorByteId { get; set; }

    [Column("value")]
    public double Value { get; set; }

    /// <summary>Rolling sequence counter 0–255 from the node firmware</summary>
    [Column("seq")]
    public short Seq { get; set; }

    [Column("level")]
    public ReadingLevel Level { get; set; } = ReadingLevel.Normal;

    [Column("crc8_ok")]
    public bool Crc8Ok { get; set; } = true;
}
