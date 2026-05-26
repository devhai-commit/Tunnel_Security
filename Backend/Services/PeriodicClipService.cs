using System.Diagnostics;
using Backend.Data;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Tự động lưu đoạn video 10 giây mỗi 5 phút cho mỗi camera đang phát stream.
/// Chỉ hoạt động khi FFmpeg có sẵn; ghi log cảnh báo và thoát nếu không có.
/// Frames được lấy từ CameraFrameBuffer (push mode) với tốc độ 10 FPS.
/// </summary>
public sealed class PeriodicClipService : BackgroundService
{
    private readonly TimeSpan _recordInterval;
    private readonly int _clipDurationSeconds;
    private const int CaptureFps = 10;

    private readonly CameraFrameBuffer _frameBuffer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PeriodicClipService> _logger;
    private readonly string _storageRoot;

    public PeriodicClipService(
        CameraFrameBuffer frameBuffer,
        IServiceScopeFactory scopeFactory,
        IWebHostEnvironment env,
        IConfiguration configuration,
        ILogger<PeriodicClipService> logger)
    {
        _frameBuffer  = frameBuffer;
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _storageRoot  = Path.Combine(env.ContentRootPath, "Storage");

        _recordInterval     = TimeSpan.FromMinutes(configuration.GetValue("Camera:PeriodicClip:IntervalMinutes", 5));
        _clipDurationSeconds = configuration.GetValue("Camera:PeriodicClip:DurationSeconds", 10);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!VideoFrameReader.IsAvailable())
        {
            _logger.LogWarning("[PeriodicClip] FFmpeg not found — periodic clip recording disabled. " +
                               "Install FFmpeg and add to PATH or set FFMPEG_PATH.");
            return;
        }

        _logger.LogInformation(
            "[PeriodicClip] Started — interval {Minutes}m, clip duration {Seconds}s at {Fps} FPS",
            (int)_recordInterval.TotalMinutes, _clipDurationSeconds, CaptureFps);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_recordInterval, stoppingToken);

            try { await RecordAllActiveCamerasAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PeriodicClip] Unexpected error during recording cycle");
            }
        }
    }

    // ── Cycle ─────────────────────────────────────────────────────────────────

    private async Task RecordAllActiveCamerasAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TunnelDbContext>();
        var allIds = await db.Cameras.Select(c => c.Id).ToListAsync(ct);

        // Only record cameras that currently have live frames in the buffer
        var activeIds = allIds.Where(id => _frameBuffer.TryGetLatest(id, out _)).ToList();
        if (activeIds.Count == 0)
        {
            _logger.LogDebug("[PeriodicClip] No active cameras — skipping cycle");
            return;
        }

        _logger.LogInformation("[PeriodicClip] Recording {Count} camera(s): {Ids}",
            activeIds.Count, string.Join(", ", activeIds));

        // Collect frames for all cameras concurrently over the clip window, then encode
        await Task.WhenAll(activeIds.Select(id => RecordOneCameraAsync(id, ct)));
    }

    // ── Per-camera pipeline ───────────────────────────────────────────────────

    private async Task RecordOneCameraAsync(string cameraId, CancellationToken ct)
    {
        var startTime = DateTime.UtcNow;
        var frames = await CollectFramesAsync(cameraId, ct);
        var endTime = DateTime.UtcNow;

        int minFrames = CaptureFps; // at least 1 second of content
        if (frames.Count < minFrames)
        {
            _logger.LogWarning("[PeriodicClip] {CameraId}: only {N}/{Total} frames — skipping",
                cameraId, frames.Count, _clipDurationSeconds * CaptureFps);
            return;
        }

        var clipPath = await EncodeClipAsync(cameraId, startTime, frames, ct);
        if (clipPath == null) return;

        await PersistAsync(cameraId, startTime, endTime, clipPath, ct);
        _logger.LogInformation("[PeriodicClip] {CameraId}: clip saved → {Path}", cameraId, clipPath);
    }

    // ── Frame collection (10s window) ─────────────────────────────────────────

    private async Task<List<byte[]>> CollectFramesAsync(string cameraId, CancellationToken ct)
    {
        int totalTicks = _clipDurationSeconds * CaptureFps;
        int delayMs    = 1000 / CaptureFps;
        var frames = new List<byte[]>(totalTicks);

        for (int i = 0; i < totalTicks && !ct.IsCancellationRequested; i++)
        {
            if (_frameBuffer.TryGetLatest(cameraId, out var frame) && frame != null)
                frames.Add(frame);
            await Task.Delay(delayMs, ct);
        }

        return frames;
    }

    // ── FFmpeg encoding ───────────────────────────────────────────────────────

    private async Task<string?> EncodeClipAsync(
        string cameraId, DateTime startTime, List<byte[]> frames, CancellationToken ct)
    {
        var dateDir = startTime.ToString("yyyy-MM-dd");
        var clipDir = Path.Combine(_storageRoot, "Clips", cameraId, dateDir);
        Directory.CreateDirectory(clipDir);

        var tempDir = Path.Combine(clipDir, $"tmp_{startTime:HHmmss_fff}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write numbered JPEG frames for ffmpeg image2 demuxer
            for (int i = 0; i < frames.Count; i++)
                await File.WriteAllBytesAsync(Path.Combine(tempDir, $"{i:D5}.jpg"), frames[i], ct);

            var outputPath = Path.Combine(clipDir, $"periodic_{startTime:HHmmss}.mp4");
            var ffmpeg = VideoFrameReader.GetFfmpegPath();
            // -y          : overwrite output
            // -framerate  : input frame rate matching our capture rate
            // -i          : numbered JPEG sequence
            // -c:v libx264: H.264 encode
            // -pix_fmt    : required by libx264 for compatibility
            // -crf 23     : quality (lower = better; 23 is default)
            // -movflags   : fast-start for progressive web playback
            var args = $"-y -framerate {CaptureFps} " +
                       $"-i \"{tempDir}/%05d.jpg\" " +
                       $"-c:v libx264 -pix_fmt yuv420p -crf 23 -movflags +faststart " +
                       $"\"{outputPath}\" -loglevel warning -nostdin";

            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo(ffmpeg, args)
                {
                    UseShellExecute        = false,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                }
            };

            proc.Start();
            var stderr = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode != 0)
            {
                _logger.LogWarning("[PeriodicClip] FFmpeg failed for {CameraId} (exit {Code}): {Err}",
                    cameraId, proc.ExitCode, stderr.Trim());
                return null;
            }

            return outputPath;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[PeriodicClip] Encode error for {CameraId}", cameraId);
            return null;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    // ── DB persistence ────────────────────────────────────────────────────────

    private async Task PersistAsync(
        string cameraId, DateTime startTime, DateTime endTime,
        string clipPath, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TunnelDbContext>();

        var info = new FileInfo(clipPath);
        db.VideoClips.Add(new VideoClip
        {
            CameraId      = cameraId,
            StartTime     = startTime,
            EndTime       = endTime,
            FilePath      = Path.GetRelativePath(_storageRoot, clipPath).Replace('\\', '/'),
            SizeBytes     = info.Exists ? info.Length : 0L,
            TriggerReason = "periodic_5min",
            CreatedAt     = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
