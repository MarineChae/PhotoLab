using PhotoLab.Core;
using PhotoLab.Imaging;
using PhotoLab.Rendering;

namespace PhotoLab.Desktop;

public sealed record PreviewFrame(long Version, PixelBuffer Pixels)
{
    public ImageHistogram? Histogram { get; init; }
}

/// <summary>Serializes previews and discards superseded results, even when processing ignores cancellation.</summary>
public sealed class PreviewRenderer : IDisposable
{
    private readonly IImageProcessor _processor;
    private readonly IFloatImageProcessor _floatProcessor;
    private readonly SemaphoreSlim _worker = new(1, 1);
    private readonly object _gate = new();
    private CancellationTokenSource? _current;
    private long _version;
    private bool _disposed;

    /// <summary>The renderer owns the injected float processor and disposes it after all readers finish.</summary>
    public PreviewRenderer(IImageProcessor processor, IFloatImageProcessor? floatProcessor = null)
    {
        _processor = processor;
        string? library = Environment.GetEnvironmentVariable("PHOTOLAB_VULKAN_LIBRARY");
        _floatProcessor = floatProcessor ?? (string.IsNullOrWhiteSpace(library) ? new FloatImageProcessor()
            : new FallbackFloatProcessor(() => new VulkanFloatProcessor(library)));
    }

    public string BackendName => _floatProcessor.Name;

    public bool IsCurrent(long version)
    {
        lock (_gate) return !_disposed && version == _version;
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _version++;
            _current?.Cancel();
        }
    }

    public Task<PreviewFrame?> RenderAsync(PixelBuffer source, EditSettings settings)
        => RenderAsync(cancellation =>
        {
            var pixels = _processor.Process(source, settings, cancellation);
            cancellation.ThrowIfCancellationRequested();
            using var linear = ImageConvert.FromSrgb8(pixels);
            return (pixels, ImageHistogram.Compute(linear, cancellation));
        });

    public Task<PreviewFrame?> RenderAsync(FloatImageBuffer source, EditSettings settings)
        => RenderAsync(cancellation =>
        {
            using var pixels = _floatProcessor.Process(source, settings, cancellation);
            cancellation.ThrowIfCancellationRequested();
            var histogram = ImageHistogram.Compute(pixels, cancellation);
            return (ImageConvert.ToSrgb8(pixels), histogram);
        });

    /// <summary>Waits for readers before the caller releases the pooled source buffers.</summary>
    public async Task DrainAsync()
    {
        Invalidate();
        await _worker.WaitAsync();
        _worker.Release();
    }

    private async Task<PreviewFrame?> RenderAsync(Func<CancellationToken, (PixelBuffer Pixels, ImageHistogram Histogram)> render)
    {
        CancellationTokenSource cancellation;
        long version;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _current?.Cancel();
            _current = cancellation = new CancellationTokenSource();
            version = ++_version;
        }
        bool entered = false;
        try
        {
            await _worker.WaitAsync(cancellation.Token);
            entered = true;
            var pixels = await Task.Run(() => render(cancellation.Token), cancellation.Token);
            return IsCurrent(version) ? new PreviewFrame(version, pixels.Pixels) { Histogram = pixels.Histogram } : null;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return null; }
        catch (Exception) when (!IsCurrent(version)) { return null; } // Obsolete failures must not replace current UI status.
        finally
        {
            if (entered) _worker.Release();
            lock (_gate)
            {
                if (_current == cancellation) _current = null;
                cancellation.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _version++;
            _current?.Cancel();
        }
        _ = ReleaseProcessorAsync();
        // Pending tasks own their CTS and finish releasing the managed semaphore after cancellation.
    }

    private async Task ReleaseProcessorAsync()
    {
        await _worker.WaitAsync();
        try { await Task.Run(() => (_floatProcessor as IDisposable)?.Dispose()); }
        finally { _worker.Release(); }
    }
}
