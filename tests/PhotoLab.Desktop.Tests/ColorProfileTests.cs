using PhotoLab.Core;
using PhotoLab.Desktop;
using SkiaSharp;
using System.Runtime.InteropServices;

/// <summary>Tagged wide-gamut input and explicit output encoding through actual JPEG/PNG files.</summary>
static class ColorProfileTests
{
    public static async Task RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "photolab-color-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var p3 = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);
            using var bitmap = new SKBitmap(new SKImageInfo(2,1,SKColorType.Rgba8888,SKAlphaType.Unpremul,p3));
            // Write encoded P3 bytes directly: SetPixel/Pixels interprets SKColor as sRGB and converts it.
            Marshal.Copy(new byte[] {255,0,0,255,128,128,128,128},0,bitmap.GetPixels(),8);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png,100);
            string path = Path.Combine(directory,"p3.png");await File.WriteAllBytesAsync(path,encoded.ToArray());
            using (var codec = SKCodec.Create(path)) Check(codec.Info.ColorSpace != null && !codec.Info.ColorSpace.IsSrgb, "fixture PNG contains a non-sRGB source color profile");
            using var linear = SkiaImageCodec.LoadLinearFull(path);
            Check(Math.Abs(linear.Pixels[0]-1.224745f)<0.002f && Math.Abs(linear.Pixels[1]+0.042058f)<0.002f && Math.Abs(linear.Pixels[2]+0.019642f)<0.002f,
                $"P3 red maps to unclipped linear sRGB ({linear.Pixels[0]:G6}, {linear.Pixels[1]:G6}, {linear.Pixels[2]:G6})");
            Check(Math.Abs(linear.Pixels[4]-0.21586f)<0.001f && Math.Abs(linear.Pixels[5]-linear.Pixels[4])<0.001f && Math.Abs(linear.Pixels[7]-128/255f)<1e-6,
                "ICC conversion preserves neutral gray and straight alpha");
            await using var loader = new PhotoImageLoader();
            using var loaded = await loader.LoadLinearFullAsync(path);
            Check(loaded.Pixels.SequenceEqual(linear.Pixels), "app float loader applies embedded source-profile conversion before edits");
            var bytes = new byte[256*4];
            for (int i=0;i<256;i++) { bytes[i*4]=bytes[i*4+1]=bytes[i*4+2]=(byte)i;bytes[i*4+3]=255; }
            var pixels = new PixelBuffer(256,1,bytes);
            foreach (bool jpeg in new[] {false,true})
            {
                string output = Path.Combine(directory,jpeg?"srgb.jpg":"srgb.png");
                await File.WriteAllBytesAsync(output,SkiaImageCodec.Encode(pixels,jpeg));
                using var codec = SKCodec.Create(output);
                Check(codec.Info.ColorSpace != null && codec.Info.ColorSpace.IsSrgb, "export declares sRGB color encoding: " + Path.GetExtension(output));
                using var roundtrip = SkiaImageCodec.LoadLinearFull(output);
                Check(roundtrip.Width==256 && roundtrip.Height==1, "tagged export decodes into working linear sRGB: " + Path.GetExtension(output));
            }
            string untagged = Path.Combine(directory,"untagged.png");
            using var plain = new SKBitmap(1,1,SKColorType.Rgba8888,SKAlphaType.Unpremul);
            plain.Pixels=[new SKColor(128,128,128)];
            using var plainImage=SKImage.FromBitmap(plain);using var plainBytes=plainImage.Encode(SKEncodedImageFormat.Png,100);
            await File.WriteAllBytesAsync(untagged,plainBytes.ToArray());
            using var assumed = SkiaImageCodec.LoadLinearFull(untagged);
            Check(Math.Abs(assumed.Pixels[0]-0.21586f)<0.001f, "untagged RGB uses the existing sRGB assumption");
        }
        finally { Directory.Delete(directory,recursive:true); }
    }
    private static void Check(bool condition,string message)
    {
        if (!condition) throw new Exception("FAIL: "+message);
        Console.WriteLine("PASS: "+message);
    }
}
