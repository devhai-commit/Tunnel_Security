using Microsoft.Extensions.Options;
using NodeSim.Config;

namespace NodeSim.Services;

/// <summary>
/// BackgroundService that captures frames via FFMpeg (webcam or looped video file)
/// and pushes each one to the Backend as it arrives. Reconnects with a delay if the
/// capture pipe or the Backend is unavailable.
/// </summary>
public sealed class CameraPushWorker : BackgroundService
{
    private readonly FfmpegCameraSource _source;
    private readonly CameraPushTransport _transport;
    private readonly CameraOptions _opts;
    private readonly ILogger<CameraPushWorker> _logger;

    public CameraPushWorker(
        FfmpegCameraSource source,
        CameraPushTransport transport,
        IOptions<CameraOptions> opts,
        ILogger<CameraPushWorker> logger)
    {
        _source = source;
        _transport = transport;
        _opts = opts.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opts.Enabled)
        {
            _logger.LogInformation("Camera pipeline disabled — skipping");
            return;
        }

        if (!FfmpegCameraSource.IsAvailable())
        {
            _logger.LogWarning("FFMpeg not found (PATH or FFMPEG_PATH). Camera pipeline will not run.");
            return;
        }

        _logger.LogInformation(
            "CameraPushWorker starting — Backend: {Url}, Camera: {CameraId}, Source: {Source}",
            _opts.BackendUrl, _opts.CameraId, _opts.Source);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var frame in _source.ReadFramesAsync(stoppingToken))
                {
                    await _transport.PushFrameAsync(frame, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Camera capture pipe ended unexpectedly, retrying in 5s...");
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                try { await Task.Delay(5_000, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
