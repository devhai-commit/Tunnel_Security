using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.TimeSeries;

/// <summary>
/// TimescaleDB hypertable — sensor_frames_raw
/// Stores the raw binary frame exactly as received over WebSocket/UART.
/// Format: 0xAA | NODE_BYTE | SENSOR_BYTE | SEQ | VALUE[4] | CRC8 | 0xBB (9 bytes → 18 hex chars)
/// Extended frames (with timestamp) stored in 29-char hex.
/// </summary>
[Table("sensor_frames_raw")]
public class SensorFrameRaw
{
    [Column("id")]
    public long Id { get; set; }

    [Column("time")]
    public DateTime Time { get; set; } = DateTime.UtcNow;

    [Column("node_byte_id")]
    public byte NodeByteId { get; set; }

    [Column("sensor_byte_id")]
    public byte SensorByteId { get; set; }

    /// <summary>Sequence counter 0–255</summary>
    [Column("seq")]
    public byte Seq { get; set; }

    /// <summary>Full frame as uppercase hex string (max 29 chars for extended frame)</summary>
    [Column("raw_hex")]
    public string RawHex { get; set; } = default!;

    [Column("crc8_valid")]
    public bool Crc8Valid { get; set; }

    /// <summary>Parse/CRC error description; null when frame is valid</summary>
    [Column("error_msg")]
    public string? ErrorMsg { get; set; }
}
