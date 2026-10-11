using PhotoLab.Core;
using PhotoLab.Imaging;
using PhotoLab.Imaging.Decoding;
using PhotoLab.Infrastructure;

namespace PhotoLab.Desktop;

/// <summary>
/// The UI/export share one file-type independent loading entry point.
/// RAW decoding is currently performed by LibRaw; standard images use SkiaSharp.
/// </summary>
public sealed class PhotoImageLoader : IAsyncDisposable
{
    private readonly IRawDecoder _raw;
    private readonly LibRawCommandDecoder? _legacyRaw;

    public PhotoImageLoader() : this(CreateDecoder()) { _legacyRaw = new LibRawCommandDecoder(); }

    public PhotoImageLoader(IRawDecoder raw) => _raw = raw;
    public bool SupportsRawPreview => _raw is IRawPreviewDeveloper;

    public async Task<(FloatImageBuffer preview, int originalWidth, int originalHeight, RawMetadata? metadata)> LoadInteractivePreviewAsync(
        string path, CameraWhiteBalance whiteBalance, CancellationToken cancellation = default)
    {
        if (!PhotoFormats.IsRaw(path)) throw new NotSupportedException("Camera WB requires a RAW image.");
        _ = whiteBalance.Normalize();
        if (_raw is IRawPreviewDeveloper previewDecoder)
        {
            using var decoded = await previewDecoder.DevelopPreviewAsync(path, whiteBalance, 600, cancellation);
            cancellation.ThrowIfCancellationRequested();
            return (decoded.TakePixels(), decoded.Metadata.Width, decoded.Metadata.Height, decoded.Metadata);
        }
        var loaded = await LoadPreviewWithMetadataAsync(path, whiteBalance, cancellation);
        using (loaded.preview)
        {
            var small = await Task.Run(() => ImageResize.Preview(loaded.preview, 600, cancellation), cancellation);
            return (small, loaded.originalWidth, loaded.originalHeight, loaded.metadata);
        }
    }

    private static IRawDecoder CreateDecoder()
    {
        var decoder = new LibRawDecoder();
        string? bridge = Environment.GetEnvironmentVariable("PHOTOLAB_NATIVE_RAW_BRIDGE");
        return string.IsNullOrWhiteSpace(bridge) ? decoder : new SessionRawDecoder(decoder,
            async (path, token) => await NativeRawSession.OpenAsync(bridge, path, token));
    }

    public Task ResetAsync(CancellationToken cancellation = default)
        => _raw is SessionRawDecoder cached ? cached.ResetAsync(cancellation) : Task.CompletedTask;

    public ValueTask DisposeAsync()
        => _raw is IAsyncDisposable owned ? owned.DisposeAsync() : ValueTask.CompletedTask;

    public async Task<(FloatImageBuffer preview, int originalWidth, int originalHeight)> LoadLinearPreviewAsync(
        string path, CancellationToken cancellation = default)
    {
        var loaded = await LoadPreviewWithMetadataAsync(path, cancellation);
        return (loaded.preview, loaded.originalWidth, loaded.originalHeight);
    }

    public Task<(FloatImageBuffer preview, int originalWidth, int originalHeight, RawMetadata? metadata)> LoadPreviewWithMetadataAsync(
        string path, CancellationToken cancellation = default)
        => LoadPreviewCoreAsync(path, null, cancellation);

    /// <summary>Develops RAW with camera WB before creating a float preview; never applies WB to cached sRGB.</summary>
    public Task<(FloatImageBuffer preview, int originalWidth, int originalHeight, RawMetadata? metadata)> LoadPreviewWithMetadataAsync(
        string path, CameraWhiteBalance whiteBalance, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(whiteBalance);
        _ = whiteBalance.Normalize();
        return LoadPreviewCoreAsync(path, whiteBalance, cancellation);
    }

