namespace CameraPublisherSim;

/// <summary>One line of cameras.txt: a camera id mapped to a local video path or network link.</summary>
public sealed record CameraSimEntry(string CameraId, string VideoPath, int? Fps);

/// <summary>
/// Parses cameras.txt so multiple cameras can be simulated from one file instead of one
/// CAMERA_ID/CAMERA_VIDEO_PATH env var pair per process.
/// </summary>
public static class CameraSimConfig
{
    public static IReadOnlyList<CameraSimEntry> Load(string path)
    {
        var entries = new List<CameraSimEntry>();

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var cameraId = line[..eq].Trim();
            var rest = line[(eq + 1)..].Trim();
            if (cameraId.Length == 0 || rest.Length == 0) continue;

            var parts = rest.Split(',', 2);
            var videoPath = parts[0].Trim();
            int? fps = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var parsedFps) ? parsedFps : null;

            entries.Add(new CameraSimEntry(cameraId, videoPath, fps));
        }

        return entries;
    }

    public static bool LooksLikeNetworkLink(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" or "rtsp" or "rtmp";
}
