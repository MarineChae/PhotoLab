using PhotoLab.Core;
using PhotoLab.Imaging;
using PhotoLab.Infrastructure;

namespace PhotoLab.Desktop;

/// <summary>Compatibility entry points for existing clients; native execution lives in Infrastructure.</summary>
public sealed class LibRawCommandDecoder
{
    private readonly LibRawDecoder _decoder = new();

    public Task<PixelBuffer> DecodeAsync(string sourcePath, CancellationToken cancellation = default)
        => _decoder.DecodeLegacyAsync(sourcePath, cancellation);

    public Task<FloatImageBuffer> DecodeLinearAsync(string sourcePath, CancellationToken cancellation = default)
        => _decoder.DecodeLinearAsync(sourcePath, cancellation);
}
