namespace PhotoLab.Imaging.Decoding;

/// <summary>
/// Output dimensions and source extension are known after decoding.
/// Camera fields remain null until extracted from the file, never inferred from its extension.
/// </summary>
public sealed record RawMetadata(int Width, int Height, string SourceExtension,
    string? CameraMake = null, string? CameraModel = null, string? MetadataWarning = null)
{
    public double? IsoSpeed { get; init; }
    public double? ExposureSeconds { get; init; }
    public double? Aperture { get; init; }
    public RawColorInfo? CameraColor { get; init; }
    /// <summary>Explicit development WB; null means the decoder's camera-WB default. AsShot remains original metadata.</summary>
    public CameraWhiteBalance? AppliedWhiteBalance { get; init; }
}
