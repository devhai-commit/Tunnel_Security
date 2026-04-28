using Backend.Data;
using Backend.Models;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Backend.Services;

/// <summary>
/// Background service kết nối camera RTSP và capture snapshot định kỳ.
/// Khi không có RTSP client thực (Emgu.CV/FFmpeg), service sẽ hoạt động ở chế độ
/// placeholder — sẵn sàng cho integration thực sau.
/// </summary>
public class VideoCaptureService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VideoCaptureService> _logger;
    private readonly string _storageRoot;
    private readonly int _captureIntervalSeconds;

    public VideoCaptureService(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<VideoCaptureService> logger,
        IWebHostEnvironment env)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _storageRoot = Path.Combine(env.ContentRootPath, "Storage");
        _captureIntervalSeconds = configuration.GetValue("Camera:CaptureIntervalSeconds", 5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureStorageDirectories();
        _logger.LogInformation("[VideoCapture] Started — capture interval: {Interval}s", _captureIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await CaptureAllCamerasAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(_captureIntervalSeconds), stoppingToken);
        }
    }

    private async Task CaptureAllCamerasAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TunnelDbContext>();

        var cameras = await db.Cameras
            .Where(c => c.Status == CameraStatus.Online)
            .ToListAsync(ct);

        if (cameras.Count == 0) return;

        var tasks = cameras.Select(cam => CaptureFrameAsync(cam, db, ct));
        await Task.WhenAll(tasks);
        await db.SaveChangesAsync(ct);
    }

    private async Task CaptureFrameAsync(CameraDevice camera, TunnelDbContext db, CancellationToken ct)
    {
        try
        {
            var frame = await FetchFrameAsync(camera, ct);
            if (frame == null || frame.Length == 0) return;

            var timestamp = DateTime.UtcNow;
            var dateFolder = timestamp.ToString("yyyy-MM-dd");
            var snapshotDir = Path.Combine(_storageRoot, "Snapshots", camera.Id, dateFolder);
            Directory.CreateDirectory(snapshotDir);

            var fileName = $"{timestamp:HHmmss_fff}.jpg";
            var filePath = Path.Combine(snapshotDir, fileName);
            var thumbPath = Path.Combine(snapshotDir, $"thumb_{fileName}");

            await File.WriteAllBytesAsync(filePath, frame, ct);
            await SaveThumbnailAsync(frame, thumbPath, ct);

            camera.LastFrameTime = timestamp;

            db.CameraSnapshots.Add(new CameraSnapshot
            {
                CameraId = camera.Id,
                Timestamp = timestamp,
                FilePath = GetRelativePath(filePath),
                ThumbnailPath = GetRelativePath(thumbPath)
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("[VideoCapture] Camera {CameraId} capture failed: {Message}",
                camera.Id, ex.Message);
        }
    }

    /// <summary>
    /// Fetch JPEG frame từ camera.
    /// HTTP snapshot URL: camera.StreamUrl thay ".sdp"/":8554" bằng "/snapshot.jpg"
    /// RTSP: cần FFmpeg/Emgu.CV — hiện tại trả null nếu chưa cài.
    /// </summary>
    private async Task<byte[]?> FetchFrameAsync(CameraDevice camera, CancellationToken ct)
    {
        if (camera.Protocol == CameraProtocol.HTTP && !string.IsNullOrEmpty(camera.StreamUrl))
        {
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                return await httpClient.GetByteArrayAsync(camera.StreamUrl, ct);
            }
            catch
            {
                return null;
            }
        }

        // RTSP: placeholder — integration với FFmpeg.AutoGen hoặc Emgu.CV ở đây
        // Khi thêm thư viện native, replace block này:
        // using var capture = new VideoCapture(camera.StreamUrl);
        // using var frame = capture.QueryFrame();
        // return frame?.ToImage<Bgr, byte>().ToJpegBytes();
        return null;
    }

    private static async Task SaveThumbnailAsync(byte[] jpegBytes, string thumbPath, CancellationToken ct)
    {
        using var image = Image.Load(jpegBytes);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(320, 180),
            Mode = ResizeMode.Max
        }));
        await image.SaveAsJpegAsync(thumbPath, ct);
    }

    private void EnsureStorageDirectories()
    {
        Directory.CreateDirectory(Path.Combine(_storageRoot, "Snapshots"));
        Directory.CreateDirectory(Path.Combine(_storageRoot, "Clips"));
    }

    private string GetRelativePath(string fullPath)
        => Path.GetRelativePath(_storageRoot, fullPath).Replace('\\', '/');
}
