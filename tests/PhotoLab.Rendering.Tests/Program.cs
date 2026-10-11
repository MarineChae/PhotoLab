using System.Diagnostics;
using PhotoLab.Core;
using PhotoLab.Imaging;
using PhotoLab.Rendering;

static void Check(bool value, string message)
{
    if (!value) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

var cpu = new FloatImageProcessor();
if (args.Length > 0 && args[0] == "--raw")
{
    await RawGpuTests.RunAsync(args[1],args.Skip(2).ToArray());
    return;
}
using var small = FloatImageBuffer.FromLinearRgba32(2, 1, [-0.2f, 0.4f, 2, 0, 0.1f, 0.8f, 0.5f, 0.75f]);
using var fallbackSource = FloatImageBuffer.FromLinearRgba32(601,600,new float[601 * 600 * 4]);
using (var interactive = new FallbackFloatProcessor(() => throw new Exception("Small preview unexpectedly requested GPU")))
using (var frame = interactive.Process(small,new EditSettings()))
    Check(frame.Pixels.SequenceEqual(small.Pixels) && interactive.Name == "CPU", "small interactive preview selects CPU without initializing GPU");
int attempts = 0;
using (var fallback = new FallbackFloatProcessor(() => { attempts++; throw new DllNotFoundException("Controlled missing driver"); }))
{
    using var actual = fallback.Process(fallbackSource, new EditSettings { Exposure = 1 });
    using var expected = cpu.Process(fallbackSource, new EditSettings { Exposure = 1 });
    Check(actual.Pixels.SequenceEqual(expected.Pixels) && fallback.Name == "CPU", "missing GPU initializes a faithful CPU fallback");
    using var again = fallback.Process(fallbackSource, new EditSettings());
    Check(attempts == 1, "failed GPU initialization is not repeated for each frame");
}
var failing = new FailingGpu();
using (var fallback = new FallbackFloatProcessor(() => new CleanupFailureGpu()))
using (var recovered = fallback.Process(fallbackSource,new EditSettings()))
{
    try { await fallback.RetiredGpuCleanup; throw new Exception("Cleanup failure was hidden"); }
    catch (InvalidOperationException)
    {
        Check(recovered.Pixels.SequenceEqual(fallbackSource.Pixels) && fallback.RetiredGpuCleanup.IsFaulted,
            "background cleanup failure is observable without discarding the recovered CPU frame");
    }
}
var slowCleanup = new SlowCleanupGpu();
using (var fallback = new FallbackFloatProcessor(() => slowCleanup))
{
    var work = Task.Run(() => fallback.Process(fallbackSource,new EditSettings()));
    await slowCleanup.CleanupStarted.WaitAsync(TimeSpan.FromSeconds(5));
    await Task.Delay(100);
    bool returnedBeforeCleanup = work.IsCompleted;
    if (returnedBeforeCleanup) fallback.Dispose();
    bool retainedOnClose = !fallback.RetiredGpuCleanup.IsCompleted;
    slowCleanup.Release();
    using var recovered = await work.WaitAsync(TimeSpan.FromSeconds(5));
    Check(returnedBeforeCleanup && recovered.Pixels.SequenceEqual(fallbackSource.Pixels),
        "CPU fallback returns while failed GPU cleanup is blocked waiting for retirement");
    Check(retainedOnClose, "fallback close retains the failed device until its cleanup can safely complete");
    await fallback.RetiredGpuCleanup.WaitAsync(TimeSpan.FromSeconds(5));
    Check(fallback.RetiredGpuCleanup.IsCompletedSuccessfully, "failed GPU cleanup remains observable and finishes after retirement");
}
using (var fallback = new FallbackFloatProcessor(() => failing))
{
    using var frame = fallback.Process(fallbackSource, new EditSettings());
    await fallback.RetiredGpuCleanup.WaitAsync(TimeSpan.FromSeconds(5));
    Check(frame.Pixels.SequenceEqual(fallbackSource.Pixels) && failing.Disposed, "GPU execution failure releases the device and returns CPU output");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { fallback.Process(small, new EditSettings(), cancelled.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: fallback honors cancellation before processing"); }
}

if (args.Length == 0) return;
using var gpu = new VulkanFloatProcessor(args[0]);
Console.WriteLine("DEVICE: " + gpu.DeviceName);
using (var adaptive = new FallbackFloatProcessor(() => new VulkanFloatProcessor(args[0])))
{
    using var quality = adaptive.Process(fallbackSource,new EditSettings());
    Check(adaptive.Name == "Vulkan", "quality preview selects the actual Vulkan processor");
    using var interactive = adaptive.Process(small,new EditSettings());
    Check(adaptive.Name == "CPU", "interactive preview reports CPU after a GPU quality frame");
    using var resumed = adaptive.Process(fallbackSource,new EditSettings());
    Check(adaptive.Name == "Vulkan" && resumed.Pixels.SequenceEqual(fallbackSource.Pixels), "quality processing resumes after an interactive CPU frame");
}
var random = new Random(7721);
var samples = new float[257 * 193 * 4];
for (int i = 0; i < samples.Length; i += 4)
{
    samples[i] = (float)random.NextDouble() * 4 - 0.5f;
    samples[i + 1] = (float)random.NextDouble() * 2;
    samples[i + 2] = (float)random.NextDouble() * 3;
    samples[i + 3] = (i / 4 % 5) / 4f;
}
using var source = FloatImageBuffer.FromLinearRgba32(257, 193, samples);
var edits = new List<EditSettings> { new() };
foreach (int value in new[] { -100, 100 })
{
    edits.Add(new() { Exposure = value / 25.0 }); edits.Add(new() { Contrast = value });
    edits.Add(new() { Highlights = value }); edits.Add(new() { Shadows = value });
    edits.Add(new() { Temperature = value }); edits.Add(new() { Tint = value });
    edits.Add(new() { Saturation = value }); edits.Add(new() { Vibrance = value });
}
for (int i = 0; i < 10; i++) edits.Add(new() { Exposure = random.NextDouble() * 8 - 4, Contrast = random.Next(-100,101),
    Highlights = random.Next(-100,101), Shadows = random.Next(-100,101), Temperature = random.Next(-100,101), Tint = random.Next(-100,101),
    Saturation = random.Next(-100,101), Vibrance = random.Next(-100,101) });
foreach (var edit in edits)
{
    using var expected = cpu.Process(source, edit);
    using var actual = gpu.Process(source, edit);
    double maxError = 0;
    for (int i = 0; i < samples.Length; i++)
    {
        float a = actual.Pixels[i], e = expected.Pixels[i];
        maxError = Math.Max(maxError, Math.Abs(a-e));
        if (i % 4 == 3 && a != e) throw new Exception("Alpha changed");
        if (Math.Abs(a-e) > 2e-5 * Math.Max(1, Math.Abs(e))) throw new Exception($"GPU mismatch {i}: {a} vs {e} for {edit}");
    }
    Check(edit != new EditSettings() || actual.Pixels.SequenceEqual(source.Pixels), "GPU control parity, maximum float error " + maxError.ToString("G6"));
}
Check(gpu.BufferAllocationCount == 2 && gpu.SourceUploadCount == 1, "repeated controls reuse two buffers and the immutable source upload");
Check(source.Pixels.SequenceEqual(samples), "GPU processing never modifies the CPU source");
using (var smallResult = gpu.Process(small, new EditSettings()))
    Check(gpu.BufferAllocationCount == 2 && gpu.SourceUploadCount == 2 && smallResult.Pixels.SequenceEqual(small.Pixels), "smaller replacement reuses capacity and preserves hidden RGB");
using (var restored = gpu.Process(source, new EditSettings()))
    Check(gpu.BufferAllocationCount == 2 && gpu.SourceUploadCount == 3 && restored.Pixels.SequenceEqual(source.Pixels), "A/B/A sources reupload independently without reallocating buffers");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel(); int uploads = gpu.SourceUploadCount;
    try { gpu.Process(small, new EditSettings(), cancelled.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { Check(gpu.SourceUploadCount == uploads, "pre-cancellation never uploads or dispatches GPU work"); }
}
int beforeInvalid = gpu.SourceUploadCount;
try { gpu.Process(small,new EditSettings { Exposure = double.NaN }); throw new Exception("Invalid controls accepted"); }
catch (ArgumentException) { Check(gpu.SourceUploadCount == beforeInvalid, "invalid controls fail before GPU source upload"); }
using (var overflow = FloatImageBuffer.FromLinearRgba32(1,1,[float.MaxValue,float.MaxValue,float.MaxValue,1]))
{
    try { gpu.Process(overflow,new EditSettings { Exposure = 4 }); throw new Exception("GPU overflow accepted"); }
    catch (ArithmeticException) { Console.WriteLine("PASS: GPU rejects non-finite processing output like CPU"); }
}
var stopwatch = Stopwatch.StartNew();
for (int i = 0; i < 60; i++) { using var frame = gpu.Process(source, edits[17 + i % 10]); }
Console.WriteLine($"GPU_REUSE_BENCHMARK: {source.Width}x{source.Height}, 60 controls, {stopwatch.Elapsed.TotalMilliseconds:F3}ms; includes fence/readback/owned float allocation, excludes display.");
var largeSamples = new float[1800 * 1200 * 4];
for (int i = 0; i < largeSamples.Length; i += 4)
{ largeSamples[i] = 0.18f; largeSamples[i+1] = 0.4f; largeSamples[i+2] = 1.3f; largeSamples[i+3] = 1; }
using var large = FloatImageBuffer.FromLinearRgba32(1800, 1200, largeSamples);
var largeEdit = new EditSettings { Exposure = 0.5, Contrast = 30, Highlights = -50, Shadows = 20, Temperature = 10, Tint = -20, Saturation = 15, Vibrance = 25 };
using (var expected = cpu.Process(large, largeEdit))
using (var actual = gpu.Process(large, largeEdit))
{
    for (int i = 0; i < actual.Pixels.Length; i++)
        if (Math.Abs(actual.Pixels[i]-expected.Pixels[i]) > 2e-5 * Math.Max(1, Math.Abs(expected.Pixels[i]))) throw new Exception("Large preview mismatch");
    Check(gpu.BufferAllocationCount == 4 && gpu.BufferCapacityBytes == (ulong)largeSamples.Length * 8, "large preview grows buffer capacity once and preserves CPU parity");
}
using (var retired = gpu.Process(source, new EditSettings()))
    Check(gpu.BufferAllocationCount == 4 && retired.Pixels.SequenceEqual(samples), "smaller image after growth reuses buffers without stale tail pixels");
var wideSamples = new float[2100 * 2100 * 4];
for (int i = 0; i < wideSamples.Length; i += 4)
{ wideSamples[i] = -0.25f; wideSamples[i+1] = 0.5f; wideSamples[i+2] = 2; wideSamples[i+3] = 0.75f; }
using (var wide = FloatImageBuffer.FromLinearRgba32(2100, 2100, wideSamples))
using (var wideResult = gpu.Process(wide, new EditSettings()))
    Check(wideResult.Pixels.SequenceEqual(wideSamples) && gpu.BufferAllocationCount == 6,
        "4.41MP neutral image preserves all samples across two-dimensional dispatch");
gpu.Dispose();
try { gpu.Process(source, new EditSettings()); throw new Exception("Disposed GPU accepted work"); }
catch (ObjectDisposedException) { Console.WriteLine("PASS: disposed GPU rejects processing and repeated disposal is safe"); }

sealed class FailingGpu : IFloatImageProcessor, IDisposable
{
    public bool Disposed { get; private set; }
    public string Name => "Controlled GPU";
    public FloatImageBuffer Process(FloatImageBuffer source, EditSettings edit, CancellationToken cancellation = default)
        => throw new InvalidOperationException("Controlled device failure");
    public void Dispose() => Disposed = true;
}

sealed class SlowCleanupGpu : IFloatImageProcessor, IDisposable
{
    private readonly ManualResetEventSlim _release = new(false);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task CleanupStarted => _started.Task;
    public string Name => "Controlled stalled GPU";
    public void Release() => _release.Set();
    public FloatImageBuffer Process(FloatImageBuffer source, EditSettings edit, CancellationToken cancellation = default)
        => throw new InvalidOperationException("Controlled fence timeout");
    public void Dispose() { _started.SetResult(); _release.Wait(); _release.Dispose(); }
}

sealed class CleanupFailureGpu : IFloatImageProcessor, IDisposable
{
    public string Name => "Controlled cleanup failure";
    public FloatImageBuffer Process(FloatImageBuffer source, EditSettings edit, CancellationToken cancellation = default)
        => throw new InvalidOperationException("Controlled device loss");
    public void Dispose() => throw new InvalidOperationException("Controlled cleanup failure");
}
