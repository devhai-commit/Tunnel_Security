using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Concurrent;

namespace Backend.Services;

/// <summary>
/// Generates synthetic JPEG frames for simulated camera streams.
/// Design goals:
///   – Fast warm-up: 320×240 instead of 640×480 (4× fewer pixels).
///   – Zero per-frame RNG: noise is pre-baked into 4 rotation maps.
///   – Zero per-frame bitmap allocation after warm-up: all CacheSize frames are
///     rendered once eagerly per camera and then returned as cached byte arrays.
/// </summary>
public static class CameraFrameGenerator
{
    private const int Width       = 320;
    private const int Height      = 240;
    private const int JpegQuality = 72;
    private const int CacheSize   = 30; // 30 frames ≈ 1 s loop at 30 fps

    // 4 pre-baked noise patterns — rotated per frame to give texture variation
    // without per-pixel RNG at render time.
    private const int NoiseVariants = 4;
    private static readonly byte[][] _noises = BuildNoises();

    private static byte[][] BuildNoises()
    {
        var rng = new Random(0x5EED_CAFE);
        var maps = new byte[NoiseVariants][];
        for (int v = 0; v < NoiseVariants; v++)
        {
            var m = new byte[Width * Height];
            for (int i = 0; i < m.Length; i++)
                m[i] = rng.NextDouble() < 0.04 ? (byte)rng.Next(28, 85) : (byte)0;
            maps[v] = m;
        }
        return maps;
    }

    // Lazy<byte[][]> ensures only ONE render pass per camera even under concurrent first requests.
    private static readonly ConcurrentDictionary<string, Lazy<byte[][]>> _cache = new();

    /// <summary>
    /// Returns a pre-rendered JPEG for <paramref name="frameIndex"/>.
    /// All CacheSize frames are generated eagerly on first access per camera.
    /// Subsequent calls are a single array lookup — no allocation.
    /// </summary>
    public static byte[] GenerateFrame(string cameraId, long frameIndex)
    {
        var lazy = _cache.GetOrAdd(cameraId, id =>
            new Lazy<byte[][]>(() =>
            {
                var arr = new byte[CacheSize][];
                for (int i = 0; i < CacheSize; i++)
                    arr[i] = RenderFrame(id, i);
                return arr;
            }, LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value[frameIndex % CacheSize];
    }

    private static byte[] RenderFrame(string cameraId, long frameIndex)
    {
        int tintMode = Math.Abs(cameraId.GetHashCode()) % 3;
        // Scan line sweeps the full height across the cache window
        int scanY = (int)((double)frameIndex / CacheSize * Height);
        var noise = _noises[frameIndex % NoiseVariants];

        using var image = new Image<Rgba32>(Width, Height);

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                int scanDist = Math.Abs(y - scanY);
                int scanBoost = scanDist < 4 ? Math.Max(0, 80 - scanDist * 18) : 0;

                for (int x = 0; x < Width; x++)
                {
                    byte r = 8, g = 12, b = 8;
                    switch (tintMode)
                    {
                        case 1: r = 6; g = 12; b = 12; break; // teal
                        case 2: r = 6; g = 8;  b = 14; break; // blue
                    }

                    // Pre-baked grain (no RNG per pixel)
                    byte grain = noise[y * Width + x];
                    if (grain > 0) { r = g = b = grain; }

                    r = (byte)Math.Min(255, r + scanBoost);
                    g = (byte)Math.Min(255, g + scanBoost * 2);
                    b = (byte)Math.Min(255, b + scanBoost);

                    // Grid (scaled for 320×240)
                    if (x % 40 == 0 || y % 30 == 0)
                    {
                        r = (byte)Math.Min(255, r + 5);
                        g = (byte)Math.Min(255, g + 8);
                        b = (byte)Math.Min(255, b + 5);
                    }

                    // Corner L-bracket markers
                    bool nearLeft   = x < 11;
                    bool nearRight  = x >= Width - 11;
                    bool nearTop    = y < 11;
                    bool nearBottom = y >= Height - 11;
                    bool onBracket  = false;
                    if ((nearTop && y < 2) || (nearBottom && y >= Height - 2))
                        if (nearLeft || nearRight) onBracket = true;
                    if ((nearLeft && x < 2) || (nearRight && x >= Width - 2))
                        if (nearTop || nearBottom) onBracket = true;
                    if (onBracket) { r = 160; g = 220; b = 140; }

                    // Blinking status dot (top-right)
                    if (x >= Width - 7 && x <= Width - 4 && y >= 4 && y <= 7)
                    {
                        bool blink = (frameIndex / 8) % 2 == 0;
                        if (blink) { r = 220; g = 50; b = 50; }
                        else       { r = 40;  g = 10; b = 10; }
                    }

                    row[x] = new Rgba32(r, g, b, 255);
                }
            }
        });

        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms, new JpegEncoder { Quality = JpegQuality });
        return ms.ToArray();
    }
}
