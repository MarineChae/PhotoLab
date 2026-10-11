using System.Diagnostics;
using PhotoLab.Core;
using PhotoLab.Desktop;
using PhotoLab.Imaging;

/// <summary>Measures float/RGBA8 preview service under 60 continuous input requests.</summary>
static class PreviewBenchmark
{
    public static async Task RunAsync()
    {
        var bytes = new byte[600 * 400 * 4];
        for (int i = 0; i < bytes.Length; i += 4)
        {
            bytes[i] = (byte)(i / 4 % 256);
            bytes[i + 1] = (byte)(i / 2400 % 256);
            bytes[i + 2] = 120;
            bytes[i + 3] = 255;
        }
        using var source = ImageConvert.FromSrgb8(new PixelBuffer(600, 400, bytes));
        using var renderer = new PreviewRenderer(new CpuImageProcessor());
        await renderer.RenderAsync(source, new EditSettings());
        var tasks = new List<Task>();
        var latencies = new List<double>();
        int publishedDuringInput = 0;
        bool moving = true;
        object gate = new();
        var duration = Stopwatch.StartNew();
        for (int i = 0; i < 60; i++)
        {
            tasks.Add(RenderAsync(i));
            await Task.Delay(16);
        }
        double inputSeconds = duration.Elapsed.TotalSeconds;
        lock (gate) moving = false;
        await Task.WhenAll(tasks);
        latencies.Sort();
        double p95 = latencies.Count == 0 ? double.NaN : latencies[(int)Math.Ceiling(latencies.Count * 0.95) - 1];
        Console.WriteLine($"BENCHMARK: 600x400 linear float + histogram + RGBA8, 60 requests, {inputSeconds:F3}s input, " +
            $"{publishedDuringInput} frames during input ({publishedDuringInput / inputSeconds:F1}/s), " +
            $"accepted request p95 {p95:F2}ms; service-only measurement, excludes UI Bitmap upload/presentation.");

        async Task RenderAsync(int index)
        {
            var latency = Stopwatch.StartNew();
            var frame = await renderer.RenderAsync(source, new EditSettings { Exposure = index / 60.0 });
            if (frame == null || !renderer.IsCurrent(frame.Version)) return;
            lock (gate)
            {
                if (moving) publishedDuringInput++;
                latencies.Add(latency.Elapsed.TotalMilliseconds);
            }
        }
    }
}
