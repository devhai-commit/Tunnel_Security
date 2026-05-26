using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Backend.Services;

/// <summary>
/// Generates synthetic JPEG frames for simulated camera streams.
/// Produces a CCTV-style dark image with noise, scan lines, and grid markers.
/// No external fonts required — pixel-only rendering via ImageSharp core.
/// </summary>
public static class CameraFrameGenerator
{
    private const int Width = 640;
    private const int Height = 480;
    private const int JpegQuality = 72;

    [ThreadStatic]
    private static Random? _rng;
    private static Random Rng => _rng ??= new Random(Environment.TickCount + Thread.CurrentThread.ManagedThreadId);

    /// <summary>
    /// Generate one JPEG frame. Thread-safe.
    /// </summary>
    /// <param name="cameraId">Used to derive a distinct hue per camera.</param>
    /// <param name="frameIndex">Drives animation (scan line position, noise seed).</param>
    public static byte[] GenerateFrame(string cameraId, long frameIndex)
    {
        // Deterministic per-camera tint: cycle through green / teal / blue
        int hash = Math.Abs(cameraId.GetHashCode());
        int tintMode = hash % 3; // 0=green, 1=teal, 2=blue

        int scanY = (int)(frameIndex * 4 % Height);

        using var image = new Image<Rgba32>(Width, Height);

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                int scanDist = Math.Abs(y - scanY);
                int scanBoost = scanDist < 4 ? (80 - scanDist * 18) : 0;
                if (scanBoost < 0) scanBoost = 0;

                for (int x = 0; x < Width; x++)
                {
                    // Base: dark CCTV-like background
                    byte r = 8, g = 12, b = 8;
                    switch (tintMode)
                    {
                        case 1: r = 6; g = 12; b = 12; break; // teal
                        case 2: r = 6; g = 8;  b = 14; break; // blue
                    }

                    // Random grain (4% pixel chance)
                    double noise = Rng.NextDouble();
                    if (noise < 0.04)
                    {
                        byte grain = (byte)Rng.Next(28, 85);
                        r = g = b = grain;
                    }

                    // Scan line glow
                    r = (byte)Math.Min(255, r + scanBoost);
                    g = (byte)Math.Min(255, g + scanBoost * 2);
                    b = (byte)Math.Min(255, b + scanBoost);

                    // Faint grid overlay every 80×60 px
                    if (x % 80 == 0 || y % 60 == 0)
                    {
                        r = (byte)Math.Min(255, r + 5);
                        g = (byte)Math.Min(255, g + 8);
                        b = (byte)Math.Min(255, b + 5);
                    }

                    // Corner L-bracket markers (top-left, top-right, bottom-left, bottom-right)
                    bool nearLeft   = x < 22;
                    bool nearRight  = x >= Width - 22;
                    bool nearTop    = y < 22;
                    bool nearBottom = y >= Height - 22;

                    bool onBracket = false;
                    // Horizontal arm of bracket (top 2 rows / bottom 2 rows within corner zone)
                    if ((nearTop    && y  < 3)  || (nearBottom && y  >= Height - 3))
                        if (nearLeft || nearRight) onBracket = true;
                    // Vertical arm of bracket (left 2 cols / right 2 cols within corner zone)
                    if ((nearLeft   && x  < 3)  || (nearRight  && x  >= Width - 3))
                        if (nearTop || nearBottom) onBracket = true;
                    // Inner corner dot
                    if (((x == 21 || x == Width - 22) && nearTop && y < 22) ||
                        ((y == 21 || y == Height - 22) && nearLeft && x < 22))
                        onBracket = true;

                    if (onBracket) { r = 160; g = 220; b = 140; }

                    // Blinking status dot top-right corner (blinks with frameIndex)
                    if (x >= Width - 14 && x <= Width - 8 && y >= 8 && y <= 14)
                    {
                        bool blink = (frameIndex / 8) % 2 == 0;
                        if (blink) { r = 220; g = 50; b = 50; } // red dot = LIVE
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
