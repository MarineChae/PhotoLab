namespace PhotoLab.Imaging.Decoding;

/// <summary>Fast owned WB preview; Metadata dimensions describe full developed output, Pixels may be smaller.</summary>
public interface IRawPreviewDeveloper : IRawDeveloper
{
    Task<RawImage> DevelopPreviewAsync(string sourcePath, CameraWhiteBalance whiteBalance, int maxEdge,
        CancellationToken cancellation = default);
}
