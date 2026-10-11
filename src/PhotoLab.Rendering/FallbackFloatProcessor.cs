using PhotoLab.Core;
using PhotoLab.Imaging;

namespace PhotoLab.Rendering;

/// <summary>Uses CPU for small interactive previews, lazily owns GPU quality processing, and selects CPU after driver failure.</summary>
public sealed class FallbackFloatProcessor : IFloatImageProcessor, IDisposable
{
    private const int CPU_PREVIEW_PIXELS = 600 * 600;
    private readonly FloatImageProcessor _cpu = new();
    private readonly Func<IFloatImageProcessor> _createGpu;
    private readonly object _gate = new();
    private IFloatImageProcessor? _gpu;
    private bool _failed, _disposed;
    private string _name = "CPU";

    public FallbackFloatProcessor(Func<IFloatImageProcessor> createGpu) => _createGpu = createGpu;
    public string Name => _name;
    /// <summary>Completion/failure of failed-device cleanup; CPU recovery never waits on this task.</summary>
    public Task RetiredGpuCleanup { get; private set; } = Task.CompletedTask;

    public FloatImageBuffer Process(FloatImageBuffer source, EditSettings edit, CancellationToken cancellation = default)
    {
        _ = FloatAdjustments.From(edit);
        _ = source.Pixels;
        cancellation.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Measured small interactive previews cost less on CPU than GPU fence/readback.
            if ((long)source.Width * source.Height <= CPU_PREVIEW_PIXELS)
            {
                _name = "CPU";
                return _cpu.Process(source, edit, cancellation);
            }
            if (!_failed)
            {
                try
                {
                    _gpu ??= _createGpu();
                    var output = _gpu.Process(source, edit, cancellation);
                    _name = _gpu.Name;
                    return output;
                }
                catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or NotSupportedException ||
                    error is InvalidOperationException and not ObjectDisposedException)
                {
                    var retired = _gpu;
                    _gpu = null; _failed = true;
                    // A timed-out submission can still need DeviceWaitIdle before safe destruction.
                    // The task owns the failed processor until cleanup completes; keep CPU recovery independent.
                    if (retired is IDisposable disposable) RetiredGpuCleanup = Task.Run(disposable.Dispose);
                }
            }
            _name = "CPU";
            return _cpu.Process(source, edit, cancellation);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            (_gpu as IDisposable)?.Dispose(); _gpu = null;
        }
    }
}
