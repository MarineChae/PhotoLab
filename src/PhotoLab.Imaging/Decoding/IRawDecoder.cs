namespace PhotoLab.Imaging.Decoding;

/// <summary>
/// Decodes a supported camera file into owned linear sRGB float data.
/// Extensions are discovery hints; the decoder validates the actual file.
/// The caller must dispose the result or explicitly take ownership of its pixels.
/// </summary>
public interface IRawDecoder
{
    Task<RawImage> DecodeAsync(string sourcePath, CancellationToken cancellation = default);
}
