using PhotoLab.Imaging.Decoding;

namespace PhotoLab.Infrastructure;

/// <summary>One-photo native session cache. Camera-default decoding preserves the existing decoder's exact semantics.</summary>
public sealed class SessionRawDecoder : IRawPreviewDeveloper, IAsyncDisposable
{
    private readonly IRawDeveloper _decoder;
    private readonly Func<string, CancellationToken, Task<IRawSession>> _openSession;
    private readonly SemaphoreSlim _work = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _disposeGate = new();
    private IRawSession? _session;
    private PhotoSourceStamp? _stamp;
    private RawMetadata? _metadata;
    private Task? _disposeTask;

    public SessionRawDecoder(IRawDeveloper decoder, Func<string, CancellationToken, Task<IRawSession>> openSession)
    { _decoder = decoder; _openSession = openSession; }

    public async Task<RawImage> DecodeAsync(string sourcePath, CancellationToken cancellation = default)
    {
        using var linked = Link(cancellation);
        await _work.WaitAsync(linked.Token);
        try
        {
            var stamp = PhotoSourceStamp.Read(sourcePath);
            if (_stamp != stamp) await ClearCoreAsync();
            var decoded = await _decoder.DecodeAsync(sourcePath, linked.Token);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                if (stamp != PhotoSourceStamp.Read(sourcePath)) throw new IOException("Photo changed while it was being decoded. Reopen it.");
                _stamp = stamp; _metadata = decoded.Metadata;
                return decoded;
            }
            catch { decoded.Dispose(); throw; }
        }
        finally { _work.Release(); }
    }

    public Task<RawImage> DevelopAsync(string sourcePath, CameraWhiteBalance whiteBalance, CancellationToken cancellation = default)
        => DevelopCoreAsync(sourcePath, whiteBalance, 0, cancellation);

    public Task<RawImage> DevelopPreviewAsync(string sourcePath, CameraWhiteBalance whiteBalance, int maxEdge,
        CancellationToken cancellation = default)
    {
        if (maxEdge <= 0) throw new ArgumentOutOfRangeException(nameof(maxEdge));
        return DevelopCoreAsync(sourcePath, whiteBalance, maxEdge, cancellation);
    }

    private async Task<RawImage> DevelopCoreAsync(string sourcePath, CameraWhiteBalance whiteBalance, int maxEdge,
        CancellationToken cancellation)
    {
        _ = whiteBalance.Normalize();
        using var linked = Link(cancellation);
        await _work.WaitAsync(linked.Token);
        try
        {
            var stamp = PhotoSourceStamp.Read(sourcePath);
            if (_stamp != stamp) await ClearCoreAsync();
            if (_metadata == null)
            {
                using var decoded = await _decoder.DecodeAsync(sourcePath, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                _metadata = decoded.Metadata; _stamp = stamp;
            }
            if (_session == null)
            {
                var opened = await _openSession(sourcePath, linked.Token);
                if (linked.IsCancellationRequested)
                { await opened.DisposeAsync(); linked.Token.ThrowIfCancellationRequested(); }
                _session = opened;
            }
            var pixels = maxEdge == 0 ? await _session.DevelopAsync(whiteBalance, linked.Token)
                : await _session.DevelopPreviewAsync(whiteBalance, maxEdge, linked.Token);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                if (stamp != PhotoSourceStamp.Read(sourcePath)) throw new IOException("Photo changed while it was being developed. Reopen it.");
                return new RawImage(pixels, _metadata with { AppliedWhiteBalance = whiteBalance });
            }
            catch { pixels.Dispose(); throw; }
        }
        catch
        {
            // Cancellation leaves a healthy native cache reusable; other errors can invalidate its rawdata.
            if (!linked.IsCancellationRequested) await ClearCoreAsync();
            throw;
        }
        finally { _work.Release(); }
    }

    public async Task ResetAsync(CancellationToken cancellation = default)
    {
        using var linked = Link(cancellation);
        await _work.WaitAsync(linked.Token);
        try { await ClearCoreAsync(); }
        finally { _work.Release(); }
    }

    private CancellationTokenSource Link(CancellationToken cancellation)
    {
        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(_disposeTask != null, this);
            return CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        }
    }

    private async Task ClearCoreAsync()
    {
        var session = _session;
        _session = null; _stamp = null; _metadata = null;
        if (session != null) await session.DisposeAsync();
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            if (_disposeTask == null)
            {
                _lifetime.Cancel();
                _disposeTask = Task.Run(async () =>
                {
                    await _work.WaitAsync();
                    try { await ClearCoreAsync(); _lifetime.Dispose(); }
                    finally { _work.Release(); }
                });
            }
            return new ValueTask(_disposeTask);
        }
    }
}
