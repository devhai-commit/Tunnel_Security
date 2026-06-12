using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.TimeSeries;

/// <summary>
/// TimescaleDB hypertable — node_heartbeats
/// One row per heartbeat tick per node; partitioned by 'time'.
/// </summary>
[Table("node_heartbeats")]
public class NodeHeartbeatTs
{
    [Column("id")]
    public long Id { get; set; }

    [Column("time")]
    public DateTime Time { get; set; } = DateTime.UtcNow;

    [Column("node_id")]
    public string NodeId { get; set; } = default!;

    /// <summary>Wire-protocol byte ID (1–10)</summary>
    [Column("node_byte_id")]
    public short NodeByteId { get; set; }

    [Column("status")]
    public NodeStatus Status { get; set; }

    [Column("battery_level")]
    public double? BatteryLevel { get; set; }

    [Column("rssi")]
    public int? Rssi { get; set; }

    [Column("ip_address")]
    public string? IpAddress { get; set; }

    [Column("firmware_version")]
    public string? FirmwareVersion { get; set; }

    /// <summary>Node uptime in seconds since last reboot</summary>
    [Column("uptime_sec")]
    public int? UptimeSec { get; set; }
}
