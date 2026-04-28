using Backend.Data;
using Backend.Services;
using Backend.Services.Caching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SensorsController : ControllerBase
{
    private readonly TunnelDbContext  _db;
    private readonly SensorBroadcaster _broadcaster;
    private readonly ISensorRepository _repo;

    public SensorsController(TunnelDbContext db, SensorBroadcaster broadcaster, ISensorRepository repo)
    {
        _db          = db;
        _broadcaster  = broadcaster;
        _repo        = repo;
    }

    public class MeasurementRequest
    {
        public double Value { get; set; }
    }

    // POST /api/sensors/{id}/measurements
    [HttpPost("{id}/measurements")]
    public async Task<IActionResult> UpdateSensor(string id, [FromBody] MeasurementRequest req)
    {
        if (!double.IsFinite(req.Value))
            return BadRequest("Value must be a finite number.");

        var exists = await _db.Sensors.AnyAsync(s => s.Id == id);
        if (!exists) return NotFound("Sensor not found");

        await _broadcaster.ProcessReadingAsync(id, req.Value);

        return Ok(new { ok = true, sensorId = id, value = req.Value });
    }

    // GET /api/sensors/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetSensor(string id)
    {
        var sensor = await _db.Sensors.FirstOrDefaultAsync(s => s.Id == id);
        return sensor == null ? NotFound("Sensor not found") : Ok(sensor);
    }

    // GET /api/sensors  (all sensors, optionally filtered by nodeId)
    [HttpGet]
    public async Task<IActionResult> GetSensors([FromQuery] string? nodeId)
    {
        var query = _db.Sensors.AsQueryable();
        if (!string.IsNullOrEmpty(nodeId))
            query = query.Where(s => s.NodeId == nodeId);

        return Ok(await query.ToListAsync());
    }
}
