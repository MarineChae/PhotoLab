namespace PhotoLab.Imaging.Decoding;

/// <summary>
/// Develops original camera data with explicit camera-channel WB before demosaic/color conversion.
/// Output owns linear sRGB pixels, as with IRawDecoder. A new WB requires development from the original.
/// </summary>
public interface IRawDeveloper : IRawDecoder
{
    Task<RawImage> DevelopAsync(string sourcePath, CameraWhiteBalance whiteBalance,
        CancellationToken cancellation = default);
}
