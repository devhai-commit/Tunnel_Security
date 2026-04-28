namespace Backend.Services.Caching;

/// <summary>
/// Lightweight sensor metadata cached in IMemoryCache (key: "sensor:{id}", TTL 5 min).
/// Does NOT include CurrentValue/CurrentLevel/LastReading — those are live data, not config.
/// </summary>
public sealed record SensorCacheEntry(
    string  Id,
    string  NodeId,
    string  Type,
    string  Name,
    string? Unit,
    byte?   SensorByteId,
    double? WarningThreshold,
    double? CriticalThreshold);

/// <summary>
/// Lightweight node metadata cached in IMemoryCache (key: "node:{id}", TTL 5 min).
/// NodeStatus staleness (up to 5 min) is acceptable — DeviceStatusChanged event is authoritative.
/// </summary>
public sealed record NodeCacheEntry(
    string  Id,
    string  Name,
    string  Status,
    byte?   NodeByteId);
