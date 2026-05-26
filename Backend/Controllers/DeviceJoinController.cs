using Backend.Data;
using Backend.Hubs;
using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

[ApiController]
[Route("api/device-joins")]
public class DeviceJoinController : ControllerBase
{
    private readonly TunnelDbContext       _db;
    private readonly DeviceJoinRegistry    _registry;
    private readonly IHubContext<SensorHub> _hub;

    public DeviceJoinController(
        TunnelDbContext        db,
        DeviceJoinRegistry     registry,
        IHubContext<SensorHub> hub)
    {
        _db       = db;
        _registry = registry;
        _hub      = hub;
    }

    /// <summary>Lấy danh sách yêu cầu gia nhập (mặc định lấy trạng thái Pending)</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] JoinRequestStatus? status = null)
    {
        var query = _db.DevicePendingJoins.AsQueryable();
        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        var items = await query
            .OrderByDescending(x => x.RequestedAt)
            .Take(100)
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await _db.DevicePendingJoins.FindAsync(id);
        return item == null ? NotFound() : Ok(item);
    }

    /// <summary>Phê duyệt yêu cầu gia nhập, gán NodeByteId cho thiết bị</summary>
    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, [FromBody] ApproveJoinRequest req)
    {
        var item = await _db.DevicePendingJoins.FindAsync(id);
        if (item == null) return NotFound();
        if (item.Status != JoinRequestStatus.Pending)
            return BadRequest(new { error = "Yêu cầu này không còn ở trạng thái chờ." });

        item.Status             = JoinRequestStatus.Accepted;
        item.AssignedNodeByteId = req.NodeByteId;
        item.RespondedAt        = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        // Giải phóng WebSocket handler đang chờ
        _registry.TryDecide(id, new JoinDecision(true, req.NodeByteId));

        // Thông báo kết quả cho tất cả Station client qua SignalR
        await _hub.Clients.All.SendAsync("JoinRequestDecided", new
        {
            item.Id,
            Status      = "Accepted",
            item.MacAddress,
            req.NodeByteId
        });

        return Ok(item);
    }

    /// <summary>Từ chối yêu cầu gia nhập</summary>
    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectJoinRequest? req = null)
    {
        var item = await _db.DevicePendingJoins.FindAsync(id);
        if (item == null) return NotFound();
        if (item.Status != JoinRequestStatus.Pending)
            return BadRequest(new { error = "Yêu cầu này không còn ở trạng thái chờ." });

        item.Status          = JoinRequestStatus.Rejected;
        item.RejectionReason = req?.Reason;
        item.RespondedAt     = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        _registry.TryDecide(id, new JoinDecision(false, 0));

        await _hub.Clients.All.SendAsync("JoinRequestDecided", new
        {
            item.Id,
            Status      = "Rejected",
            item.MacAddress,
            NodeByteId  = (byte)0
        });

        return Ok(item);
    }
}

public sealed record ApproveJoinRequest(byte NodeByteId);
public sealed record RejectJoinRequest(string? Reason = null);
