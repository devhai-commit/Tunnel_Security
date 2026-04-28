namespace Backend.Services.Caching;

public interface ISensorRepository
{
    /// <summary>Cache-first lookup. Returns null if sensorId does not exist.</summary>
    Task<SensorCacheEntry?> GetSensorAsync(string sensorId, CancellationToken ct = default);

    /// <summary>Cache-first lookup. Returns null if nodeId does not exist.</summary>
    Task<NodeCacheEntry?> GetNodeAsync(string nodeId, CancellationToken ct = default);

    /// <summary>
    /// Updates Sensor.CurrentValue, CurrentLevel, LastReading via ExecuteUpdateAsync
    /// (no entity load, no change tracker, single parameterized UPDATE).
    /// </summary>
    Task UpdateSensorCurrentValueAsync(
        string sensorId,
        double value,
        string level,
        DateTime timestamp,
        CancellationToken ct = default);

    /// <summary>Evict sensor metadata from cache. Call after admin config writes.</summary>
    void InvalidateSensor(string sensorId);

    /// <summary>Evict node metadata from cache. Call after admin config writes.</summary>
    void InvalidateNode(string nodeId);
}
