using Backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Backend.Services.Caching;

public sealed class SensorRepository : ISensorRepository
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly TunnelDbContext _db;
    private readonly IMemoryCache    _cache;

    public SensorRepository(TunnelDbContext db, IMemoryCache cache)
    {
        _db    = db;
        _cache = cache;
    }

    public async Task<SensorCacheEntry?> GetSensorAsync(string sensorId, CancellationToken ct = default)
    {
        var key = $"sensor:{sensorId}";
        if (_cache.TryGetValue(key, out SensorCacheEntry? hit))
            return hit;

        var s = await _db.Sensors
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sensorId, ct);

        if (s is null) return null;

        var entry = new SensorCacheEntry(
            s.Id, s.NodeId, s.Type.ToString(), s.Name, s.Unit,
            s.SensorByteId, s.WarningThreshold, s.CriticalThreshold);

        _cache.Set(key, entry, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1
        });
        return entry;
    }

    public async Task<NodeCacheEntry?> GetNodeAsync(string nodeId, CancellationToken ct = default)
    {
        var key = $"node:{nodeId}";
        if (_cache.TryGetValue(key, out NodeCacheEntry? hit))
            return hit;

        var n = await _db.Nodes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == nodeId, ct);

        if (n is null) return null;

        var entry = new NodeCacheEntry(n.Id, n.Name, n.Status.ToString(), n.NodeByteId);
        _cache.Set(key, entry, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1
        });
        return entry;
    }

    public async Task UpdateSensorCurrentValueAsync(
        string sensorId, double value, string level, DateTime timestamp, CancellationToken ct = default)
    {
        await _db.Sensors
            .Where(s => s.Id == sensorId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CurrentValue, value)
                .SetProperty(x => x.CurrentLevel, level)
                .SetProperty(x => x.LastReading,  timestamp),
                ct);
    }

    public void InvalidateSensor(string sensorId) => _cache.Remove($"sensor:{sensorId}");
    public void InvalidateNode(string nodeId)     => _cache.Remove($"node:{nodeId}");
}
