using CameraPublisherSim;

var wsBase = Environment.GetEnvironmentVariable("CAMERA_BACKEND_WS") ?? "ws://localhost:5080/ws/camera";
var cameraId = Environment.GetEnvironmentVariable("CAMERA_ID") ?? "CAM-HUB-01";
var fps = int.TryParse(Environment.GetEnvironmentVariable("CAMERA_FPS"), out var parsedFps) ? parsedFps : 5;
var imagePath = Environment.GetEnvironmentVariable("CAMERA_IMAGE_PATH");

byte[]? staticFrame = null;
if (!string.IsNullOrWhiteSpace(imagePath))
{
    staticFrame = await File.ReadAllBytesAsync(imagePath);
    Console.WriteLine($"[CameraPublisherSim] Using static image: {imagePath} ({staticFrame.Length} bytes)");
}

var ingestUri = new Uri($"{wsBase.TrimEnd('/')}/{cameraId}/ingest");

Console.WriteLine("[CameraPublisherSim] Camera frame WebSocket publisher");
Console.WriteLine($"[CameraPublisherSim] Target: {ingestUri}");
Console.WriteLine($"[CameraPublisherSim] FPS: {fps}");
Console.WriteLine("[CameraPublisherSim] Press Ctrl+C to stop");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var publisher = new CameraPublisher(ingestUri, cameraId, fps, staticFrame);
await publisher.RunAsync(cts.Token);
