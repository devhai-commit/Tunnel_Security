using Backend.Data;
using Backend.Models;
using Backend.Models.TimeSeries;
using Backend.Services.Caching;

namespace Backend.Services;

/// <summary>
/// Shared by all ingestion services: resolves sensor metadata (cache-first),
/// persists the reading, then enqueues a broadcast message.
/// SignalR fan-out is handled by SensorBroadcastQueue (singleton), not here.
/// </summary>
public sealed class SensorBroadcaster
{
    private readonly TimeSeriesDbContext? _ts;
    private readonly ISensorRepository    _repo;
    private readonly SensorBroadcastQueue _queue;
    private readonly ILogger<SensorBroadcaster> _logger;

    public SensorBroadcaster(
        ISensorRepository    repo,
        SensorBroadcastQueue queue,
        ILogger<SensorBroadcaster> logger,
        TimeSeriesDbContext? ts = null)
    {
        _repo   = repo;
        _queue  = queue;
        _logger = logger;
        _ts     = ts;
    }

    public async Task ProcessReadingAsync(string sensorId, double value, CancellationToken ct = default)
    {
        var sensor = await _repo.GetSensorAsync(sensorId, ct);
        if (sensor is null)
        {
            // Sensor not in DB — forward with inferred metadata so simulator data reaches clients.
            var inferredType = InferTypeFromSensorId(sensorId);
            var nodeId       = ExtractNodeFromSensorId(sensorId);
            _queue.Enqueue(new SensorBroadcastMessage(
                Id:           sensorId,
                NodeId:       nodeId,
                Type:         inferredType,
                Name:         sensorId,
                CurrentValue: value,
                Unit:         UnitForType(inferredType),
                LastReading:  DateTime.UtcNow,
                Level:        "Normal",
                NodeStatus:   "online",
                NodeName:     nodeId,
                NodeByteId:   null,
                SensorByteId: null));
            return;
        }

        var level = ComputeLevel(value, sensor.WarningThreshold, sensor.CriticalThreshold);
        var now   = DateTime.UtcNow;
        var node  = await _repo.GetNodeAsync(sensor.NodeId, ct);

        // Enqueue broadcast before DB writes so clients receive data without waiting for persistence.
        _queue.Enqueue(new SensorBroadcastMessage(
            Id:           sensor.Id,
            NodeId:       sensor.NodeId,
            Type:         sensor.Type,
            Name:         sensor.Name,
            CurrentValue: value,
            Unit:         sensor.Unit,
            LastReading:  now,
            Level:        level,
            NodeStatus:   node?.Status,
            NodeName:     node?.Name,
            NodeByteId:   node?.NodeByteId,
            SensorByteId: sensor.SensorByteId));

        // Persist after broadcast (best-effort — does not block fan-out).
        await _repo.UpdateSensorCurrentValueAsync(sensorId, value, level, now, ct);

        if (_ts is not null)
        {
            try
            {
                _ts.SensorReadings.Add(new SensorReadingTs
                {
                    Time         = now,
                    SensorId     = sensor.Id,
                    NodeId       = sensor.NodeId,
                    SensorByteId = (short)(sensor.SensorByteId ?? 0),
                    Value        = value,
                    Level        = Enum.Parse<ReadingLevel>(level)
                });
                await _ts.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("[Broadcaster] TimescaleDB write failed: {Msg}", ex.Message);
            }
        }
    }

    /// <summary>
    /// Store a raw binary frame in the sensor_frames_raw hypertable (TimescaleDB only).
    /// </summary>
    public async Task StoreRawFrameAsync(SensorFrameRaw frame, CancellationToken ct = default)
    {
        if (_ts is null) return;
        try
        {
            _ts.SensorFramesRaw.Add(frame);
            await _ts.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[Broadcaster] Raw frame store failed: {Msg}", ex.Message);
        }
    }

    private static string ComputeLevel(double value, double? warnThreshold, double? critThreshold)
    {
        if (critThreshold.HasValue && value >= critThreshold.Value) return nameof(ReadingLevel.Critical);
        if (warnThreshold.HasValue && value >= warnThreshold.Value) return nameof(ReadingLevel.Warning);
        return nameof(ReadingLevel.Normal);
    }

    private static string InferTypeFromSensorId(string id) =>
        id.EndsWith("-TEMP",  StringComparison.OrdinalIgnoreCase) ? "temperature" :
        id.EndsWith("-HUM",   StringComparison.OrdinalIgnoreCase) ? "humidity"    :
        id.EndsWith("-LIGHT", StringComparison.OrdinalIgnoreCase) ? "light"       :
        id.EndsWith("-RADAR", StringComparison.OrdinalIgnoreCase) ? "radar"       :
        id.EndsWith("-VIB",   StringComparison.OrdinalIgnoreCase) ? "vibration"   :
        id.EndsWith("-WATER", StringComparison.OrdinalIgnoreCase) ? "waterlevel"  :
        "unknown";

    private static string ExtractNodeFromSensorId(string id)
    {
        var idx = id.LastIndexOf('-');
        return idx > 0 ? id[..idx] : id;
    }

    private static string? UnitForType(string type) => type switch
    {
        "temperature" => "°C",
        "humidity"    => "%",
        "light"       => "lux",
        "radar"       => "m",
        "vibration"   => "m/s",
        "waterlevel"  => "mm",
        _             => null
    };
}
