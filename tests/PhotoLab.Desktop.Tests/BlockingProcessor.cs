using PhotoLab.Core;

/// <summary>A non-cancellable first render reproduces late completions deterministically.</summary>
sealed class BlockingProcessor : IImageProcessor, IDisposable
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim _release = new();
    private int _calls;
    private int _active;

    public Task Started => _started.Task;
    public void Release() => _release.Set();

    public PixelBuffer Process(PixelBuffer source, EditSettings edit, CancellationToken cancellation = default)
    {
        if (Interlocked.Increment(ref _active) > 1) throw new Exception("Concurrent CPU processing detected.");
        try
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                _started.TrySetResult();
                if (!_release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test render was not released.");
            }
            return source;
        }
        finally { Interlocked.Decrement(ref _active); }
    }

    public void Dispose() => _release.Dispose();
}
