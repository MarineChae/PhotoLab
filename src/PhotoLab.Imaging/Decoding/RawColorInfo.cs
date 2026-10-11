namespace PhotoLab.Imaging.Decoding;

/// <summary>LibRaw camera-channel multipliers; channel 3 may be absent (zero), never alpha.</summary>
public sealed record CameraWhiteBalance(float Channel0, float Channel1, float Channel2, float Channel3)
{
    public void Validate()
    {
        if (!float.IsFinite(Channel0) || !float.IsFinite(Channel1) ||
            !float.IsFinite(Channel2) || !float.IsFinite(Channel3) ||
            Channel0 <= 0 || Channel1 <= 0 || Channel2 <= 0 || Channel3 < 0)
            throw new ArgumentException("Invalid camera white balance coefficients.");
    }

    /// <summary>WB is a ratio. Unit minimum avoids LibRaw's tiny-coefficient fallback without changing that ratio.</summary>
    public CameraWhiteBalance Normalize()
    {
        Validate();
        double minimum = Math.Min(Channel0, Math.Min(Channel1, Channel2));
        if (Channel3 > 0) minimum = Math.Min(minimum, Channel3);
        var normalized = new CameraWhiteBalance((float)(Channel0 / minimum), (float)(Channel1 / minimum),
            (float)(Channel2 / minimum), (float)(Channel3 / minimum));
        normalized.Validate();
        // scale_colors multiplies a normalized coefficient by 65535 before dividing by the white level.
        float maximum = Math.Max(normalized.Channel0, Math.Max(normalized.Channel1, Math.Max(normalized.Channel2, normalized.Channel3)));
        if (maximum > float.MaxValue / 65535f) throw new ArgumentException("Camera WB ratio exceeds the native float range.");
        return normalized;
    }
}

/// <summary>
/// Full float precision from LibRaw C getters. The 3x4 matrix is row-major camera-channel to sRGB.
/// These values describe pre-output camera processing and must not be reapplied to an sRGB surface.
/// </summary>
public sealed class RawColorInfo
{
    private readonly float[] _cameraToSrgb;

    public CameraWhiteBalance AsShot { get; }
    public ReadOnlySpan<float> CameraToSrgb => _cameraToSrgb;
    public string LibRawVersion { get; }

    public RawColorInfo(CameraWhiteBalance asShot, ReadOnlySpan<float> cameraToSrgb, string libRawVersion)
    {
        if (cameraToSrgb.Length != 12) throw new ArgumentException("Expected a 3x4 camera matrix.", nameof(cameraToSrgb));
        foreach (float value in cameraToSrgb)
            if (!float.IsFinite(value)) throw new ArgumentException("Camera matrix values must be finite.", nameof(cameraToSrgb));
        asShot.Validate();
        AsShot = asShot;
        _cameraToSrgb = cameraToSrgb.ToArray();
        LibRawVersion = libRawVersion;
    }
}
