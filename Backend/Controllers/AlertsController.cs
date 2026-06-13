using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AlertsController : ControllerBase
{
    private readonly TunnelDbContext _db;

    public AlertsController(TunnelDbContext db)
    {
        _db = db;
    }

    // GET /api/alerts?status=Active&severity=Critical&from=&to=&page=1&pageSize=50
    [HttpGet]
    public async Task<IActionResult> GetAlerts(
        [FromQuery] AlertStatus? status,
        [FromQuery] AlertSeverity? severity,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? nodeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        pageSize = Math.Min(pageSize, 200);
        var query = _db.Alerts.AsQueryable();

        if (status.HasValue) query = query.Where(a => a.Status == status.Value);
        if (severity.HasValue) query = query.Where(a => a.Severity == severity.Value);
        if (from.HasValue) query = query.Where(a => a.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(a => a.CreatedAt <= to.Value);
        if (!string.IsNullOrEmpty(nodeId)) query = query.Where(a => a.NodeId == nodeId);

        var total = await query.CountAsync();
        var alerts = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { total, page, pageSize, alerts });
    }

    // POST /api/alerts  (tạo alert từ frontend hoặc ingestion service)
    [HttpPost]
    public async Task<IActionResult> CreateAlert([FromBody] Alert alert)
    {
        alert.CreatedAt = DateTime.UtcNow;
        alert.Status = AlertStatus.Active;
        _db.Alerts.Add(alert);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAlert), new { id = alert.Id }, alert);
    }

    // GET /api/alerts/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetAlert(long id)
    {
        var alert = await _db.Alerts.FindAsync(id);
        return alert == null ? NotFound() : Ok(alert);
    }

    // POST /api/alerts/{id}/acknowledge
    [HttpPost("{id}/acknowledge")]
    public async Task<IActionResult> Acknowledge(long id, [FromBody] AcknowledgeRequest req)
    {
        var alert = await _db.Alerts.FindAsync(id);
        if (alert == null) return NotFound();
        if (alert.Status != AlertStatus.Active)
            return BadRequest("Alert is not active");

        alert.Status = AlertStatus.Acknowledged;
        alert.AcknowledgedAt = DateTime.UtcNow;
        alert.AcknowledgedBy = req.AcknowledgedBy;
        await _db.SaveChangesAsync();
        return Ok(alert);
    }

    // POST /api/alerts/{id}/resolve
    [HttpPost("{id}/resolve")]
    public async Task<IActionResult> Resolve(long id)
    {
        var alert = await _db.Alerts.FindAsync(id);
        if (alert == null) return NotFound();

        alert.Status = AlertStatus.Resolved;
        alert.ResolvedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(alert);
    }

    public class AcknowledgeRequest
    {
        public string? AcknowledgedBy { get; set; }
    }
}
