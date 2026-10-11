using PhotoLab.Core;
using PhotoLab.Imaging;
using PhotoLab.Infrastructure;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace PhotoLab.Desktop;

/// <summary>
/// Standard-image codecs and source-profile conversion. RAW decoding lives in Infrastructure.
/// </summary>
public static class SkiaImageCodec
{
    public static (PixelBuffer preview, int originalWidth, int originalHeight) LoadPreview(string path, int maxEdge = 1800)
    {
        using var decoded = DecodeSrgb(path);
        var width = decoded.Width;
        var height = decoded.Height;
        float ratio = MathF.Min(1f, maxEdge / (float)Math.Max(width, height));
        if (ratio >= 1) return (FromBitmap(decoded), width, height);

        int smallW = Math.Max(1, (int)MathF.Round(width * ratio));
        int smallH = Math.Max(1, (int)MathF.Round(height * ratio));
        using var scaled = new SKBitmap(smallW, smallH, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var canvas = new SKCanvas(scaled))
        using (var paint = new SKPaint { IsAntialias = true })
        {
            canvas.Clear(SKColors.Transparent);
            using var image = SKImage.FromBitmap(decoded);
            canvas.DrawImage(image, new SKRect(0, 0, smallW, smallH),
                new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
            canvas.Flush();
        }
        return (FromBitmap(scaled), width, height);
    }

    /// <summary>Downscale an RGBA8 source to a manageable editing preview.</summary>
    public static PixelBuffer ResizePreview(PixelBuffer source, int maxEdge = 1800)
    {
        float ratio = MathF.Min(1f, maxEdge / (float)Math.Max(source.Width, source.Height));
        if (ratio >= 1f) return source;
        int smallW = Math.Max(1, (int)MathF.Round(source.Width * ratio));
        int smallH = Math.Max(1, (int)MathF.Round(source.Height * ratio));
        // Skia's managed Pixel property avoids pointer assumptions about stride/order.
        using var original = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var colors = new SKColor[source.Width * source.Height];
        for (int i = 0, p = 0; i < colors.Length; i++, p += 4)
            colors[i] = new SKColor(source.Data[p], source.Data[p + 1], source.Data[p + 2], source.Data[p + 3]);
        original.Pixels = colors;
        using var scaled = new SKBitmap(smallW, smallH, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var canvas = new SKCanvas(scaled))
        using (var paint = new SKPaint { IsAntialias = true })
        using (var image = SKImage.FromBitmap(original))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawImage(image, new SKRect(0, 0, smallW, smallH),
                new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
            canvas.Flush();
        }
        return FromBitmap(scaled);
    }

    public static PixelBuffer LoadFull(string path)
    {
        using var bitmap = DecodeSrgb(path);
        return FromBitmap(bitmap);
    }

    /// <summary>Convert decoded source-profile RGB into linear sRGB float before display quantization.</summary>
    public static FloatImageBuffer LoadLinearFull(string path, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException("Cannot decode source image.");
        string? library = ColorLibraries.LittleCms;
        bool useLittleCms = !string.IsNullOrWhiteSpace(library) &&
            codec.EncodedFormat is SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg;
        byte[] icc = useLittleCms ? EmbeddedIccProfile.Read(path) : [];
        // The native transform consumes RGB profiles. Keep gray ICC input on Skia's established profile path.
        if (icc.Length >= 20 && icc.AsSpan(16,4).SequenceEqual("GRAY"u8))
        {
            using var parsedGray = SKColorSpaceIccProfile.Create(icc);
            if (parsedGray == null) throw new InvalidDataException("Invalid grayscale ICC profile.");
            using var grayProfile = SKColorSpace.CreateIcc(parsedGray);
            if (grayProfile == null) throw new InvalidDataException("Cannot interpret grayscale ICC profile.");
            useLittleCms = false;
        }
        // PNG gamma/chromaticity without ICC still needs Skia's parsed color space, not an sRGB assumption.
        if (icc.Length == 0 && codec.Info.ColorSpace is { IsSrgb: false }) useLittleCms = false;
        string? pngBridge = ColorLibraries.Png16;
        bool usePng16 = !string.IsNullOrWhiteSpace(pngBridge) && codec.EncodedFormat == SKEncodedImageFormat.Png &&
            Png16Decoder.Is16Bit(path);
        var sourceInfo = codec.Info.WithColorType(usePng16 ? SKColorType.RgbaF32 : SKColorType.Rgba8888)
            .WithAlphaType(SKAlphaType.Unpremul);
        // LCMS consumes encoded source values. Disable Skia's conversion to avoid applying the ICC twice.
        if (useLittleCms) sourceInfo = sourceInfo.WithColorSpace(null);
        float[]? encoded16 = null;
        if (usePng16)
        {
            var highDepth = Png16Decoder.Decode(path,pngBridge!,cancellation);
            if (highDepth.Width != sourceInfo.Width || highDepth.Height != sourceInfo.Height)
                throw new InvalidDataException("PNG dimensions changed during decoding.");
            encoded16 = highDepth.Rgba;
            if (useLittleCms)
            {
                using var transform = new LittleCmsTransform(library!,icc);
                return transform.Process(highDepth.Width,highDepth.Height,encoded16,cancellation);
            }
            if (codec.Info.ColorSpace == null || codec.Info.ColorSpace.IsSrgb)
                return ImageConvert.FromSrgb32(highDepth.Width,highDepth.Height,encoded16,cancellation);
        }
        using var decoded = new SKBitmap(sourceInfo);
        if (encoded16 != null) Marshal.Copy(encoded16,0,decoded.GetPixels(),encoded16.Length);
        else
        {
            var status = codec.GetPixels(sourceInfo, decoded.GetPixels());
            if (status != SKCodecResult.Success) throw new InvalidDataException("Cannot decode source-profile image: " + status);
        }
        if (useLittleCms)
        {
            using var transform = new LittleCmsTransform(library!, icc);
            var sourceBytes = new byte[checked(decoded.Width * decoded.Height * 4)];
            Marshal.Copy(decoded.GetPixels(), sourceBytes, 0, sourceBytes.Length);
            var encoded = new float[sourceBytes.Length];
            for (int i = 0; i < sourceBytes.Length; i++) encoded[i] = sourceBytes[i] / 255f;
            return transform.Process(decoded.Width, decoded.Height, encoded, cancellation);
        }
        using var image = SKImage.FromBitmap(decoded);
        using var linear = SKColorSpace.CreateSrgbLinear();
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.RgbaF32, SKAlphaType.Unpremul, linear);
        var bytes = GC.AllocateUninitializedArray<byte>(checked(info.Width * info.Height * 16));
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            if (!image.ReadPixels(info, pinned.AddrOfPinnedObject(), info.RowBytes, 0, 0))
                throw new InvalidDataException("Cannot convert image profile into linear sRGB.");
        }
        finally { pinned.Free(); }
        cancellation.ThrowIfCancellationRequested();
        return FloatImageBuffer.FromLinearRgba32(info.Width, info.Height, MemoryMarshal.Cast<byte, float>(bytes));
    }

    private static SKBitmap DecodeSrgb(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException("Cannot decode source image.");
        using var srgb = SKColorSpace.CreateSrgb();
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul, srgb);
        var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) == SKCodecResult.Success) return bitmap;
        bitmap.Dispose(); throw new InvalidDataException("Cannot decode source image as sRGB.");
    }

    private static PixelBuffer FromBitmap(SKBitmap bitmap)
    {
        SKColor[] colors = bitmap.Pixels;
        var rgba = GC.AllocateUninitializedArray<byte>(colors.Length * 4);
        for (int i = 0, d = 0; i < colors.Length; i++, d += 4)
        {
            rgba[d] = colors[i].Red;
            rgba[d + 1] = colors[i].Green;
            rgba[d + 2] = colors[i].Blue;
            rgba[d + 3] = colors[i].Alpha;
        }
        return new PixelBuffer(bitmap.Width, bitmap.Height, rgba);
    }

    public static byte[] Encode(PixelBuffer pixels, bool jpeg, int quality = 93)
    {
        // Export bytes are encoded sRGB; tag that profile rather than copying an incompatible source ICC.
        using var srgb = SKColorSpace.CreateSrgb();
        using var bitmap = new SKBitmap(new SKImageInfo(pixels.Width, pixels.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul, srgb));
        var rgba = pixels.Data;
        var skColors = new SKColor[pixels.Width * pixels.Height];
        for (int i = 0, s = 0; i < skColors.Length; i++, s += 4)
            skColors[i] = new SKColor(rgba[s], rgba[s + 1], rgba[s + 2], jpeg ? (byte)255 : rgba[s + 3]);
        bitmap.Pixels = skColors;
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, quality);
        return encoded?.ToArray() ?? throw new IOException("Could not encode the image.");
    }
}
