using System.Collections.Concurrent;

namespace Backend.Services;

/// <summary>
/// Singleton in-memory buffer for camera frames pushed by external simulators.
/// Each Push records the frame and a tick timestamp so StreamMjpeg can detect
/// when the simulator has gone stale and fall back to synthetic frames.
/// </summary>
public sealed class CameraFrameBuffer
{
    private const long StalenessTicks = 2_000; // ms — fall back to synthetic after 2s silence

    private readonly ConcurrentDictionary<string, FrameSlot> _slots = new();

    public void Push(string cameraId, byte[] jpeg)
        => _slots.GetOrAdd(cameraId, _ => new FrameSlot()).Push(jpeg);

    /// <summary>
    /// Returns the latest pushed frame if one was received within the staleness window.
    /// Returns false (frame = null) when no frame has ever been pushed or the last push
    /// was too long ago — caller should fall back to a synthetic frame.
    /// </summary>
    public bool TryGetLatest(string cameraId, out byte[]? frame)
    {
        if (_slots.TryGetValue(cameraId, out var slot))
        {
            var (f, tickMs) = slot.GetLatest();
            if (f != null && Environment.TickCount64 - tickMs < StalenessTicks)
            {
                frame = f;
                return true;
            }
        }
        frame = null;
        return false;
    }

    public bool Has(string cameraId)
        => _slots.TryGetValue(cameraId, out var s) && s.Latest != null;

    public void Clear(string cameraId) => _slots.TryRemove(cameraId, out _);

    // ── Per-camera slot ────────────────────────────────────────────────────────

    private sealed class FrameSlot
    {
        private volatile byte[]? _latest;
        private long _pushedAtMs;

        public byte[]? Latest => _latest;

        public (byte[]? Frame, long TickMs) GetLatest()
            => (_latest, Interlocked.Read(ref _pushedAtMs));

        public void Push(byte[] frame)
        {
            _latest = frame;
            Interlocked.Exchange(ref _pushedAtMs, Environment.TickCount64);
        }
    }
}
