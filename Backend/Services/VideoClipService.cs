using System.Collections.Concurrent;
using Backend.Data;
using Backend.Models;

namespace Backend.Services;

/// <summary>
/// Quản lý ring buffer snapshot và tạo video clip khi alert kích hoạt.
/// Ring buffer giữ lại các frame gần nhất để kết hợp với frame sau khi alert.
/// Khi có FFmpeg thực, replace SaveClipAsync với FFmpeg process.
/// </summary>
public class VideoClipService
{
    // Ring buffer: cameraId → queue of (timestamp, filePath)
    private readonly ConcurrentDictionary<string, Queue<(DateTime ts, string path)>> _ringBuffers = new();
    private const int RingBufferSize = 10; // giữ 10 snapshots trước alert

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VideoClipService> _logger;
    private readonly string _storageRoot;

    public VideoClipService(
        IServiceScopeFactory scopeFactory,
        ILogger<VideoClipService> logger,
        IWebHostEnvironment env)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _storageRoot = Path.Combine(env.ContentRootPath, "Storage");
    }

    /// <summary>Gọi từ VideoCaptureService sau mỗi frame capture thành công.</summary>
    public void AddToRingBuffer(string cameraId, DateTime timestamp, string filePath)
    {
        var buffer = _ringBuffers.GetOrAdd(cameraId, _ => new Queue<(DateTime, string)>());
        lock (buffer)
        {
            buffer.Enqueue((timestamp, filePath));
            while (buffer.Count > RingBufferSize) buffer.Dequeue();
        }
    }

    /// <summary>
    /// Kích hoạt ghi clip khi có alert.
    /// Ghi clip = ring buffer snapshots (10s trước) + capture thêm 20s.
    /// </summary>
    public async Task TriggerClipAsync(string cameraId, string triggerReason, CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow.AddSeconds(-10);
        var endTime = DateTime.UtcNow.AddSeconds(20);

        var clipDir = Path.Combine(_storageRoot, "Clips", cameraId, DateTime.UtcNow.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(clipDir);
        var clipPath = Path.Combine(clipDir, $"clip_{DateTime.UtcNow:HHmmss}.mp4");

        // Lấy frames từ ring buffer
        var frames = new List<string>();
        if (_ringBuffers.TryGetValue(cameraId, out var buffer))
        {
            lock (buffer)
            {
                frames.AddRange(buffer.Select(x => x.path));
            }
        }

        var fileSize = await SaveClipAsync(clipPath, frames, ct);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TunnelDbContext>();

        db.VideoClips.Add(new VideoClip
        {
            CameraId = cameraId,
            StartTime = startTime,
            EndTime = endTime,
            FilePath = Path.GetRelativePath(_storageRoot, clipPath).Replace('\\', '/'),
            SizeBytes = fileSize,
            TriggerReason = triggerReason,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("[VideoClip] Clip created for camera {CameraId}: {Path}", cameraId, clipPath);
    }

    /// <summary>
    /// Tạo MP4 từ danh sách JPEG frames.
    /// Hiện tại: tạo file placeholder. Khi có FFmpeg, dùng:
    ///   ffmpeg -framerate 1 -i /path/frame%03d.jpg -c:v libx264 output.mp4
    /// </summary>
    private static async Task<long> SaveClipAsync(string clipPath, List<string> framePaths, CancellationToken ct)
    {
        // Placeholder: ghi danh sách frame paths vào file text
        // Replace với FFmpeg process call khi có native dependency:
        // var args = $"-framerate 1 -pattern_type glob -i '{frameDir}/*.jpg' -c:v libx264 '{clipPath}'";
        // await RunFfmpegAsync(args, ct);
        var content = string.Join("\n", framePaths);
        await File.WriteAllTextAsync(clipPath.Replace(".mp4", ".txt"), content, ct);
        return content.Length;
    }
}
