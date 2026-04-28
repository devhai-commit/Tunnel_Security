using System.Threading.Channels;
using Backend.Hubs;
using Backend.Models;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Services;

/// <summary>
/// Singleton BackgroundService that decouples ingestion from SignalR fan-out.
/// Drains Channel&lt;SensorBroadcastMessage&gt; every 500ms via PeriodicTimer.
/// This is the ONLY component that calls IHubContext&lt;SensorHub&gt;.
/// </summary>
public sealed class SensorBroadcastQueue : BackgroundService
{
    private const int Capacity = 5000;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(500);

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
        _logger.LogInformation(
            "[BroadcastQueue] Started — capacity={Capacity} flush={FlushMs}ms",
            Capacity, FlushInterval.TotalMilliseconds);

        using var timer  = new PeriodicTimer(FlushInterval);
        var       buffer = new List<SensorBroadcastMessage>(128);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                DrainInto(buffer);
                if (buffer.Count > 0)
                {
                    await FlushAsync(buffer, stoppingToken);
                    buffer.Clear();
                }
            }
        }
        catch (OperationCanceledException) { /* expected on host shutdown */ }
        finally
        {
            // Best-effort drain of remaining items on shutdown (2s window).
            DrainInto(buffer);
            if (buffer.Count > 0)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await FlushAsync(buffer, cts.Token); }
                catch { /* swallow — host is shutting down */ }
            }
            _logger.LogInformation("[BroadcastQueue] Stopped");
        }
    }

    private void DrainInto(List<SensorBroadcastMessage> buffer)
    {
        while (_channel.Reader.TryRead(out var msg))
            buffer.Add(msg);
    }

    private async Task FlushAsync(IReadOnlyList<SensorBroadcastMessage> batch, CancellationToken ct)
    {
        try
        {
            // One SendAsync per message preserves the existing "SensorUpdated" + SensorUpdateDto
            // wire contract — clients expect individual DTO objects, not arrays.
            foreach (var m in batch)
                await _hub.Clients.All.SendAsync("SensorUpdated", m, ct);

            _logger.LogDebug("[BroadcastQueue] Flushed {Count} messages", batch.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("[BroadcastQueue] Flush failed ({Count} messages): {Msg}",
                batch.Count, ex.Message);
        }
    }
}
