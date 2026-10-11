using PhotoLab.Core;

namespace PhotoLab.Imaging;

/// <summary>Shared CPU/GPU control validation and float gain preparation.</summary>
public readonly record struct FloatAdjustments(float Exposure, float Red, float Green, float Blue,
    float Contrast, float Highlights, float Shadows, float Saturation, float Vibrance)
{
    public static FloatAdjustments From(EditSettings edit)
    {
        if (!double.IsFinite(edit.Exposure) || !double.IsFinite(edit.Contrast) ||
            !double.IsFinite(edit.Highlights) || !double.IsFinite(edit.Shadows) ||
            !double.IsFinite(edit.Temperature) || !double.IsFinite(edit.Tint) ||
            !double.IsFinite(edit.Saturation) || !double.IsFinite(edit.Vibrance))
            throw new ArgumentException("Adjustment values must be finite.", nameof(edit));
        float warmth = (float)Math.Clamp(edit.Temperature, -100, 100) / 100f;
        float tint = (float)Math.Clamp(edit.Tint, -100, 100) / 100f;
        return new FloatAdjustments(MathF.Pow(2f, (float)Math.Clamp(edit.Exposure, -4, 4)),
            MathF.Pow(2f, warmth * 0.32f + tint * 0.10f), MathF.Pow(2f, -tint * 0.18f),
            MathF.Pow(2f, -warmth * 0.32f + tint * 0.10f),
            (float)Math.Clamp(edit.Contrast, -100, 100) / 100f,
            (float)Math.Clamp(edit.Highlights, -100, 100) / 100f,
            (float)Math.Clamp(edit.Shadows, -100, 100) / 100f,
            (float)Math.Clamp(edit.Saturation, -100, 100) / 100f,
            (float)Math.Clamp(edit.Vibrance, -100, 100) / 100f);
    }
}
