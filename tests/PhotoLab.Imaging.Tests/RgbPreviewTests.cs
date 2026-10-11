using System.Diagnostics;
using PhotoLab.Imaging;

/// <summary>RGB16 direct-resize comparison with expand/area-resize and paired allocation/time measurements.</summary>
static class RgbPreviewTests
{
    public static void Run()
    {
        var random = new Random(5819);
        int cases = 0;
        foreach (int width in new[] { 1, 2, 3, 7, 17, 61 })
        foreach (int height in new[] { 1, 2, 5, 11, 33 })
        {
            var rgb = new ushort[width * height * 3];
            for (int i = 0; i < rgb.Length; i++) rgb[i] = (ushort)random.Next(65536);
            rgb[0] = 0; rgb[1] = 1; rgb[2] = 65535;
            ushort[] original = rgb.ToArray();
            using var expanded = FloatImageBuffer.FromLinearRgb16(width, height, rgb);
            foreach (int edge in new[] { 1, 2, 4, 13, 100 })
            {
                using var expected = ImageResize.Preview(expanded, edge);
                using var actual = ImageResize.PreviewRgb16(width, height, rgb, edge);
                if (expected.Width != actual.Width || expected.Height != actual.Height || !expected.Pixels.SequenceEqual(actual.Pixels))
                    throw new Exception($"FAIL: direct RGB16 area preview differs at {width}x{height} -> {edge}");
                if (!actual.Pixels.SequenceEqual(AreaReference(expanded, edge)))
                    throw new Exception($"FAIL: direct RGB16 differs from independent area integral at {width}x{height} -> {edge}");
                cases++;
            }
            if (!rgb.SequenceEqual(original)) throw new Exception("FAIL: RGB16 source modified");
        }
        Console.WriteLine($"PASS: {cases} direct RGB16 geometries preserve every expanded-reference float and input sample");
        var allSamples = new ushort[256 * 256 * 3];
        for (int i = 0; i < 65536; i++) allSamples[i * 3] = allSamples[i * 3 + 1] = allSamples[i * 3 + 2] = (ushort)i;
        using var allExpanded = FloatImageBuffer.FromLinearRgb16(256, 256, allSamples);
        using var allExpected = ImageResize.Preview(allExpanded, 128);
        using var allActual = ImageResize.PreviewRgb16(256, 256, allSamples, 128);
        if (!allActual.Pixels.SequenceEqual(allExpected.Pixels)) throw new Exception("FAIL: RGB16 normalization changed float rounding");
        Console.WriteLine("PASS: all 65536 RGB16 values preserve expanded-reference float rounding through optimized area preview");
        Reject(() => ImageResize.PreviewRgb16(0, 1, [], 1), "RGB16 preview rejects zero dimensions");
        Reject(() => ImageResize.PreviewRgb16(2, 1, [1, 2, 3], 1), "RGB16 preview rejects mismatched payload");
        Reject(() => ImageResize.PreviewRgb16(1, 1, [1, 2, 3], 0), "RGB16 preview rejects invalid edge");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { ImageResize.PreviewRgb16(1, 1, [1, 2, 3], 1, cancelled.Token); throw new Exception("FAIL: cancelled RGB16 resize succeeded"); }
        catch (OperationCanceledException) { Console.WriteLine("PASS: RGB16 preview rejects pre-cancellation before allocation"); }
    }

    public static void Benchmark()
    {
        foreach (var size in new[] { (Width: 2154, Height: 1434), (Width: 3000, Height: 2000) })
        {
            var rgb = new ushort[size.Width * size.Height * 3];
            for (int i = 0; i < rgb.Length; i++) rgb[i] = (ushort)(i * 137);
            using (var warm = Old()) { }
            using (var warm = Direct()) { }
            var oldTimes = new List<double>(); var directTimes = new List<double>();
            var oldBytes = new List<long>(); var directBytes = new List<long>();
            for (int i = 0; i < 10; i++)
            {
                if (i % 2 == 0) { Measure(Old, oldTimes, oldBytes); Measure(Direct, directTimes, directBytes); }
                else { Measure(Direct, directTimes, directBytes); Measure(Old, oldTimes, oldBytes); }
            }
            oldTimes.Sort(); directTimes.Sort(); oldBytes.Sort(); directBytes.Sort();
            Console.WriteLine($"RGB16_PREVIEW_COST: {size.Width}x{size.Height}->600, 10 alternating pairs, expand+resize median {oldTimes[5]:F3}ms / {oldBytes[5]} allocated bytes; " +
                $"direct median {directTimes[5]:F3}ms / {directBytes[5]} allocated bytes; input already managed, native development/copy and UI excluded, output pool warmed.");
            FloatImageBuffer Old()
            {
                using var expanded = FloatImageBuffer.FromLinearRgb16(size.Width, size.Height, rgb);
                return ImageResize.Preview(expanded, 600);
            }
            FloatImageBuffer Direct() => ImageResize.PreviewRgb16(size.Width, size.Height, rgb, 600);
        }
    }

    // Independent opaque area integral visits the entire input, avoiding the production footprint bounds.
    private static float[] AreaReference(FloatImageBuffer source, int edge)
    {
        double ratio = Math.Min(1, edge / (double)Math.Max(source.Width, source.Height));
        int width = Math.Max(1, (int)Math.Round(source.Width * ratio));
        int height = Math.Max(1, (int)Math.Round(source.Height * ratio));
        if (width == source.Width && height == source.Height) return source.Pixels.ToArray();
        var output = new float[width * height * 4];
        double scaleX = source.Width / (double)width, scaleY = source.Height / (double)height;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            double red = 0, green = 0, blue = 0, coverage = 0;
            for (int sy = 0; sy < source.Height; sy++)
            for (int sx = 0; sx < source.Width; sx++)
            {
                double overlapY = Math.Max(0, Math.Min((y + 1) * scaleY, sy + 1) - Math.Max(y * scaleY, sy));
                double overlapX = Math.Max(0, Math.Min((x + 1) * scaleX, sx + 1) - Math.Max(x * scaleX, sx));
                double weight = overlapY * overlapX;
                int pixel = (sy * source.Width + sx) * 4;
                red += source.Pixels[pixel] * weight; green += source.Pixels[pixel + 1] * weight; blue += source.Pixels[pixel + 2] * weight;
                coverage += weight;
            }
            int target = (y * width + x) * 4;
            output[target] = (float)(red / coverage); output[target + 1] = (float)(green / coverage); output[target + 2] = (float)(blue / coverage);
            output[target + 3] = (float)(coverage / (scaleX * scaleY));
        }
        return output;
    }

    private static void Measure(Func<FloatImageBuffer> create, List<double> times, List<long> allocations)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        using var result = create();
        times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { Console.WriteLine("PASS: " + message); return; }
        throw new Exception("FAIL: " + message);
    }
}
