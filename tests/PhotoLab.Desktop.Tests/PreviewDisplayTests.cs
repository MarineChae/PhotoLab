using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PhotoLab.Core;
using PhotoLab.Desktop;
using PhotoLab.Imaging;

/// <summary>Skia display ownership checks and paired PNG/direct-bitmap cost measurements.</summary>
static class PreviewDisplayTests
{
    public static void Check()
    {
        byte[] bytes = [255, 0, 0, 255, 0, 255, 0, 128, 4, 5, 6, 0,
            0, 0, 255, 255, 7, 8, 9, 1, 100, 150, 200, 254];
        byte[] expected = bytes.ToArray();
        using var bitmap = PreviewBitmap.Create(new PixelBuffer(3, 2, bytes));
        Array.Clear(bytes);
        GC.Collect(); GC.WaitForPendingFinalizers();
        Assert(bitmap.PixelSize == new PixelSize(3, 2) && bitmap.Format == PixelFormat.Rgba8888 && bitmap.AlphaFormat == AlphaFormat.Unpremul,
            "direct preview declares RGBA order, straight alpha and odd row width");
        // Skia's bitmap copy normalizes RGB at alpha zero, which has no display contribution.
        expected[8] = expected[9] = expected[10] = 0;
        byte[] actual = Read(bitmap);
        Assert(actual.SequenceEqual(expected), "direct preview owns a copy after source mutation/unpin/collection and preserves visible RGB/alpha");
        var opaque = new PixelBuffer(3, 2, expected.Select((value, index) => index % 4 == 3 ? (byte)255 : value).ToArray());
        using var direct = PreviewBitmap.Create(opaque);
        using var stream = new MemoryStream(SkiaImageCodec.Encode(opaque, jpeg: false));
        using var encoded = new Bitmap(stream);
        using var directPng = new MemoryStream(); direct.Save(directPng);
        using var oldPng = new MemoryStream(); encoded.Save(oldPng);
        using var directDecoded = SkiaSharp.SKBitmap.Decode(directPng.ToArray());
        using var oldDecoded = SkiaSharp.SKBitmap.Decode(oldPng.ToArray());
        Assert(directDecoded.Pixels.SequenceEqual(oldDecoded.Pixels), "direct opaque preview matches the former PNG bitmap's sRGB pixels");
    }

    public static async Task BenchmarkAsync()
    {
        foreach (int width in new[] { 600, 1800 })
        {
            int height = width * 2 / 3;
            var bytes = new byte[width * height * 4];
            for (int i = 0; i < bytes.Length; i += 4)
            {
                bytes[i] = (byte)(i / 4 % 256); bytes[i + 1] = (byte)(i / (width * 4) % 256);
                bytes[i + 2] = 120; bytes[i + 3] = 255;
            }
            var pixels = new PixelBuffer(width, height, bytes);
            await PngAsync(); Direct();
            var png = new List<double>(); var direct = new List<double>();
            for (int i = 0; i < 20; i++)
            {
                if (i % 2 == 0) { await MeasurePngAsync(); MeasureDirect(); }
                else { MeasureDirect(); await MeasurePngAsync(); }
            }
            png.Sort(); direct.Sort();
            Console.WriteLine($"DISPLAY_COST: {width}x{height}, 20 alternating pairs, PNG encode/decode median {png[10]:F3}ms p95 {png[18]:F3}ms; " +
                $"direct bitmap median {direct[10]:F3}ms p95 {direct[18]:F3}ms (synthetic RGBA8; excludes float processing and OS presentation)");

            using var linear = ImageConvert.FromSrgb8(pixels);
            using var renderer = new PreviewRenderer(new CpuImageProcessor());
            using var warmup = PreviewBitmap.Create((await renderer.RenderAsync(linear, new EditSettings()))!.Pixels);
            var latencies = new List<double>(); var requests = new List<Task>();
            bool moving = true; int frames = 0;
            var input = Stopwatch.StartNew();
            for (int i = 0; i < 60; i++)
            {
                requests.Add(RenderAsync(i));
                await Task.Delay(16);
            }
            double seconds = input.Elapsed.TotalSeconds; moving = false;
            await Task.WhenAll(requests); latencies.Sort();
            double p95 = latencies.Count == 0 ? double.NaN : latencies[(int)Math.Ceiling(latencies.Count * 0.95) - 1];
            Console.WriteLine($"DISPLAY_INPUT: {width}x{height}, 60 requests over {seconds:F3}s, {frames} accepted bitmap updates during input ({frames / seconds:F1}/s), " +
                $"accepted request p95 {p95:F2}ms (headless UI thread plus float/RGBA8/upload; excludes OS presentation)");

            async Task PngAsync()
            {
                byte[] encoded = await Task.Run(() => SkiaImageCodec.Encode(pixels, jpeg: false));
                using var stream = new MemoryStream(encoded);
                using var bitmap = new Bitmap(stream);
            }
            void Direct() { using var bitmap = PreviewBitmap.Create(pixels); }
            async Task MeasurePngAsync() { var time = Stopwatch.StartNew(); await PngAsync(); png.Add(time.Elapsed.TotalMilliseconds); }
            void MeasureDirect() { var time = Stopwatch.StartNew(); Direct(); direct.Add(time.Elapsed.TotalMilliseconds); }
            async Task RenderAsync(int index)
            {
                var time = Stopwatch.StartNew();
                var frame = await renderer.RenderAsync(linear, new EditSettings { Exposure = index / 60.0 });
                if (frame == null || !renderer.IsCurrent(frame.Version)) return;
                using var bitmap = PreviewBitmap.Create(frame.Pixels);
                if (moving) frames++;
                latencies.Add(time.Elapsed.TotalMilliseconds);
            }
        }
    }

    private static byte[] Read(Bitmap bitmap)
    {
        var result = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        var pinned = GCHandle.Alloc(result, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pinned.AddrOfPinnedObject(), result.Length, bitmap.PixelSize.Width * 4); }
        finally { pinned.Free(); }
        return result;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
