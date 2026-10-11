using System.Runtime.InteropServices;
using System.Diagnostics;
using PhotoLab.Core;
using PhotoLab.Imaging;
using PhotoLab.Imaging.Decoding;

namespace PhotoLab.Infrastructure;

/// <summary>
/// Explicit native bridge backend. Keeps unpacked sensor data and serializes repeated custom-WB development.
/// This is not the default app decoder; the caller owns the session and must await disposal.
/// </summary>
public sealed class NativeRawSession : IRawSession
{
    private readonly object _gate = new();
    private readonly object _lifetimeGate = new();
    private (TimeSpan Native, TimeSpan Conversion) _developmentTiming;

    private readonly CancellationTokenSource _lifetime = new();
    private readonly Bridge _bridge;
    private readonly SessionHandle _handle;
    private Task? _disposeTask;
    private bool _faulted;

    public string SourcePath { get; }
    public string LibRawVersion { get; }

    /// <summary>Last successful development's SDK and conversion durations; excludes gate wait, open and image release.</summary>
    public (TimeSpan Native, TimeSpan Conversion) DevelopmentTiming
    {
        get { lock (_gate) return _developmentTiming; }
    }

    private NativeRawSession(Bridge bridge, IntPtr context, string sourcePath)
    {
        _bridge = bridge;
        SourcePath = sourcePath;
        LibRawVersion = bridge.Version;
        _handle = new SessionHandle(bridge, context);
    }

