using Backend.Data;
using Backend.Models.TimeSeries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReadingsController : ControllerBase
{
    private readonly TunnelDbContext _db;
    private readonly TimeSeriesDbContext? _ts;

    public ReadingsController(TunnelDbContext db, TimeSeriesDbContext? ts = null)
    {
        _db = db;
        _ts = ts;
    }

    // GET /api/readings/{sensorId}?from=&to=&limit=500
    [HttpGet("{sensorId}")]
    public async Task<IActionResult> GetReadings(
        string sensorId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 500)
    {
        var sensor = await _db.Sensors.FirstOrDefaultAsync(s => s.Id == sensorId);
        if (sensor == null) return NotFound("Sensor not found");

        if (_ts != null)
        {
            var query = _ts.SensorReadings.Where(r => r.SensorId == sensorId);
            if (from.HasValue) query = query.Where(r => r.Time >= from.Value);
            if (to.HasValue)   query = query.Where(r => r.Time <= to.Value);

            var readings = await query
                .OrderByDescending(r => r.Time)
                .Take(Math.Min(limit, 5000))
                .Select(r => new { r.Id, r.Value, unit = sensor.Unit, timestamp = r.Time,
                                   r.Level, r.Seq, r.Crc8Ok })
                .ToListAsync();

            return Ok(new { sensor.Id, sensor.Name, sensor.Unit, readings });
        }

        // Fallback — return current value only when TimescaleDB unavailable
        return Ok(new
        {
            sensor.Id, sensor.Name, sensor.Unit,
            readings = sensor.CurrentValue.HasValue
                ? new[] { new { Id = 0L, Value = sensor.CurrentValue.Value,
                                unit = sensor.Unit, timestamp = sensor.LastReading,
                                Level = sensor.CurrentLevel, Seq = (byte?)null, Crc8Ok = (bool?)null } }
                : Array.Empty<object>()
        });
    }

    // GET /api/readings/{sensorId}/latest?count=50
    [HttpGet("{sensorId}/latest")]
    public async Task<IActionResult> GetLatest(string sensorId, [FromQuery] int count = 50)
    {
        if (_ts != null)
        {
            var readings = await _ts.SensorReadings
                .Where(r => r.SensorId == sensorId)
                .OrderByDescending(r => r.Time)
                .Take(Math.Min(count, 1000))
                .Select(r => new { r.Value, timestamp = r.Time, r.Level })
                .ToListAsync();
            return Ok(readings);
        }

        var sensor = await _db.Sensors.FirstOrDefaultAsync(s => s.Id == sensorId);
        if (sensor == null) return NotFound();
        return Ok(new[] { new { Value = sensor.CurrentValue ?? 0,
                                timestamp = sensor.LastReading ?? DateTime.UtcNow,
                                Level = sensor.CurrentLevel ?? "Normal" } });
    }

    // GET /api/readings/node/{nodeId}?from=&to=&limit=200
    [HttpGet("node/{nodeId}")]
    public async Task<IActionResult> GetNodeReadings(
        string nodeId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 200)
    {
        if (_ts == null) return Ok(Array.Empty<object>());

        var query = _ts.SensorReadings.Where(r => r.NodeId == nodeId);
        if (from.HasValue) query = query.Where(r => r.Time >= from.Value);
        if (to.HasValue)   query = query.Where(r => r.Time <= to.Value);

        var readings = await query
            .OrderByDescending(r => r.Time)
            .Take(Math.Min(limit, 2000))
            .Select(r => new { r.SensorId, r.Value, timestamp = r.Time, r.Level })
            .ToListAsync();

        return Ok(readings);
    }

    // GET /api/readings/{sensorId}/stats?from=&to=
    [HttpGet("{sensorId}/stats")]
    public async Task<IActionResult> GetStats(
        string sensorId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        if (_ts == null)
        {
            var sensor = await _db.Sensors.FirstOrDefaultAsync(s => s.Id == sensorId);
            if (sensor == null) return NotFound();
            return Ok(new { count = sensor.CurrentValue.HasValue ? 1 : 0,
                            min = sensor.CurrentValue, max = sensor.CurrentValue,
                            avg = sensor.CurrentValue });
        }

        var q = _ts.SensorReadings.Where(r => r.SensorId == sensorId);
        if (from.HasValue) q = q.Where(r => r.Time >= from.Value);
        if (to.HasValue)   q = q.Where(r => r.Time <= to.Value);

        var count = await q.CountAsync();
        if (count == 0) return Ok(new { count = 0 });

        var stats = await q.GroupBy(_ => 1).Select(g => new
        {
            count    = g.Count(),
            min      = g.Min(r => r.Value),
            max      = g.Max(r => r.Value),
            avg      = g.Average(r => r.Value),
            warnings = g.Count(r => r.Level == ReadingLevel.Warning),
            criticals= g.Count(r => r.Level == ReadingLevel.Critical)
        }).FirstAsync();

        return Ok(stats);
    }

    // GET /api/readings/{sensorId}/hourly?from=&to=
    [HttpGet("{sensorId}/hourly")]
    public async Task<IActionResult> GetHourlyAggregates(
        string sensorId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        from ??= DateTime.UtcNow.AddDays(-7);
        to   ??= DateTime.UtcNow;

        if (_ts == null) return Ok(Array.Empty<object>());

        // Pull in-memory then group — for a production TimescaleDB
        // this can be replaced with time_bucket() via raw SQL
        var readings = await _ts.SensorReadings
            .Where(r => r.SensorId == sensorId && r.Time >= from && r.Time <= to)
            .Select(r => new { r.Time, r.Value })
            .ToListAsync();

        var hourly = readings
            .GroupBy(r => new DateTime(r.Time.Year, r.Time.Month, r.Time.Day, r.Time.Hour, 0, 0))
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                hour  = g.Key,
                min   = g.Min(r => r.Value),
                max   = g.Max(r => r.Value),
                avg   = Math.Round(g.Average(r => r.Value), 2),
                count = g.Count()
            });

        return Ok(hourly);
    }

    // GET /api/readings/{sensorId}/heartbeats?from=&to=&limit=100
    [HttpGet("{sensorId}/heartbeats")]
    public async Task<IActionResult> GetHeartbeats(
        string nodeId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 100)
    {
        if (_ts == null) return Ok(Array.Empty<object>());

        var q = _ts.NodeHeartbeats.Where(h => h.NodeId == nodeId);
        if (from.HasValue) q = q.Where(h => h.Time >= from.Value);
        if (to.HasValue)   q = q.Where(h => h.Time <= to.Value);

        var result = await q.OrderByDescending(h => h.Time)
            .Take(Math.Min(limit, 1000))
            .Select(h => new { h.NodeId, timestamp = h.Time, h.Status, h.BatteryLevel, h.Rssi })
            .ToListAsync();

        return Ok(result);
    }
}
