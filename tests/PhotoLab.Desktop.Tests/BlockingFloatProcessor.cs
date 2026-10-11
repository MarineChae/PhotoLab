using PhotoLab.Core;
using PhotoLab.Imaging;

/// <summary>Controlled non-cooperative work verifies renderer-owned GPU lifetime ordering.</summary>
sealed class BlockingFloatProcessor : IFloatImageProcessor, IDisposable
{
    private readonly ManualResetEventSlim _release = new(false);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Name => "Controlled";
    public Task Started => _started.Task;
    public Task Disposed => _disposed.Task;
    public void Release() => _release.Set();
    public FloatImageBuffer Process(FloatImageBuffer source, EditSettings edit, CancellationToken cancellation = default)
    {
        _started.SetResult(); _release.Wait();
        return new FloatImageProcessor().Process(source, edit);
    }
    public void Dispose() { _release.Dispose(); _disposed.TrySetResult(); }
}
