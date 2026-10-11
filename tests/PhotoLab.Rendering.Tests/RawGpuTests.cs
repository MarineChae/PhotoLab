using System.Diagnostics;
using PhotoLab.Core;
using PhotoLab.Imaging;
using PhotoLab.Infrastructure;
using PhotoLab.Rendering;

/// <summary>Actual RAW control parity and paired CPU/GPU service costs with identical decoded inputs.</summary>
static class RawGpuTests
{
    public static async Task RunAsync(string library, string[] sources)
    {
        var cpu = new FloatImageProcessor();
        using var gpu = new VulkanFloatProcessor(library);
        var edits = new EditSettings[] { new(), new() { Exposure = 1.5 },
            new() { Exposure = -0.7, Contrast = 35, Highlights = -40, Shadows = 30, Temperature = 25, Tint = -15, Saturation = 10, Vibrance = 20 },
            new() { Exposure = 2, Contrast = -50, Highlights = 60, Shadows = -50, Temperature = -80, Tint = 70, Saturation = -70, Vibrance = 100 } };
        foreach (string source in sources)
        {
            using var raw = await new LibRawDecoder().DecodeAsync(source);
            using var preview = ImageResize.Preview(raw.Pixels, 1800);
            foreach (var edit in edits)
            {
                using var expected = cpu.Process(preview, edit);
                using var actual = gpu.Process(preview, edit);
                double maxError = 0;
                for (int i = 0; i < actual.Pixels.Length; i++)
                {
                    float a = actual.Pixels[i], e = expected.Pixels[i];
                    maxError = Math.Max(maxError, Math.Abs(a-e));
                    if (!float.IsFinite(a) || Math.Abs(a-e) > 2e-5 * Math.Max(1,Math.Abs(e)) || (i % 4 == 3 && a != e))
                        throw new Exception($"FAIL: RAW GPU parity {Path.GetFileName(source)} sample {i}: {a} vs {e}");
                }
                var expectedDisplay = ImageConvert.ToSrgb8(expected);
                var actualDisplay = ImageConvert.ToSrgb8(actual);
                int maxDelta = 0, changed = 0;
                for (int i = 0; i < actualDisplay.Data.Length; i++)
                {
                    int delta = Math.Abs(actualDisplay.Data[i]-expectedDisplay.Data[i]);
                    maxDelta = Math.Max(delta,maxDelta); if (delta != 0) changed++;
                }
                if (maxDelta > 1) throw new Exception("FAIL: RAW GPU display difference exceeds one code value");
                Console.WriteLine($"PASS: RAW GPU controls {Path.GetFileName(source)}, max float error {maxError:G6}, max display delta {maxDelta}, {changed} changed channel bytes");
            }
            if (Path.GetExtension(source).Equals(".dng", StringComparison.OrdinalIgnoreCase))
            {
                using var small = ImageResize.Preview(raw.Pixels, 600);
                Benchmark(cpu,gpu,small,edits[2]); Benchmark(cpu,gpu,preview,edits[2]);
            }
        }
    }

    private static void Benchmark(FloatImageProcessor cpu, VulkanFloatProcessor gpu, FloatImageBuffer source, EditSettings edit)
    {
        using (var warm = cpu.Process(source, edit)) { }
        using (var warm = gpu.Process(source, edit)) { }
        var cpuTimes = new List<double>(); var gpuTimes = new List<double>();
        for (int i = 0; i < 20; i++)
        {
            if (i % 2 == 0) { Measure(cpu,cpuTimes); Measure(gpu,gpuTimes); }
            else { Measure(gpu,gpuTimes); Measure(cpu,cpuTimes); }
        }
        cpuTimes.Sort(); gpuTimes.Sort();
        Console.WriteLine($"RAW_GPU_PAIRED: {source.Width}x{source.Height}, 20 alternating same-source combined-control pairs, " +
            $"CPU median/p95 {cpuTimes[10]:F3}/{cpuTimes[18]:F3}ms, GPU median/p95 {gpuTimes[10]:F3}/{gpuTimes[18]:F3}ms; " +
            $"GPU includes fence/readback/owned float, upload cached, logical capacity {gpu.BufferCapacityBytes} bytes; RAW decoding/resize/display excluded.");
        void Measure(IFloatImageProcessor processor, List<double> times)
        {
            long start = Stopwatch.GetTimestamp(); using var output = processor.Process(source,edit);
            times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }
}
