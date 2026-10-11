using PhotoLab.Desktop;
using PhotoLab.Imaging;
using PhotoLab.Imaging.Decoding;
using PhotoLab.Infrastructure;

/// <summary>Cache ownership and real-RAW app-loader comparisons, independent of GUI drawing.</summary>
public static class CachedLoaderTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    public static async Task RunAsync(string bridge, string[] sources)
    {
        await OwnershipAsync();
        foreach (string source in sources)
        {
            int opened = 0;
            var cached = new SessionRawDecoder(new LibRawDecoder(), async (path, token) =>
            { opened++; return await NativeRawSession.OpenAsync(bridge, path, token); });
            await using var loader = new PhotoImageLoader(cached);
            var original = await loader.LoadPreviewWithMetadataAsync(source);
            using (original.preview) Check(original.metadata?.CameraColor != null, "cached loader preserves original camera metadata: " + Path.GetFileName(source));
            var wb = CameraWhiteBalanceAdjust.Apply(original.metadata!.CameraColor!.AsShot, 50, 20);
            using var baseline = await new LibRawDecoder().DevelopAsync(source, wb);
            using (var full = await loader.LoadLinearFullAsync(source, wb))
                Check(full.Pixels.SequenceEqual(baseline.Pixels.Pixels), "cached app full/export pixels match CLI development: " + Path.GetFileName(source));
            var preview = await loader.LoadPreviewWithMetadataAsync(source, wb);
            using (preview.preview)
            using (var expected = ImageResize.Preview(baseline.Pixels, 1800))
                Check(preview.preview.Pixels.SequenceEqual(expected.Pixels) && preview.metadata?.AppliedWhiteBalance == wb,
                    "cached app quality preview matches full development and WB metadata: " + Path.GetFileName(source));
            var fast = await loader.LoadInteractivePreviewAsync(source, wb);
            using (fast.preview)
            using (var halfReference = await NativeSessionTests.HalfReferenceAsync(source, wb))
            using (var expectedFast = ImageResize.Preview(halfReference, 600))
                Check(fast.preview.Pixels.SequenceEqual(expectedFast.Pixels) && fast.originalWidth == baseline.Pixels.Width &&
                    fast.originalHeight == baseline.Pixels.Height && fast.metadata?.AppliedWhiteBalance == wb,
                    "interactive loader preserves full dimensions and matches native half-size reference: " + Path.GetFileName(source));
            Check(opened == 1, "preview/export share one unpack session: " + Path.GetFileName(source));
            await loader.ResetAsync();
            using (var reopened = await loader.LoadLinearFullAsync(source, wb))
                Check(opened == 2 && reopened.Pixels.SequenceEqual(baseline.Pixels.Pixels), "explicit reopen replaces unpack session without changing pixels: " + Path.GetFileName(source));
        }
        Console.WriteLine("All cached loader tests passed.");
    }

    private static async Task OwnershipAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "photolab-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "fixture.NEF");
            await File.WriteAllTextAsync(source, "Storage fixture; fake decoder only.");
            int decoded = 0;
            var camera = new CountingDecoder(() => decoded++);
            var sessions = new List<FakeSession>();
            var cache = new SessionRawDecoder(camera, (_, _) =>
            {
                var session = new FakeSession(); sessions.Add(session);
                return Task.FromResult<IRawSession>(session);
            });
            await using var loader = new PhotoImageLoader(cache);
            var wb = new CameraWhiteBalance(2, 1, 1.5f, 0);
            using (var full = await loader.LoadLinearFullAsync(source, wb)) { }
            using (var full = await loader.LoadLinearFullAsync(source, wb)) { }
            Check(decoded == 1 && sessions.Count == 1 && sessions[0].Developments == 2, "repeated WB reuses metadata and native session");
            await File.AppendAllTextAsync(source, "Changed length.");
            using (var full = await loader.LoadLinearFullAsync(source, wb)) { }
            Check(decoded == 2 && sessions.Count == 2 && sessions[0].Disposals == 1, "external source edit invalidates and releases the session");
            await loader.ResetAsync();
            Check(sessions[1].Disposals == 1, "explicit reopen releases cached native resources");

            var pending = new TaskCompletionSource<IRawSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var lateCache = new SessionRawDecoder(camera, (_, _) => pending.Task);
            using var cancel = new CancellationTokenSource();
            var lateLoad = lateCache.DevelopAsync(source, wb, cancel.Token);
            cancel.Cancel();
            var lateSession = new FakeSession(); pending.SetResult(lateSession);
            try { using var unexpected = await lateLoad; throw new Exception("FAIL: late session accepted"); }
            catch (OperationCanceledException) { Console.WriteLine("PASS: cancelled session opening rejects late completion"); }
            Check(lateSession.Disposals == 1, "cancelled late session is released before becoming cache owner");

            var completion = new TaskCompletionSource<FloatImageBuffer>(TaskCreationOptions.RunContinuationsAsynchronously);
            var blocked = new FakeSession { Pending = completion.Task };
            var closingCache = new SessionRawDecoder(camera, (_, _) => Task.FromResult<IRawSession>(blocked));
            var closingLoad = closingCache.DevelopAsync(source, wb);
            var disposal = closingCache.DisposeAsync().AsTask();
            var latePixels = FloatImageBuffer.FromLinearRgba32(1, 1, [0.25f, 0.5f, 0.75f, 1]);
            completion.SetResult(latePixels);
            try { using var unexpected = await closingLoad; throw new Exception("FAIL: closed cache returned pixels"); }
            catch (OperationCanceledException) { Console.WriteLine("PASS: closing cache rejects non-cooperative late pixels"); }
            await disposal;
            Check(blocked.Disposals == 1, "cache disposal joins active work and releases the session once");
            try { _ = latePixels.Pixels.Length; throw new Exception("FAIL: late cache pixels leaked"); }
            catch (ObjectDisposedException) { Console.WriteLine("PASS: closed cache releases late pixel buffers"); }
            await closingCache.DisposeAsync();
            await loader.DisposeAsync();
            try { await loader.LoadLinearFullAsync(source, wb); throw new Exception("FAIL: disposed cache accepted a load"); }
            catch (ObjectDisposedException) { Console.WriteLine("PASS: disposed app cache rejects new loads"); }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class CountingDecoder : IRawDeveloper
    {
        private readonly Action _decoded;
        public CountingDecoder(Action decoded) => _decoded = decoded;
        public Task<RawImage> DecodeAsync(string path, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested(); _decoded();
            return Task.FromResult(new RawImage(FloatImageBuffer.FromLinearRgba32(1, 1, [0.25f, 0.5f, 0.75f, 1]), new RawMetadata(1, 1, ".NEF")));
        }
        public Task<RawImage> DevelopAsync(string path, CameraWhiteBalance wb, CancellationToken cancellation = default)
            => throw new Exception("Custom WB must use the cached session.");
    }

    private sealed class FakeSession : IRawSession
    {
        public int Developments { get; private set; }
        public int Disposals { get; private set; }
        public Task<FloatImageBuffer>? Pending { get; init; }
        public Task<FloatImageBuffer> DevelopAsync(CameraWhiteBalance wb, CancellationToken cancellation = default)
        {
            Developments++;
            return Pending ?? Task.FromResult(FloatImageBuffer.FromLinearRgba32(1, 1, [0.25f, 0.5f, 0.75f, 1]));
        }
        public Task<FloatImageBuffer> DevelopPreviewAsync(CameraWhiteBalance wb, int maxEdge, CancellationToken cancellation = default)
            => DevelopAsync(wb, cancellation);
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
