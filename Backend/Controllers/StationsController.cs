using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StationsController : ControllerBase
{
    private readonly TunnelDbContext _db;

    public StationsController(TunnelDbContext db)
    {
        _db = db;
    }

    // GET /api/stations
    [HttpGet]
    public async Task<IActionResult> GetStations()
    {
        var stations = await _db.Stations
            .Include(s => s.Lines)
                .ThenInclude(l => l.Nodes)
                    .ThenInclude(n => n.Sensors)
            .ToListAsync();
        return Ok(stations);
    }

    // GET /api/stations/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetStation(string id)
    {
        var station = await _db.Stations
            .Include(s => s.Lines)
                .ThenInclude(l => l.Nodes)
                    .ThenInclude(n => n.Sensors)
            .FirstOrDefaultAsync(s => s.Id == id);

        return station == null ? NotFound() : Ok(station);
    }

    // GET /api/stations/{id}/lines
    [HttpGet("{id}/lines")]
    public async Task<IActionResult> GetLines(string id)
    {
        var exists = await _db.Stations.AnyAsync(s => s.Id == id);
        if (!exists) return NotFound();

        var lines = await _db.Lines
            .Include(l => l.Nodes)
                .ThenInclude(n => n.Sensors)
            .Where(l => l.StationId == id)
            .ToListAsync();

        return Ok(lines);
    }

    // GET /api/stations/{id}/nodes (GeoJSON)
    [HttpGet("{id}/nodes")]
    public async Task<IActionResult> GetNodes(string id)
    {
        var station = await _db.Stations.FirstOrDefaultAsync(s => s.Id == id);
        if (station == null) return NotFound();

        var lineIds = await _db.Lines
            .Where(l => l.StationId == id)
            .Select(l => l.Id)
            .ToListAsync();

        var nodes = await _db.Nodes
            .Include(n => n.Sensors)
            .Where(n => lineIds.Contains(n.LineId))
            .ToListAsync();

        var lineNames = await _db.Lines
            .Where(l => l.StationId == id)
            .ToDictionaryAsync(l => l.Id, l => l.Name);

        var geo = new
        {
            type = "FeatureCollection",
            features = nodes.Select(node => new
            {
                type = "Feature",
                geometry = new
                {
                    type = "Point",
                    coordinates = new[] { node.Lng, node.Lat }
                },
                properties = new
                {
                    node.Id,
                    node.Code,
                    node.Name,
                    node.LineId,
                    Line = lineNames.GetValueOrDefault(node.LineId),
                    node.Status,
                    node.IsHub,
                    node.BatteryLevel,
                    node.RSSI,
                    node.LastOnline,
                    SensorCount = node.Sensors.Count,
                    node.CameraId
                }
            })
        };

        return Ok(geo);
    }

    // GET /api/stations/{id}/lines-geojson (GeoJSON)
    [HttpGet("{id}/lines-geojson")]
    public async Task<IActionResult> GetLinesGeoJson(string id)
    {
        var exists = await _db.Stations.AnyAsync(s => s.Id == id);
        if (!exists) return NotFound();

        var lines = await _db.Lines
            .Include(l => l.Nodes)
            .Where(l => l.StationId == id)
            .ToListAsync();

        var geo = new
        {
            type = "FeatureCollection",
            features = lines.Select(line => new
            {
                type = "Feature",
                geometry = new
                {
                    type = "LineString",
                    coordinates = new[] {
                        new[] { line.StartLng, line.StartLat },
                        new[] { line.EndLng, line.EndLat }
                    }
                },
                properties = new
                {
                    line.Id,
                    line.Code,
                    line.Name,
                    line.Description,
                    line.Status,
                    line.Length,
                    NodeCount = line.Nodes.Count
                }
            })
        };

        return Ok(geo);
    }

    // GET /api/stations/{stationId}/lines/{lineId}/nodes
    [HttpGet("{stationId}/lines/{lineId}/nodes")]
    public async Task<IActionResult> GetLineNodes(string stationId, string lineId)
    {
        var line = await _db.Lines
            .Include(l => l.Nodes)
                .ThenInclude(n => n.Sensors)
            .FirstOrDefaultAsync(l => l.Id == lineId && l.StationId == stationId);

        if (line == null) return NotFound("Line not found");

        return Ok(line.Nodes);
    }

    // GET /api/stations/{stationId}/nodes/{nodeId}
    [HttpGet("{stationId}/nodes/{nodeId}")]
    public async Task<IActionResult> GetNodeDetail(string stationId, string nodeId)
    {
        var lineIds = await _db.Lines
            .Where(l => l.StationId == stationId)
            .Select(l => l.Id)
            .ToListAsync();

        var node = await _db.Nodes
            .Include(n => n.Sensors)
            .FirstOrDefaultAsync(n => n.Id == nodeId && lineIds.Contains(n.LineId));

        return node == null ? NotFound("Node not found") : Ok(node);
    }

    // GET /api/stations/{stationId}/nodes/{nodeId}/sensors
    [HttpGet("{stationId}/nodes/{nodeId}/sensors")]
    public async Task<IActionResult> GetNodeSensors(string stationId, string nodeId)
    {
        var lineIds = await _db.Lines
            .Where(l => l.StationId == stationId)
            .Select(l => l.Id)
            .ToListAsync();

        var nodeExists = await _db.Nodes
            .AnyAsync(n => n.Id == nodeId && lineIds.Contains(n.LineId));

        if (!nodeExists) return NotFound("Node not found");

        var sensors = await _db.Sensors
            .Where(s => s.NodeId == nodeId)
            .ToListAsync();

        return Ok(sensors);
    }
}
