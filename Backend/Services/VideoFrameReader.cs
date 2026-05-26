using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Backend.Services;

/// <summary>
/// Spawns an FFMpeg process to decode a video file and yields JPEG frames
/// extracted from the resulting MJPEG pipe output.
/// FFMpeg must be available in PATH or at a known location (see FindFfmpeg).
/// Set the FFMPEG_PATH environment variable to override.
/// </summary>
public static class VideoFrameReader
{
    private const int Width     = 640;
    private const int Height    = 480;
    private const int OutputFps = 15;

    /// <summary>
    /// Yields JPEG frames from <paramref name="videoPath"/> in a real-time loop.
    /// The video loops indefinitely until <paramref name="ct"/> is cancelled.
    /// </summary>
    public static async IAsyncEnumerable<byte[]> ReadFramesAsync(
        string videoPath,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var ffmpeg = FindFfmpeg();
        // -stream_loop -1   : loop the input indefinitely
        // -re               : read at native frame rate (real-time)
        // -vf scale=WxH     : normalize frame size
        // -vcodec mjpeg     : encode each frame as JPEG
        // -q:v 3            : JPEG quality (1=best … 31=worst; 3 ≈ 90%)
        // -r OutputFps      : output frame rate
        // -f mjpeg pipe:1   : write raw JPEG sequence to stdout
        var args = $"-stream_loop -1 -re -i \"{videoPath}\" " +
                   $"-vf \"scale={Width}:{Height}\" " +
                   $"-vcodec mjpeg -q:v 3 -r {OutputFps} -f mjpeg pipe:1 " +
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

    // ── JPEG frame parser ─────────────────────────────────────────────────────

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
                    // SOI — start (or restart) a frame
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
                        // EOI — complete frame
                        yield return frameBuf.ToArray();
                        frameBuf.SetLength(0);
                        inFrame = false;
                    }
                }

                prevByte = b;
            }
        }
    }

    // ── FFMpeg discovery ──────────────────────────────────────────────────────

    public static bool IsAvailable() => File.Exists(FindFfmpeg()) || ExistsInPath("ffmpeg");

    /// <summary>Returns the resolved FFmpeg executable path (full path or "ffmpeg" if only in PATH).</summary>
    public static string GetFfmpegPath() => FindFfmpeg();

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
