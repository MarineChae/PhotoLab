using PhotoLab.Core;

namespace PhotoLab.Imaging;

/// <summary>
/// Existing basic tone controls in linear sRGB, with no RGB clipping or 8-bit intermediate.
/// WB remains a relative RGB gain; camera WB and color conversion belong to the decoder.
/// </summary>
public sealed class FloatImageProcessor : IFloatImageProcessor
{
    private const float MIDDLE_GRAY = 0.18f;
    public string Name => "CPU";
    public FloatImageBuffer Process(FloatImageBuffer source, EditSettings edit, CancellationToken cancellation = default)
    {
        var controls = FloatAdjustments.From(edit);
        cancellation.ThrowIfCancellationRequested();
        var input = source.Pixels;
        var result = new FloatImageBuffer(source.Width, source.Height);
        try
        {
            var output = result.WritablePixels;
            if (edit == new EditSettings()) { input.CopyTo(output); return result; }
            for (int i = 0; i < input.Length; i += 4)
            {
                if ((i & 0xFFFF) == 0) cancellation.ThrowIfCancellationRequested();
                float r = input[i], g = input[i + 1], b = input[i + 2];
                // All stages remain in linear sRGB; alpha is copied unchanged below.
                // Separate stages permit additional controls without changing existing tone formulas.
                AdjustLight(ref r, ref g, ref b, controls.Exposure, controls.Red, controls.Green, controls.Blue);
                AdjustTone(ref r, ref g, ref b, controls.Highlights, controls.Shadows);
                AdjustContrast(ref r, ref g, ref b, controls.Contrast);
                AdjustColor(ref r, ref g, ref b, controls.Saturation, controls.Vibrance);
                if (!float.IsFinite(r) || !float.IsFinite(g) || !float.IsFinite(b))
                    throw new ArithmeticException("Image processing exceeded the finite float range.");
                output[i] = r;
                output[i + 1] = g;
                output[i + 2] = b;
                output[i + 3] = input[i + 3];
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static void AdjustLight(ref float r, ref float g, ref float b,
        float exposure, float rGain, float gGain, float bGain)
    {
        // Keep the multiplication order to preserve the reference CPU's float rounding.
        r = r * exposure * rGain;
        g = g * exposure * gGain;
        b = b * exposure * bGain;
    }

    private static void AdjustTone(ref float r, ref float g, ref float b, float highlights, float shadows)
    {
        float luminance = MathF.Max(0, 0.2126f * r + 0.7152f * g + 0.0722f * b);
        float highMask = SmoothStep(0.32f, 0.95f, luminance);
        float shadowMask = 1f - SmoothStep(0.035f, 0.45f, luminance);
        float gain = MathF.Max(0.05f, 1f + highlights * highMask * 0.65f + shadows * shadowMask * 0.75f);
        r *= gain; g *= gain; b *= gain;
    }

    private static void AdjustContrast(ref float r, ref float g, ref float b, float contrast)
    {
        float factor = 1f + contrast * 0.70f;
        r = (r - MIDDLE_GRAY) * factor + MIDDLE_GRAY;
        g = (g - MIDDLE_GRAY) * factor + MIDDLE_GRAY;
        b = (b - MIDDLE_GRAY) * factor + MIDDLE_GRAY;
    }

    private static void AdjustColor(ref float r, ref float g, ref float b, float saturation, float vibrance)
    {
        float y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        float chroma = MathF.Max(r, MathF.Max(g, b)) - MathF.Min(r, MathF.Min(g, b));
        float factor = MathF.Max(0, 1f + saturation + vibrance * (1f - Math.Clamp(chroma * 2, 0, 1)) * 0.8f);
        r = y + (r - y) * factor;
        g = y + (g - y) * factor;
        b = y + (b - y) * factor;
    }

    private static float SmoothStep(float start, float end, float value)
    {
        float t = Math.Clamp((value - start) / (end - start), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
