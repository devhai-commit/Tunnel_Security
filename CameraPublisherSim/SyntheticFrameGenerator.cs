using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace CameraPublisherSim;

/// <summary>
/// Procedurally renders a fake camera frame via direct pixel writes (no ffmpeg, no real
/// footage, no SixLabors.ImageSharp.Drawing dependency) so the WebSocket ingest → relay →
/// view pipeline can be exercised end-to-end.
/// </summary>
public static class SyntheticFrameGenerator
{
    private const int Width = 640;
    private const int Height = 480;
    private const int ScanLineHeight = 4;
    private const int MarkerSize = 16;

    public static byte[] GenerateFrame(int frameIndex, string cameraId)
    {
        var background = HueToRgb((frameIndex % 360) / 360f);

        using var image = new Image<Rgb24>(Width, Height, background);

        var scanY = (frameIndex * 6) % Height;
        for (var y = scanY; y < Math.Min(scanY + ScanLineHeight, Height); y++)
        {
            for (var x = 0; x < Width; x++)
            {
                image[x, y] = new Rgb24(255, 255, 255);
            }
        }

        var blinkOn = (frameIndex % 10) < 5;
        if (blinkOn)
        {
            for (var y = 8; y < 8 + MarkerSize; y++)
            {
                for (var x = Width - 8 - MarkerSize; x < Width - 8; x++)
                {
                    image[x, y] = new Rgb24(220, 20, 20);
                }
            }
        }

        using var stream = new MemoryStream();
        image.Save(stream, new JpegEncoder { Quality = 75 });
        return stream.ToArray();
    }

    private static Rgb24 HueToRgb(float hue)
    {
        const float saturation = 0.6f;
        const float value = 0.5f;

        var h = hue * 6f;
        var i = (int)h;
        var f = h - i;
        var p = value * (1 - saturation);
        var q = value * (1 - saturation * f);
        var t = value * (1 - saturation * (1 - f));

        var (r, g, b) = (i % 6) switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q),
        };

        return new Rgb24((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
}
