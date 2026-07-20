using System.Net.WebSockets;

namespace CameraPublisherSim;

/// <summary>
/// Owns a persistent WebSocket connection to the BackendV2 camera ingest endpoint and
/// keeps sending frames, reconnecting on failure so the sim can outlive backend restarts.
/// </summary>
public sealed class CameraPublisher
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly Uri _ingestUri;
    private readonly string _cameraId;
    private readonly int _fps;
    private readonly TimeSpan _frameInterval;
    private readonly byte[]? _staticFrame;
    private readonly string? _videoPath;

    public CameraPublisher(Uri ingestUri, string cameraId, int fps, byte[]? staticFrame, string? videoPath = null)
    {
        _ingestUri = ingestUri;
        _cameraId = cameraId;
        _fps = fps;
        _frameInterval = TimeSpan.FromSeconds(1.0 / Math.Max(1, fps));
        _staticFrame = staticFrame;
        _videoPath = videoPath;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var frameIndex = 0;

        void LogProgress(int length)
        {
            if (++frameIndex % 25 == 0)
            {
                Console.WriteLine($"[CameraPublisher:{_cameraId}] Sent {frameIndex} frames ({length} bytes last frame)");
            }
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();

            try
            {
                await socket.ConnectAsync(_ingestUri, cancellationToken);
                Console.WriteLine($"[CameraPublisher:{_cameraId}] Connected to {_ingestUri}");

                if (_videoPath is not null)
                {
                    await foreach (var frame in VideoFrameSource.ReadFramesAsync(_videoPath, _fps, cancellationToken))
                    {
                        if (socket.State != WebSocketState.Open) break;

                        await socket.SendAsync(frame, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
                        LogProgress(frame.Length);
                    }
                }
                else
                {
                    while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                    {
                        var frame = _staticFrame ?? SyntheticFrameGenerator.GenerateFrame(frameIndex, _cameraId);
                        await socket.SendAsync(frame, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
                        LogProgress(frame.Length);

                        await Task.Delay(_frameInterval, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CameraPublisher:{_cameraId}] Connection error: {ex.Message} — retrying in {ReconnectDelay.TotalSeconds}s");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(ReconnectDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Console.WriteLine($"[CameraPublisher:{_cameraId}] Stopped");
    }
}
