namespace Backend.Models;

/// <summary>
/// Enqueued by SensorBroadcaster, drained by SensorBroadcastQueue.
/// Field names mirror the anonymous payload previously sent directly via SignalR
/// so the "SensorUpdated" wire contract is preserved byte-for-byte.
/// </summary>
public sealed record SensorBroadcastMessage(
    string    Id,
    string    NodeId,
    string    Type,
    string    Name,
    double    CurrentValue,
    string?   Unit,
    DateTime? LastReading,
    string    Level,
    string?   NodeStatus,
    string?   NodeName,
    byte?     NodeByteId,
    byte?     SensorByteId);
