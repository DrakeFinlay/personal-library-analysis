namespace PersonalLibrary.Core;

public readonly record struct PhotoPoint(double X, double Y);
public sealed record PhotoArea(PhotoPoint TopLeft, PhotoPoint TopRight, PhotoPoint BottomRight, PhotoPoint BottomLeft)
{
    public static PhotoArea Whole { get; } = new(new(0, 0), new(1, 0), new(1, 1), new(0, 1));
    public PhotoPoint[] Corners => [TopLeft, TopRight, BottomRight, BottomLeft];
    public void Validate()
    {
        var points = Corners;
        if (points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X < 0 || p.X > 1 || p.Y < 0 || p.Y > 1))
            throw new InvalidOperationException("Keep the four corners inside the photo.");
        var area = 0d;
        for (var i = 0; i < 4; i++)
        {
            var a = points[i]; var b = points[(i + 1) % 4]; var c = points[(i + 2) % 4];
            if ((b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X) <= 0)
                throw new InvalidOperationException("Select a convex page in clockwise order: top left, top right, bottom right, bottom left.");
            area += a.X * b.Y - b.X * a.Y;
        }
        if (area < .01) throw new InvalidOperationException("Select a larger page area.");
    }
}

public sealed record GrayPhoto(int Width, int Height, byte[] Pixels);

public static class PhotoPage
{
    // Project the selected quadrilateral onto a flat rectangle. Coordinates refer
    // to the orientation-corrected original, independent of preview/display size.
    public static GrayPhoto Rectify(GrayPhoto source, PhotoArea area, bool improveContrast = true)
    {
        area.Validate();
        if (source.Width < 2 || source.Height < 2 || source.Pixels.LongLength != (long)source.Width * source.Height)
            throw new InvalidOperationException("Invalid photo pixels.");
        var p = area.Corners.Select(p => new PhotoPoint(p.X * (source.Width - 1), p.Y * (source.Height - 1))).ToArray();
        static double Distance(PhotoPoint a, PhotoPoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        var w = (Distance(p[0], p[1]) + Distance(p[3], p[2])) / 2;
        var h = (Distance(p[0], p[3]) + Distance(p[1], p[2])) / 2;
        var scale = Math.Min(1, 3999 / Math.Max(w, h));
        var width = Math.Max(2, (int)Math.Round(w * scale) + 1); var height = Math.Max(2, (int)Math.Round(h * scale) + 1);
        var dx1 = p[1].X - p[2].X; var dx2 = p[3].X - p[2].X;
        var dy1 = p[1].Y - p[2].Y; var dy2 = p[3].Y - p[2].Y;
        var sx = p[0].X - p[1].X + p[2].X - p[3].X; var sy = p[0].Y - p[1].Y + p[2].Y - p[3].Y;
        var denominator = dx1 * dy2 - dx2 * dy1;
        var g = (sx * dy2 - dx2 * sy) / denominator; var k = (dx1 * sy - sx * dy1) / denominator;
        var a = p[1].X - p[0].X + g * p[1].X; var b = p[3].X - p[0].X + k * p[3].X;
        var d = p[1].Y - p[0].Y + g * p[1].Y; var e = p[3].Y - p[0].Y + k * p[3].Y;
        var pixels = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            var v = y / (double)(height - 1);
            for (var x = 0; x < width; x++)
            {
                var u = x / (double)(width - 1); var z = g * u + k * v + 1;
                var xx = Math.Clamp((a * u + b * v + p[0].X) / z, 0, source.Width - 1);
                var yy = Math.Clamp((d * u + e * v + p[0].Y) / z, 0, source.Height - 1);
                var ix = Math.Min((int)xx, source.Width - 2); var iy = Math.Min((int)yy, source.Height - 2);
                var fx = xx - ix; var fy = yy - iy; var offset = iy * source.Width + ix;
                pixels[y * width + x] = (byte)Math.Clamp((1 - fy) * ((1 - fx) * source.Pixels[offset] + fx * source.Pixels[offset + 1]) +
                    fy * ((1 - fx) * source.Pixels[offset + source.Width] + fx * source.Pixels[offset + source.Width + 1]), 0, 255);
            }
        }
        if (improveContrast) Normalize(pixels, width, height);
        return new(width, height, pixels);
    }

    private static void Normalize(byte[] pixels, int width, int height)
    {
        // Local illumination normalization removes broad page shadows while
        // retaining grayscale antialiasing; no hard threshold erases thin italics.
        var stride = width + 1; var integral = new long[stride * (height + 1)];
        for (var y = 0; y < height; y++)
        {
            long row = 0;
            for (var x = 0; x < width; x++) { row += pixels[y * width + x]; integral[(y + 1) * stride + x + 1] = integral[y * stride + x + 1] + row; }
        }
        var radius = Math.Max(12, width / 35);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var l = Math.Max(0, x - radius); var r = Math.Min(width, x + radius + 1);
            var t = Math.Max(0, y - radius); var b = Math.Min(height, y + radius + 1);
            var mean = (integral[b * stride + r] - integral[t * stride + r] - integral[b * stride + l] + integral[t * stride + l]) / (double)((r - l) * (b - t));
            pixels[y * width + x] = (byte)Math.Clamp(pixels[y * width + x] * 245d / Math.Max(1, mean), 0, 255);
        }
    }
}
