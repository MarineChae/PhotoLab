namespace PhotoLab.Imaging;

/// <summary>Owned 256-bin linear sRGB/Y counts from visible preview pixels; partial alpha counts once.</summary>
public sealed class ImageHistogram
{
    public const int BIN_COUNT = 256;
    private readonly int[] _red = new int[BIN_COUNT];
    private readonly int[] _green = new int[BIN_COUNT];
    private readonly int[] _blue = new int[BIN_COUNT];
    private readonly int[] _luminance = new int[BIN_COUNT];
    public ReadOnlySpan<int> Red => _red;
    public ReadOnlySpan<int> Green => _green;
    public ReadOnlySpan<int> Blue => _blue;
    public ReadOnlySpan<int> Luminance => _luminance;
    public int PixelCount { get; private set; }
    public int ShadowClipped { get; private set; }
    public int HighlightClipped { get; private set; }

    private ImageHistogram() { }

    public static ImageHistogram Compute(FloatImageBuffer source, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var result = new ImageHistogram();
        var pixels = source.Pixels;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (i % 65_536 == 0) cancellation.ThrowIfCancellationRequested();
            if (pixels[i + 3] == 0) continue;
            float red = pixels[i], green = pixels[i + 1], blue = pixels[i + 2];
            result._red[Bin(red)]++; result._green[Bin(green)]++; result._blue[Bin(blue)]++;
            // Rec.709/sRGB linear-light luminance. Extended gamut values are retained for clipping counts.
            result._luminance[Bin(0.2126 * red + 0.7152 * green + 0.0722 * blue)]++;
            result.PixelCount++;
            if (red <= 0 || green <= 0 || blue <= 0) result.ShadowClipped++;
            if (red >= 1 || green >= 1 || blue >= 1) result.HighlightClipped++;
        }
        cancellation.ThrowIfCancellationRequested();
        return result;
    }

    private static int Bin(double value) => value <= 0 ? 0 : value >= 1 ? BIN_COUNT - 1 : (int)(value * BIN_COUNT);
}
