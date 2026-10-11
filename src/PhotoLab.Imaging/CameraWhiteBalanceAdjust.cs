using PhotoLab.Imaging.Decoding;

namespace PhotoLab.Imaging;

/// <summary>Relative camera-channel WB offsets around as-shot, not Kelvin or post-output sRGB gains.</summary>
public static class CameraWhiteBalanceAdjust
{
    private const double OFFSET_TOLERANCE = 0.0001;

    public static CameraWhiteBalance Apply(CameraWhiteBalance asShot, double warmth, double tint)
    {
        if (!double.IsFinite(warmth) || !double.IsFinite(tint) || Math.Abs(warmth) > 100 || Math.Abs(tint) > 100)
            throw new ArgumentException("Camera WB offsets must be finite and between -100 and 100.");
        var basis = asShot.Normalize();
        double w = warmth / 100, t = tint / 100;
        // Opposed red/blue warmth; tint raises red/blue relative to both physical green channels.
        float red = (float)Math.Pow(2, w * 0.32 + t * 0.10);
        float green = (float)Math.Pow(2, -t * 0.18);
        float blue = (float)Math.Pow(2, -w * 0.32 + t * 0.10);
        var result = new CameraWhiteBalance(basis.Channel0 * red, basis.Channel1 * green,
            basis.Channel2 * blue, basis.Channel3 * green);
        _ = result.Normalize();
        return result;
    }

    /// <summary>Restore controls from saved ratios. Unrepresentable custom WB stays custom, never silently clamped.</summary>
    public static bool TryGetOffsets(CameraWhiteBalance asShot, CameraWhiteBalance? current, out double warmth, out double tint)
    {
        warmth = tint = 0;
        if (current == null) return true;
        asShot.Validate(); current.Validate();
        double red = Math.Log2(((double)current.Channel0 / current.Channel1) / ((double)asShot.Channel0 / asShot.Channel1));
        double blue = Math.Log2(((double)current.Channel2 / current.Channel1) / ((double)asShot.Channel2 / asShot.Channel1));
        warmth = (red - blue) / 0.64 * 100;
        tint = (red + blue) / 0.56 * 100;
        bool fourthMatches = asShot.Channel3 == 0 ? current.Channel3 == 0
            : Math.Abs(((double)current.Channel3 / current.Channel1) / ((double)asShot.Channel3 / asShot.Channel1) - 1) <= OFFSET_TOLERANCE;
        if (!fourthMatches || !double.IsFinite(warmth) || !double.IsFinite(tint) ||
            Math.Abs(warmth) > 100 + OFFSET_TOLERANCE || Math.Abs(tint) > 100 + OFFSET_TOLERANCE) return false;
        warmth = Math.Clamp(warmth, -100, 100); tint = Math.Clamp(tint, -100, 100);
        return true;
    }
}
