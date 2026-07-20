using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CameraPublisherSim;

/// <summary>
/// Spawns an ffmpeg process to decode a local video file and yields JPEG frames from the
/// resulting MJPEG pipe output, looping the video indefinitely — mirrors
/// Backend/Services/VideoFrameReader.cs's approach so CameraPublisherSim can "simulate" a
/// camera from a real video file instead of synthetic/static frames.
/// </summary>
public static class VideoFrameSource
{
    private const int Width = 640;
    private const int Height = 480;

    public static async IAsyncEnumerable<byte[]> ReadFramesAsync(
        string videoPath,
        int fps,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var ffmpeg = FindFfmpeg();
        var args = $"-stream_loop -1 -re -i \"{videoPath}\" " +
                   $"-vf \"scale={Width}:{Height}\" " +
                   $"-vcodec mjpeg -q:v 3 -r {fps} -f mjpeg pipe:1 " +
                   "-loglevel warning -nostdin";

        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo(ffmpeg, args)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = false,
                CreateNoWindow         = true,
            }
        };

        proc.Start();

        try
        {
            await foreach (var frame in ParseJpegFramesAsync(proc.StandardOutput.BaseStream, ct))
                yield return frame;
        }
        finally
        {
            if (!proc.HasExited)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            }
        }
    }

    private static async IAsyncEnumerable<byte[]> ParseJpegFramesAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var readBuf  = new byte[65536];
        var frameBuf = new MemoryStream(128 * 1024);
        int prevByte = -1;
        bool inFrame = false;

        while (!ct.IsCancellationRequested)
        {
            int n;
            try { n = await stream.ReadAsync(readBuf, ct); }
            catch (OperationCanceledException) { yield break; }

            if (n == 0) yield break; // EOF / process exited

            for (int i = 0; i < n; i++)
            {
                byte b = readBuf[i];

                if (prevByte == 0xFF && b == 0xD8)
                {
                    frameBuf.SetLength(0);
                    frameBuf.WriteByte(0xFF);
                    frameBuf.WriteByte(0xD8);
                    inFrame = true;
                }
                else if (inFrame)
                {
                    frameBuf.WriteByte(b);

                    if (prevByte == 0xFF && b == 0xD9)
                    {
                        yield return frameBuf.ToArray();
                        frameBuf.SetLength(0);
                        inFrame = false;
                    }
                }

                prevByte = b;
            }
        }
    }

    public static bool IsAvailable() => File.Exists(FindFfmpeg()) || ExistsInPath("ffmpeg");

    private static string FindFfmpeg()
    {
        var env = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

        string[] known =
        [
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
            @"C:\ProgramData\chocolatey\bin\ffmpeg.exe",
            @"C:\ProgramData\scoop\shims\ffmpeg.exe",
        ];

        var found = Array.Find(known, File.Exists);
        return found ?? "ffmpeg"; // assume it's in PATH
    }

    private static bool ExistsInPath(string name)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        var exts  = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".exe").Split(';');
        return paths.Any(dir =>
            exts.Any(ext => File.Exists(Path.Combine(dir.Trim(), name + ext))));
    }
}
