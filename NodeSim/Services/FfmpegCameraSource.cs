using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using NodeSim.Config;

namespace NodeSim.Services;

/// <summary>
/// Spawns an FFMpeg process to capture a video source (a looped local file, or a real
/// webcam via FFMpeg's platform capture input) and yields JPEG frames extracted from
/// the resulting MJPEG pipe output. FFMpeg must be available in PATH, or set FFMPEG_PATH.
/// </summary>
public sealed class FfmpegCameraSource
{
    private readonly CameraOptions _opts;
    private readonly ILogger<FfmpegCameraSource> _logger;

    public FfmpegCameraSource(IOptions<CameraOptions> opts, ILogger<FfmpegCameraSource> logger)
    {
        _opts = opts.Value;
        _logger = logger;
    }

    public async IAsyncEnumerable<byte[]> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var ffmpeg = FindFfmpeg();
        var args = BuildArgs();

        _logger.LogInformation("Starting FFMpeg capture: {Ffmpeg} {Args}", ffmpeg, args);

        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo(ffmpeg, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                CreateNoWindow = true,
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

    private string BuildArgs()
    {
        var scale = $"scale={_opts.Width}:{_opts.Height}";

        return _opts.Source switch
        {
            CameraSourceKind.Webcam => OperatingSystem.IsWindows()
                ? $"-f dshow -i video=\"{_opts.WebcamDeviceName}\" " +
                  $"-vf \"{scale}\" -vcodec mjpeg -q:v 3 -r {_opts.OutputFps} -f mjpeg pipe:1 -loglevel warning -nostdin"
                : $"-f v4l2 -i \"{_opts.WebcamDeviceName}\" " +
                  $"-vf \"{scale}\" -vcodec mjpeg -q:v 3 -r {_opts.OutputFps} -f mjpeg pipe:1 -loglevel warning -nostdin",

            _ => $"-stream_loop -1 -re -i \"{_opts.VideoFilePath}\" " +
                 $"-vf \"{scale}\" -vcodec mjpeg -q:v 3 -r {_opts.OutputFps} -f mjpeg pipe:1 -loglevel warning -nostdin",
        };
    }

    // ── JPEG frame parser — splits a raw MJPEG byte stream on SOI/EOI markers ───

    private static async IAsyncEnumerable<byte[]> ParseJpegFramesAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var readBuf = new byte[65536];
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

    // ── FFMpeg discovery ─────────────────────────────────────────────────────

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
        var exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".exe").Split(';');
        return paths.Any(dir =>
            exts.Any(ext => File.Exists(Path.Combine(dir.Trim(), name + ext))));
    }
}
