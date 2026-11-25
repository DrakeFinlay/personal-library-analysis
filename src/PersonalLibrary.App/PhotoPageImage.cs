using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PersonalLibrary.Core;

namespace PersonalLibrary.App;

internal static class PhotoPageImage
{
    public static BitmapSource Load(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > 60_000_000) throw new InvalidOperationException("This photo is too large. Use a photo smaller than 60 megapixels.");
        var orientation = 1;
        if (frame.Metadata is BitmapMetadata metadata)
        {
            try { if (metadata.ContainsQuery("/app1/ifd/{ushort=274}")) orientation = Convert.ToInt32(metadata.GetQuery("/app1/ifd/{ushort=274}")); }
            catch (NotSupportedException) { }
        }
        BitmapSource image = frame;
        var transforms = new TransformGroup();
        if (orientation is 2 or 4 or 5 or 7) transforms.Children.Add(new ScaleTransform(-1, 1));
        var angle = orientation switch { 3 or 4 => 180, 6 or 7 => 90, 5 or 8 => 270, _ => 0 };
        if (angle != 0) transforms.Children.Add(new RotateTransform(angle));
        if (transforms.Children.Count > 0) image = new TransformedBitmap(frame, transforms);
        image.Freeze(); return image;
    }

    public static GrayPhoto Gray(BitmapSource image)
    {
        var gray = new FormatConvertedBitmap(image, PixelFormats.Gray8, null, 0);
        var pixels = new byte[gray.PixelWidth * gray.PixelHeight]; gray.CopyPixels(pixels, gray.PixelWidth, 0);
        return new(gray.PixelWidth, gray.PixelHeight, pixels);
    }

    public static void Prepare(string path, PhotoArea area, bool contrast, string target)
    {
        var result = PhotoPage.Rectify(Gray(Load(path)), area, contrast);
        var bitmap = BitmapSource.Create(result.Width, result.Height, 96, 96, PixelFormats.Gray8, null, result.Pixels, result.Width);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(target); encoder.Save(output);
    }
}
