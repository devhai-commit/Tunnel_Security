using System.Collections.Concurrent;
using System.Text;
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
    private readonly VideoSourceRegistry _videoSources;
    private readonly CameraFrameBuffer _frameBuffer;

    public CamerasController(
        TunnelDbContext db,
        VideoClipService clipService,
        IWebHostEnvironment env,
        VideoSourceRegistry videoSources,
        CameraFrameBuffer frameBuffer)
    {
        _db = db;
        _clipService = clipService;
        _env = env;
        _videoSources = videoSources;
        _frameBuffer = frameBuffer;
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

    // ── Simulated stream endpoints ─────────────────────────────────────────────

    private static readonly ConcurrentDictionary<string, long> _frameCounters = new();

    // GET /api/cameras/{id}/frame — single JPEG (synthetic, for polling)
    [HttpGet("{id}/frame")]
    public IActionResult GetFrame(string id)
    {
        var frameIndex = _frameCounters.AddOrUpdate(id, 0, (_, v) => v + 1);
        var jpegBytes = CameraFrameGenerator.GenerateFrame(id, frameIndex);
        Response.Headers["Cache-Control"] = "no-cache, no-store";
        return File(jpegBytes, "image/jpeg");
    }

    // GET /api/cameras/{id}/stream — MJPEG stream
    // Source is evaluated dynamically on every frame tick so the stream seamlessly
    // switches between sources without requiring a reconnect:
    //   • Pushed frames (simulator)  — highest priority, active while fresh (< 2 s ago)
    //   • Video file via FFMpeg      — when a video-source is registered
    //   • Synthetic colored noise    — permanent fallback
    [HttpGet("{id}/stream")]
    public async Task StreamMjpeg(string id, CancellationToken cancellationToken)
    {
        Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";

        long syntheticIndex = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Priority 1 — fresh pushed frame from simulator
                if (_frameBuffer.TryGetLatest(id, out var pushed))
                {
                    await WriteFrameAsync(pushed!, cancellationToken);
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                // Priority 2 — video file (FFMpeg); read one frame at a time
                var videoPath = _videoSources.Get(id);
                if (videoPath != null)
                {
                    // Delegate to FFMpeg reader for the duration of this video source
                    await foreach (var jpeg in VideoFrameReader.ReadFramesAsync(videoPath, cancellationToken))
                    {
                        await WriteFrameAsync(jpeg, cancellationToken);
                        // Re-check pushed frames between every FFMpeg frame
                        if (_frameBuffer.TryGetLatest(id, out _)) break;
                    }
                    continue;
                }

                // Priority 3 — synthetic fallback
                var synth = CameraFrameGenerator.GenerateFrame(id, syntheticIndex++);
                await WriteFrameAsync(synth, cancellationToken);
                await Task.Delay(100, cancellationToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    // POST /api/cameras/{id}/push-frame
    // Body: raw JPEG bytes (Content-Type: image/jpeg)
    // Called by simulators to push a single frame into the live stream buffer.
    [HttpPost("{id}/push-frame")]
    public async Task<IActionResult> PushFrame(string id, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, cancellationToken);
        var jpeg = ms.ToArray();
        if (jpeg.Length == 0) return BadRequest(new { error = "Empty frame body" });

        _frameBuffer.Push(id, jpeg);
        return Ok(new { cameraId = id, bytes = jpeg.Length });
    }

    // DELETE /api/cameras/{id}/push-frame — clear pushed-frame buffer, revert to next priority source
    [HttpDelete("{id}/push-frame")]
    public IActionResult ClearPushedFrames(string id)
    {
        _frameBuffer.Clear(id);
        return Ok(new { cameraId = id });
    }

    // PUT /api/cameras/{id}/video-source  body: { "filePath": "C:\\path\\to\\video.mp4" }
    [HttpPut("{id}/video-source")]
    public IActionResult SetVideoSource(string id, [FromBody] VideoSourceRequest req)
    {
        if (!System.IO.File.Exists(req.FilePath))
            return BadRequest(new { error = $"File not found: {req.FilePath}" });

        if (!VideoFrameReader.IsAvailable())
            return StatusCode(503, new { error = "FFMpeg not found. Install FFMpeg and ensure it is in PATH or set FFMPEG_PATH." });

        _videoSources.Set(id, req.FilePath);
        return Ok(new { cameraId = id, filePath = req.FilePath });
    }

    // DELETE /api/cameras/{id}/video-source — reverts to synthetic frames
    [HttpDelete("{id}/video-source")]
    public IActionResult ClearVideoSource(string id)
    {
        _videoSources.Clear(id);
        return Ok(new { cameraId = id });
    }

    private async Task WriteFrameAsync(byte[] jpeg, CancellationToken ct)
    {
        var header = Encoding.ASCII.GetBytes(
            $"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {jpeg.Length}\r\n\r\n");
        await Response.Body.WriteAsync(header, ct);
        await Response.Body.WriteAsync(jpeg, ct);
        await Response.Body.WriteAsync("\r\n"u8.ToArray(), ct);
        await Response.Body.FlushAsync(ct);
    }

    public class TriggerClipRequest  { public string? Reason { get; set; } }
    public class UpdateStatusRequest { public CameraStatus Status { get; set; } }
    public record VideoSourceRequest(string FilePath);
}
