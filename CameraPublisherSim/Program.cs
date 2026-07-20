using CameraPublisherSim;

var wsBase = Environment.GetEnvironmentVariable("CAMERA_BACKEND_WS") ?? "ws://localhost:5080/ws/camera";
var defaultFps = int.TryParse(Environment.GetEnvironmentVariable("CAMERA_FPS"), out var parsedFps) ? parsedFps : 24;
var configPath = Environment.GetEnvironmentVariable("CAMERA_CONFIG_PATH") ?? "cameras.txt";

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine("[CameraPublisherSim] Camera frame WebSocket publisher");

if (File.Exists(configPath))
{
    var entries = CameraSimConfig.Load(configPath);

    if (entries.Count > 0)
    {
        Console.WriteLine($"[CameraPublisherSim] Loaded {entries.Count} camera(s) from {configPath}");

        if (!VideoFrameSource.IsAvailable())
        {
            Console.WriteLine("[CameraPublisherSim] WARNING: ffmpeg not found (set FFMPEG_PATH or add ffmpeg to PATH) — decoding will fail");
        }

        var publishers = new List<CameraPublisher>();
        foreach (var entry in entries)
        {
            if (!CameraSimConfig.LooksLikeNetworkLink(entry.VideoPath) && !File.Exists(entry.VideoPath))
            {
                Console.WriteLine($"[CameraPublisherSim] WARNING: video not found for {entry.CameraId}: {entry.VideoPath} — skipping");
                continue;
            }

            var ingestUri = new Uri($"{wsBase.TrimEnd('/')}/{entry.CameraId}/ingest");
            var fps = entry.Fps ?? defaultFps;
            Console.WriteLine($"[CameraPublisherSim] {entry.CameraId} -> {ingestUri} (video: {entry.VideoPath}, fps: {fps})");
            publishers.Add(new CameraPublisher(ingestUri, entry.CameraId, fps, staticFrame: null, videoPath: entry.VideoPath));
        }

        if (publishers.Count == 0)
        {
            Console.WriteLine("[CameraPublisherSim] ERROR: no valid camera entries in config — nothing to publish");
            return;
        }

        Console.WriteLine("[CameraPublisherSim] Press Ctrl+C to stop");
        await Task.WhenAll(publishers.Select(p => p.RunAsync(cts.Token)));
        return;
    }
}

// Fallback: single camera driven by env vars (CAMERA_ID / CAMERA_VIDEO_PATH / CAMERA_IMAGE_PATH),
// unchanged from before cameras.txt existed — kept so existing single-camera usage still works.
var cameraId = Environment.GetEnvironmentVariable("CAMERA_ID") ?? "CAM-HUB-01";
var imagePath = Environment.GetEnvironmentVariable("CAMERA_IMAGE_PATH");
var videoPath = Environment.GetEnvironmentVariable("CAMERA_VIDEO_PATH");

if (!string.IsNullOrWhiteSpace(videoPath) && !CameraSimConfig.LooksLikeNetworkLink(videoPath) && !File.Exists(videoPath))
{
    Console.WriteLine($"[CameraPublisherSim] ERROR: CAMERA_VIDEO_PATH not found: {videoPath}");
    return;
}

byte[]? staticFrame = null;
if (string.IsNullOrWhiteSpace(videoPath) && !string.IsNullOrWhiteSpace(imagePath))
{
    staticFrame = await File.ReadAllBytesAsync(imagePath);
    Console.WriteLine($"[CameraPublisherSim] Using static image: {imagePath} ({staticFrame.Length} bytes)");
}

var singleIngestUri = new Uri($"{wsBase.TrimEnd('/')}/{cameraId}/ingest");

Console.WriteLine($"[CameraPublisherSim] Target: {singleIngestUri}");
Console.WriteLine($"[CameraPublisherSim] FPS: {defaultFps}");

if (!string.IsNullOrWhiteSpace(videoPath))
{
    Console.WriteLine($"[CameraPublisherSim] Using local video: {videoPath}");
    if (!VideoFrameSource.IsAvailable())
    {
        Console.WriteLine("[CameraPublisherSim] WARNING: ffmpeg not found (set FFMPEG_PATH or add ffmpeg to PATH) — decoding will fail");
    }
}

Console.WriteLine("[CameraPublisherSim] Press Ctrl+C to stop");

var publisher = new CameraPublisher(singleIngestUri, cameraId, defaultFps, staticFrame, videoPath);
await publisher.RunAsync(cts.Token);
