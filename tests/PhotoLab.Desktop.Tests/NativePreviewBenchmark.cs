using System.Diagnostics;
using PhotoLab.Imaging;
using PhotoLab.Infrastructure;

/// <summary>Separates SDK processing, RGB16 conversion/resize and async overhead on an owned warm native session.</summary>
static class NativePreviewBenchmark
{
    public static async Task RunAsync(string bridge, string[] sources)
    {
        foreach (string source in sources)
        {
            using var decoded = await new LibRawDecoder().DecodeAsync(source);
            var wb = CameraWhiteBalanceAdjust.Apply(decoded.Metadata.CameraColor!.AsShot, 50, 20);
            await using var session = await NativeRawSession.OpenAsync(bridge, source);
            using (var warmup = await session.DevelopPreviewAsync(wb, 600)) { }
            var native = new List<double>(); var conversion = new List<double>(); var total = new List<double>(); var allocations = new List<long>();
            for (int i = 0; i < 10; i++)
            {
                long before = GC.GetTotalAllocatedBytes(precise: true);
                long start = Stopwatch.GetTimestamp();
                using var preview = await session.DevelopPreviewAsync(wb, 600);
                double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var timing = session.DevelopmentTiming;
                if (timing.Native <= TimeSpan.Zero || timing.Conversion <= TimeSpan.Zero ||
                    timing.Native.TotalMilliseconds + timing.Conversion.TotalMilliseconds > elapsed ||
                    preview.Width > 600 || preview.Height > 600)
                    throw new Exception("FAIL: invalid native profiling snapshot");
                native.Add(timing.Native.TotalMilliseconds); conversion.Add(timing.Conversion.TotalMilliseconds); total.Add(elapsed);
                allocations.Add(GC.GetTotalAllocatedBytes(precise: true) - before);
            }
            native.Sort(); conversion.Sort(); total.Sort(); allocations.Sort();
            Console.WriteLine("PASS: serialized preview timing snapshot matches successful development: " + Path.GetFileName(source));
            Console.WriteLine($"NATIVE_PREVIEW_STAGES: {Path.GetFileName(source)}, 10 warm same-WB requests, median SDK {native[5]:F3}ms, conversion/area {conversion[5]:F3}ms, " +
                $"awaited total {total[5]:F3}ms, process-wide managed allocation {allocations[5]} bytes; SDK includes native image creation, conversion includes validation/planning, " +
                "total includes task/gate/release overhead; excludes metadata/open/unpack, UI and native allocation footprint.");
        }
    }
}
