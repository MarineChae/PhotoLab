namespace PhotoLab.Imaging.Decoding;

/// <summary>Owns cached unpacked camera data. Development returns separately owned linear sRGB pixels.</summary>
public interface IRawSession : IAsyncDisposable
{
    Task<FloatImageBuffer> DevelopAsync(CameraWhiteBalance whiteBalance, CancellationToken cancellation = default);
    Task<FloatImageBuffer> DevelopPreviewAsync(CameraWhiteBalance whiteBalance, int maxEdge,
        CancellationToken cancellation = default);
}
