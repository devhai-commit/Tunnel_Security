using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models;
using Backend.Services;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CamerasController : ControllerBase
{
    private readonly TunnelDbContext _db;
    private readonly VideoClipService _clipService;
    private readonly IWebHostEnvironment _env;

    public CamerasController(TunnelDbContext db, VideoClipService clipService, IWebHostEnvironment env)
    {
        _db = db;
        _clipService = clipService;
        _env = env;
    }

    // GET /api/cameras
    [HttpGet]
    public async Task<IActionResult> GetCameras()
    {
        var cameras = await _db.Cameras.ToListAsync();
        return Ok(cameras);
    }

    // GET /api/cameras/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetCamera(string id)
    {
        var camera = await _db.Cameras.FindAsync(id);
        return camera == null ? NotFound() : Ok(camera);
    }

    // GET /api/cameras/{id}/snapshots?from=&to=&page=1&pageSize=20
    [HttpGet("{id}/snapshots")]
    public async Task<IActionResult> GetSnapshots(
        string id,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? detectionType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Min(pageSize, 100);
        var query = _db.CameraSnapshots.Where(s => s.CameraId == id);
        if (from.HasValue) query = query.Where(s => s.Timestamp >= from.Value);
        if (to.HasValue) query = query.Where(s => s.Timestamp <= to.Value);
        if (!string.IsNullOrEmpty(detectionType))
            query = query.Where(s => s.DetectionType == detectionType);

        var total = await query.CountAsync();
        var snapshots = await query
            .OrderByDescending(s => s.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { total, page, pageSize, snapshots });
    }

    // GET /api/cameras/{id}/snapshots/{snapshotId}/image
    [HttpGet("{id}/snapshots/{snapshotId}/image")]
    public async Task<IActionResult> GetSnapshotImage(string id, long snapshotId)
    {
        var snapshot = await _db.CameraSnapshots
            .FirstOrDefaultAsync(s => s.Id == snapshotId && s.CameraId == id);
        if (snapshot == null) return NotFound();

        var fullPath = Path.Combine(_env.ContentRootPath, "Storage", snapshot.FilePath);
        if (!System.IO.File.Exists(fullPath)) return NotFound("Image file not found");

        return PhysicalFile(fullPath, "image/jpeg");
    }

    // GET /api/cameras/{id}/snapshots/{snapshotId}/thumbnail
    [HttpGet("{id}/snapshots/{snapshotId}/thumbnail")]
    public async Task<IActionResult> GetSnapshotThumbnail(string id, long snapshotId)
    {
        var snapshot = await _db.CameraSnapshots
            .FirstOrDefaultAsync(s => s.Id == snapshotId && s.CameraId == id);
        if (snapshot == null) return NotFound();
        if (string.IsNullOrEmpty(snapshot.ThumbnailPath)) return NotFound("Thumbnail not found");

        var fullPath = Path.Combine(_env.ContentRootPath, "Storage", snapshot.ThumbnailPath);
        if (!System.IO.File.Exists(fullPath)) return NotFound("Thumbnail file not found");

        return PhysicalFile(fullPath, "image/jpeg");
    }

    // GET /api/cameras/{id}/clips?page=1&pageSize=10
    [HttpGet("{id}/clips")]
    public async Task<IActionResult> GetClips(
        string id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        pageSize = Math.Min(pageSize, 50);
        var total = await _db.VideoClips.CountAsync(c => c.CameraId == id);
        var clips = await _db.VideoClips
            .Where(c => c.CameraId == id)
            .OrderByDescending(c => c.StartTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { total, page, pageSize, clips });
    }

    // POST /api/cameras/{id}/snapshot — trigger manual snapshot
    [HttpPost("{id}/snapshot")]
    public async Task<IActionResult> TriggerSnapshot(string id)
    {
        var camera = await _db.Cameras.FindAsync(id);
        if (camera == null) return NotFound();

        // Placeholder — VideoCaptureService sẽ capture ở tick tiếp theo
        // Với RTSP thực: trigger capture ngay tại đây
        return Ok(new { message = "Snapshot will be captured in next cycle", cameraId = id });
    }

    // POST /api/cameras/{id}/clip — trigger video clip recording
    [HttpPost("{id}/clip")]
    public async Task<IActionResult> TriggerClip(string id, [FromBody] TriggerClipRequest req)
    {
        var camera = await _db.Cameras.FindAsync(id);
        if (camera == null) return NotFound();

        await _clipService.TriggerClipAsync(id, req.Reason ?? "manual");
        return Ok(new { message = "Clip recording triggered", cameraId = id });
    }

    // PUT /api/cameras/{id}/status
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] UpdateStatusRequest req)
    {
        var camera = await _db.Cameras.FindAsync(id);
        if (camera == null) return NotFound();

        camera.Status = req.Status;
        await _db.SaveChangesAsync();
        return Ok(camera);
    }

    public class TriggerClipRequest
    {
        public string? Reason { get; set; }
    }

    public class UpdateStatusRequest
    {
        public CameraStatus Status { get; set; }
    }
}
