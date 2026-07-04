using Microsoft.Extensions.Options;
using NodeSim.Config;

namespace NodeSim.Services;

/// <summary>
/// Sends a single captured JPEG frame to the Backend via
/// POST /api/cameras/{id}/push-frame
/// </summary>
public sealed class CameraPushTransport
{
    private readonly IHttpClientFactory _factory;
    private readonly CameraOptions _opts;
    private readonly ILogger<CameraPushTransport> _logger;

    public CameraPushTransport(
        IHttpClientFactory factory,
        IOptions<CameraOptions> opts,
        ILogger<CameraPushTransport> logger)
    {
        _factory = factory;
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task PushFrameAsync(byte[] jpeg, CancellationToken ct)
    {
        var url = $"{_opts.BackendUrl.TrimEnd('/')}/api/cameras/{_opts.CameraId}/push-frame";
        using var http = _factory.CreateClient("NodeCamera");
        try
        {
            using var content = new ByteArrayContent(jpeg);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

            var response = await http.PostAsync(url, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Frame push failed for {CameraId}: HTTP {StatusCode}",
                    _opts.CameraId, (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Frame push error for {CameraId}: {Message}", _opts.CameraId, ex.Message);
        }
    }
}
