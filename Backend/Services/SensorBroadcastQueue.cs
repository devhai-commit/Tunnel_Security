using System.Threading.Channels;
using Backend.Hubs;
using Backend.Models;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Services;

/// <summary>
/// Singleton BackgroundService that decouples ingestion from SignalR fan-out.
/// Forwards each message immediately as it is dequeued — no batch window.
/// This is the ONLY component that calls IHubContext&lt;SensorHub&gt;.
/// </summary>
public sealed class SensorBroadcastQueue : BackgroundService
{
    private const int Capacity = 5000;

    private readonly Channel<SensorBroadcastMessage> _channel;
    private readonly IHubContext<SensorHub> _hub;
    private readonly ILogger<SensorBroadcastQueue> _logger;

    public SensorBroadcastQueue(IHubContext<SensorHub> hub, ILogger<SensorBroadcastQueue> logger)
    {
        _hub    = hub;
        _logger = logger;
        _channel = Channel.CreateBounded<SensorBroadcastMessage>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode                     = BoundedChannelFullMode.DropOldest,
                SingleReader                 = true,
                SingleWriter                 = false,
                AllowSynchronousContinuations = false
            });
    }

    /// <summary>
    /// Non-blocking enqueue. With DropOldest the oldest item is evicted to make room,
    /// so writes always succeed while the writer is open.
    /// </summary>
    public void Enqueue(SensorBroadcastMessage msg) => _channel.Writer.TryWrite(msg);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[BroadcastQueue] Started — capacity={Capacity}", Capacity);

        try
        {
            await foreach (var msg in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await _hub.Clients.All.SendAsync("SensorUpdated", msg, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("[BroadcastQueue] Send failed: {Msg}", ex.Message);
                }
            }
        }
        catch (OperationCanceledException) { /* expected on host shutdown */ }
        finally
        {
            _logger.LogInformation("[BroadcastQueue] Stopped");
        }
    }
}
