namespace NodeSim.Config;

public enum CameraSourceKind
{
    VideoFile,
    Webcam
}

public sealed class CameraOptions
{
    public const string Section = "Camera";

    /// <summary>Backend base URL, e.g. http://localhost:5280</summary>
    public string BackendUrl { get; init; } = "http://localhost:5280";

    /// <summary>Camera ID this device pushes frames as (must exist in Backend's Cameras table)</summary>
    public string CameraId { get; init; } = "CAM-HUB-01";

    public bool Enabled { get; init; } = true;

    /// <summary>VideoFile loops a local file; Webcam captures a real capture device via FFMpeg.</summary>
    public CameraSourceKind Source { get; init; } = CameraSourceKind.VideoFile;

    /// <summary>Path to a video file to loop when Source = VideoFile.</summary>
    public string VideoFilePath { get; init; } = "";

    /// <summary>FFMpeg capture device name when Source = Webcam (dshow name on Windows, e.g. "Integrated Camera").</summary>
    public string WebcamDeviceName { get; init; } = "";

    public int OutputFps { get; init; } = 15;

    public int Width { get; init; } = 640;

    public int Height { get; init; } = 480;
}