    private async Task<(FloatImageBuffer preview, int originalWidth, int originalHeight, RawMetadata? metadata)> LoadPreviewCoreAsync(
        string path, CameraWhiteBalance? whiteBalance, CancellationToken cancellation)
    {
        if (whiteBalance != null && !PhotoFormats.IsRaw(path)) throw new NotSupportedException("Camera WB requires a RAW image.");
        if (PhotoFormats.IsRaw(path))
        {
            cancellation.ThrowIfCancellationRequested();
            using var decoded = await DecodeRawAsync(path, whiteBalance, cancellation);
            var small = await Task.Run(() => ImageResize.Preview(decoded.Pixels, 1800, cancellation), cancellation);
            return (small, decoded.Pixels.Width, decoded.Pixels.Height, decoded.Metadata);
        }
        using var original = await LoadLinearFullAsync(path, cancellation);
        var preview = await Task.Run(() => ImageResize.Preview(original, 1800, cancellation), cancellation);
        return (preview, original.Width, original.Height, null);
    }

    public Task<FloatImageBuffer> LoadLinearFullAsync(string path, CancellationToken cancellation = default)
        => LoadFullCoreAsync(path, null, cancellation);

    public Task<FloatImageBuffer> LoadLinearFullAsync(string path, CameraWhiteBalance whiteBalance,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(whiteBalance);
        _ = whiteBalance.Normalize();
        return LoadFullCoreAsync(path, whiteBalance, cancellation);
    }

    private async Task<FloatImageBuffer> LoadFullCoreAsync(string path, CameraWhiteBalance? whiteBalance,
        CancellationToken cancellation)
    {
        if (whiteBalance != null && !PhotoFormats.IsRaw(path)) throw new NotSupportedException("Camera WB requires a RAW image.");
        if (PhotoFormats.IsRaw(path))
        {
            cancellation.ThrowIfCancellationRequested();
            using var decoded = await DecodeRawAsync(path, whiteBalance, cancellation);
            cancellation.ThrowIfCancellationRequested();
            return decoded.TakePixels();
        }
        if (PhotoFormats.IsStandardImage(path))
        {
            await ResetAsync(cancellation);
            return await Task.Run(() => SkiaImageCodec.LoadLinearFull(path, cancellation), cancellation);
        }
        throw new NotSupportedException("File extension is not in the supported photo list.");
    }

    private Task<RawImage> DecodeRawAsync(string path, CameraWhiteBalance? whiteBalance, CancellationToken cancellation)
        => whiteBalance == null ? _raw.DecodeAsync(path, cancellation)
            : _raw is IRawDeveloper developer ? developer.DevelopAsync(path, whiteBalance, cancellation)
            : throw new NotSupportedException("The selected RAW decoder does not support camera white balance.");

    public async Task<(PixelBuffer preview, int originalWidth, int originalHeight)> LoadPreviewAsync(
        string path, CancellationToken cancellation = default)
    {
        if (PhotoFormats.IsRaw(path))
        {
            PixelBuffer original = await LoadRaw8Async(path, cancellation);
            cancellation.ThrowIfCancellationRequested();
            PixelBuffer preview = await Task.Run(() => SkiaImageCodec.ResizePreview(original), cancellation);
            return (preview, original.Width, original.Height);
        }
        if (PhotoFormats.IsStandardImage(path))
            return await Task.Run(() => SkiaImageCodec.LoadPreview(path), cancellation);
        throw new NotSupportedException("File extension is not in the supported photo list.");
    }

    public Task<PixelBuffer> LoadFullAsync(string path, CancellationToken cancellation = default)
    {
        if (PhotoFormats.IsRaw(path)) return LoadRaw8Async(path, cancellation);
        if (PhotoFormats.IsStandardImage(path))
            return Task.Run(() => SkiaImageCodec.LoadFull(path), cancellation);
        throw new NotSupportedException("File extension is not in the supported photo list.");
    }

    private async Task<PixelBuffer> LoadRaw8Async(string path, CancellationToken cancellation)
    {
        // Preserve the original LibRaw RGBA8 path for default legacy callers.
        // Explicit decoder replacement also applies to legacy APIs, with quantization only at this boundary.
        if (_legacyRaw != null) return await _legacyRaw.DecodeAsync(path, cancellation);
        using var linear = await LoadLinearFullAsync(path, cancellation);
        return ImageConvert.ToSrgb8(linear);
    }
}
