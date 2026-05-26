using System.Collections.Concurrent;

namespace Backend.Services;

/// <summary>
/// Singleton: maps cameraId → local video file path used as MJPEG stream source.
/// When a camera has an entry here, StreamMjpeg pumps frames from that file via FFMpeg
/// instead of using the synthetic CameraFrameGenerator.
/// </summary>
public sealed class VideoSourceRegistry
{
    private readonly ConcurrentDictionary<string, string> _sources = new();

    public void   Set(string cameraId, string filePath) => _sources[cameraId] = filePath;
    public bool   Clear(string cameraId)                => _sources.TryRemove(cameraId, out _);
    public string? Get(string cameraId)                 => _sources.GetValueOrDefault(cameraId);
}
