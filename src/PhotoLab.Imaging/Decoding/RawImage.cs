namespace PhotoLab.Imaging.Decoding;

/// <summary>Owns a decoded surface until Dispose or a single TakePixels ownership transfer.</summary>
public sealed class RawImage : IDisposable
{
    private FloatImageBuffer? _pixels;

    public FloatImageBuffer Pixels => _pixels ?? throw new ObjectDisposedException(nameof(RawImage));
    public RawMetadata Metadata { get; }

    public RawImage(FloatImageBuffer pixels, RawMetadata metadata)
    {
        _pixels = pixels;
        Metadata = metadata;
    }

    public FloatImageBuffer TakePixels()
        => Interlocked.Exchange(ref _pixels, null) ?? throw new ObjectDisposedException(nameof(RawImage));

    public void Dispose() => Interlocked.Exchange(ref _pixels, null)?.Dispose();
}