    public static Task<NativeRawSession> OpenAsync(string bridgePath, string sourcePath, CancellationToken cancellation = default)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        if (!PhotoFormats.IsRaw(sourcePath)) throw new ArgumentException("A RAW session requires a RAW extension.", nameof(sourcePath));
        return Task.Run(() => Open(bridgePath, sourcePath, cancellation), cancellation);
    }

    private static NativeRawSession Open(string bridgePath, string sourcePath, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("RAW source not found.", sourcePath);
        var bridge = new Bridge(Path.GetFullPath(bridgePath));
        IntPtr context = IntPtr.Zero;
        CancelCheck check = () => cancellation.IsCancellationRequested ? 1 : 0;
        try
        {
            context = bridge.Open(sourcePath, check, out int status);
            cancellation.ThrowIfCancellationRequested();
            if (context == IntPtr.Zero || status != 0) throw bridge.Failure("open/unpack", status);
            return new NativeRawSession(bridge, context, sourcePath);
        }
        catch
        {
            if (context != IntPtr.Zero) bridge.Close(context);
            bridge.Release();
            throw;
        }
        finally { GC.KeepAlive(check); }
    }

    public Task<FloatImageBuffer> DevelopAsync(CameraWhiteBalance whiteBalance, CancellationToken cancellation = default)
    {
        var normalized = whiteBalance.Normalize();
        return Task.Run(() => Develop(normalized, halfSize: false, cancellation), cancellation);
    }

    public Task<FloatImageBuffer> DevelopPreviewAsync(CameraWhiteBalance whiteBalance, int maxEdge,
        CancellationToken cancellation = default)
    {
        if (maxEdge <= 0) throw new ArgumentOutOfRangeException(nameof(maxEdge));
        var normalized = whiteBalance.Normalize();
        return Task.Run(() => Develop(normalized, halfSize: true, cancellation, maxEdge), cancellation);
    }

    private FloatImageBuffer Develop(CameraWhiteBalance wb, bool halfSize, CancellationToken cancellation, int maxEdge = 0)
    {
        lock (_gate)
        {
            lock (_lifetimeGate)
                ObjectDisposedException.ThrowIf(_disposeTask != null || _lifetime.IsCancellationRequested, this);
            if (_faulted) throw new InvalidOperationException("The RAW session failed or was cancelled; reopen the source.");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
            var token = linked.Token;
            var lifetimeToken = _lifetime.Token;
            token.ThrowIfCancellationRequested();
            // Superseded WB requests discard output after native processing: LibRaw cancellation can recycle rawdata.
            // Only owner disposal interrupts native work; then the cache will not be reused anyway.
            CancelCheck check = () => lifetimeToken.IsCancellationRequested ? 1 : 0;
            IntPtr image = IntPtr.Zero;
            try
            {
                long nativeStart = Stopwatch.GetTimestamp();
                int status = _bridge.Develop(_handle.DangerousGetHandle(), wb.Channel0, wb.Channel1, wb.Channel2, wb.Channel3,
                    halfSize ? 1 : 0, check, out image, out IntPtr data, out int width, out int height, out uint size);
                long conversionStart = Stopwatch.GetTimestamp();
                if (status != 0) _faulted = true; // Fatal LibRaw processing errors can recycle its rawdata.
                token.ThrowIfCancellationRequested();
                if (status != 0) throw _bridge.Failure("development", status);
                int samples = checked(width * height * 3);
                if (width <= 0 || height <= 0 || image == IntPtr.Zero || data == IntPtr.Zero || size != checked((uint)samples * 2))
                    throw new InvalidDataException("Native RAW output dimensions do not match its RGB16 payload.");
                unsafe
                {
                    // Borrow only within the serialized native-image lifetime. Both converters return owned float data;
                    // ClearImage runs afterward, including cancellation/failure. No native span escapes this scope.
                    var input = new ReadOnlySpan<ushort>((void*)data, samples);
                    var pixels = maxEdge == 0 ? FloatImageBuffer.FromLinearRgb16(width, height, input, token)
                        : ImageResize.PreviewRgb16(width, height, input, maxEdge, token);
                    _developmentTiming = (Stopwatch.GetElapsedTime(nativeStart, conversionStart), Stopwatch.GetElapsedTime(conversionStart));
                    return pixels;
                }
            }
            finally
            {
                if (image != IntPtr.Zero) _bridge.ClearImage(image);
                GC.KeepAlive(check); GC.KeepAlive(_handle);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifetimeGate)
        {
            if (_disposeTask == null)
            {
                _lifetime.Cancel();
                _disposeTask = Task.Run(() =>
                {
                    lock (_gate) { _handle.Dispose(); _lifetime.Dispose(); }
                });
            }
            return new ValueTask(_disposeTask);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CancelCheck();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ApiVersion();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr VersionText();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ErrorText(int status);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr OpenRaw(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string source, CancelCheck cancel, out int status);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DevelopRaw(IntPtr context,
        float c0, float c1, float c2, float c3, int halfSize, CancelCheck cancel, out IntPtr image, out IntPtr data,
        out int width, out int height, out uint size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ReleaseRaw(IntPtr value);

    private sealed class Bridge
    {
        private readonly IntPtr _library;
        private readonly ErrorText _error;
        public OpenRaw Open { get; }
        public DevelopRaw Develop { get; }
        public ReleaseRaw Close { get; }
        public ReleaseRaw ClearImage { get; }
        public string Version { get; }

        public Bridge(string path)
        {
            _library = NativeLibrary.Load(path);
            try
            {
                if (Get<ApiVersion>("ph_raw_api_version")() != 1) throw new InvalidDataException("Unsupported native RAW bridge ABI.");
                Open = Get<OpenRaw>("ph_raw_open"); Develop = Get<DevelopRaw>("ph_raw_develop");
                Close = Get<ReleaseRaw>("ph_raw_close"); ClearImage = Get<ReleaseRaw>("ph_raw_clear_image");
                _error = Get<ErrorText>("ph_raw_error");
                Version = Marshal.PtrToStringUTF8(Get<VersionText>("ph_raw_version")()) ?? "Unknown";
            }
            catch { NativeLibrary.Free(_library); throw; }
        }
        private T Get<T>(string symbol) where T : Delegate
            => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, symbol));
        public Exception Failure(string operation, int status)
            => new InvalidDataException($"Native RAW {operation} failed ({status}): {Marshal.PtrToStringUTF8(_error(status))}");
        public void Release() => NativeLibrary.Free(_library);
    }

    private sealed class SessionHandle : SafeHandle
    {
        private readonly Bridge _bridge;
        public override bool IsInvalid => handle == IntPtr.Zero;
        public SessionHandle(Bridge bridge, IntPtr context) : base(IntPtr.Zero, ownsHandle: true)
        { _bridge = bridge; SetHandle(context); }
        protected override bool ReleaseHandle()
        {
            // Keep the module loaded until its destroy function has released all LibRaw allocations.
            _bridge.Close(handle); _bridge.Release();
            return true;
        }
    }
}
