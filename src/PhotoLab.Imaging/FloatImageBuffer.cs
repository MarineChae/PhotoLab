using System.Buffers;

namespace PhotoLab.Imaging;

/// <summary>
/// Owned, pooled, straight-alpha RGBA32F in linear sRGB (D65). RGB may exceed 1.
/// Dispose only after all readers finish; do not retain spans after disposal.
/// </summary>
public sealed class FloatImageBuffer : IDisposable
{
    private const int MAX_POOLED_SAMPLES = 4_194_304;
    private float[]? _data;
    private readonly bool _pooled;

    public int Width { get; }
    public int Height { get; }
    public int Stride => checked(Width * 4);
    public int StrideBytes => checked(Stride * sizeof(float));
    public string PixelFormat => "RGBA32F";
    public string ColorSpace => "Linear sRGB D65";
    public ReadOnlySpan<float> Pixels => WritablePixels;

    internal Span<float> WritablePixels
    {
        get
        {
            ObjectDisposedException.ThrowIf(_data == null, this);
            return _data.AsSpan(0, checked(Stride * Height));
        }
    }

    // Internal writers must initialize the complete active span before publishing the buffer.
    internal FloatImageBuffer(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        int length = checked(width * height * 4);
        Width = width; Height = height;
        // Exact large allocations avoid pool bucket rounding inflating full-resolution RAW memory.
        _pooled = length <= MAX_POOLED_SAMPLES;
        _data = _pooled ? ArrayPool<float>.Shared.Rent(length) : GC.AllocateUninitializedArray<float>(length);
    }

    public static FloatImageBuffer FromLinearRgba32(int width, int height, ReadOnlySpan<float> rgba)
    {
        if (width <= 0 || height <= 0 || checked(width * height * 4) != rgba.Length)
            throw new ArgumentException("Linear RGBA data does not match positive image dimensions.");
        for (int i = 0; i < rgba.Length; i++)
        {
            if (!float.IsFinite(rgba[i]) || (i % 4 == 3 && (rgba[i] < 0 || rgba[i] > 1)))
                throw new ArgumentException("Linear RGB must be finite and alpha must be between 0 and 1.", nameof(rgba));
        }
        var result = new FloatImageBuffer(width, height);
        rgba.CopyTo(result.WritablePixels);
        return result;
    }

    public static FloatImageBuffer FromLinearRgba16(int width, int height, ReadOnlySpan<ushort> rgba)
    {
        if (width <= 0 || height <= 0 || checked(width * height * 4) != rgba.Length)
            throw new ArgumentException("Linear RGBA data does not match positive image dimensions.");
        var result = new FloatImageBuffer(width, height);
        var output = result.WritablePixels;
        for (int i = 0; i < rgba.Length; i++) output[i] = rgba[i] / 65535f;
        return result;
    }

    /// <summary>Expand native-endian linear RGB16 to opaque RGBA32F without an 8-bit or RGBA16 intermediate.</summary>
    public static FloatImageBuffer FromLinearRgb16(int width, int height, ReadOnlySpan<ushort> rgb,
        CancellationToken cancellation = default)
    {
        if (width <= 0 || height <= 0 || checked(width * height * 3) != rgb.Length)
            throw new ArgumentException("Linear RGB data does not match positive image dimensions.");
        cancellation.ThrowIfCancellationRequested();
        var result = new FloatImageBuffer(width, height);
        try
        {
            var output = result.WritablePixels;
            for (int pixel = 0; pixel < width * height; pixel++)
            {
                if ((pixel & 0x3FFF) == 0) cancellation.ThrowIfCancellationRequested();
                output[pixel * 4] = rgb[pixel * 3] / 65535f;
                output[pixel * 4 + 1] = rgb[pixel * 3 + 1] / 65535f;
                output[pixel * 4 + 2] = rgb[pixel * 3 + 2] / 65535f;
                output[pixel * 4 + 3] = 1;
            }
            cancellation.ThrowIfCancellationRequested();
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    public void Dispose()
    {
        var data = Interlocked.Exchange(ref _data, null);
        if (data != null && _pooled) ArrayPool<float>.Shared.Return(data);
    }
}
