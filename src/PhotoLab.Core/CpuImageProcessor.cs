using System.Threading;

namespace PhotoLab.Core;

/// <summary>
/// Reference CPU renderer: linear-light exposure/WB/contrast and perceptual-ish
/// shadow/highlight and saturation controls. Colorimetric accuracy is NOT guaranteed:
/// this initial JPEG workflow assumes sRGB and does not preserve embedded ICC data.
/// Pure C#; replaceable by ICC-aware, high bit depth CPU and GPU implementations.
/// </summary>
public sealed class CpuImageProcessor : IImageProcessor
{
    private static readonly float[] ToLinear = BuildDecodeLut();

    public PixelBuffer Process(PixelBuffer source, EditSettings edit, CancellationToken cancellation = default)
    {
        var input = source.Data;
        var output = GC.AllocateUninitializedArray<byte>(input.Length);
        float exposure = MathF.Pow(2f, (float)Math.Clamp(edit.Exposure, -4, 4));
        float contrast = (float)Math.Clamp(edit.Contrast, -100, 100) / 100f;
        float highlights = (float)Math.Clamp(edit.Highlights, -100, 100) / 100f;
        float shadows = (float)Math.Clamp(edit.Shadows, -100, 100) / 100f;
        float warmth = (float)Math.Clamp(edit.Temperature, -100, 100) / 100f;
        float tint = (float)Math.Clamp(edit.Tint, -100, 100) / 100f;
        float saturation = (float)Math.Clamp(edit.Saturation, -100, 100) / 100f;
        float vibrance = (float)Math.Clamp(edit.Vibrance, -100, 100) / 100f;

        float rGain = MathF.Pow(2f, warmth * 0.32f + tint * 0.10f);
        float gGain = MathF.Pow(2f, -tint * 0.18f);
        float bGain = MathF.Pow(2f, -warmth * 0.32f + tint * 0.10f);

        // Identity fast path: exact pixel and alpha preservation.
        if (edit == new EditSettings())
        {
            Buffer.BlockCopy(input, 0, output, 0, input.Length);
            return new PixelBuffer(source.Width, source.Height, output);
        }

        for (int i = 0; i < input.Length; i += 4)
        {
            if ((i & 0xFFFF) == 0) cancellation.ThrowIfCancellationRequested();

            float r = ToLinear[input[i]] * exposure * rGain;
            float g = ToLinear[input[i + 1]] * exposure * gGain;
            float b = ToLinear[input[i + 2]] * exposure * bGain;

            // Luminance-dependent local tone approximation, not a full HDR highlight reconstruction.
            float l = MathF.Max(0, 0.2126f * r + 0.7152f * g + 0.0722f * b);
            float highMask = SmoothStep(0.32f, 0.95f, l);
            float shadowMask = 1f - SmoothStep(0.035f, 0.45f, l);
            float tonalGain = MathF.Max(0.05f, 1f + highlights * highMask * 0.65f + shadows * shadowMask * 0.75f);
            r *= tonalGain; g *= tonalGain; b *= tonalGain;

            // Contrast about a middle-gray pivot in linear light.
            float c = 1f + contrast * 0.70f;
            r = (r - 0.18f) * c + 0.18f;
            g = (g - 0.18f) * c + 0.18f;
            b = (b - 0.18f) * c + 0.18f;

            float y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            float chroma = MathF.Max(r, MathF.Max(g, b)) - MathF.Min(r, MathF.Min(g, b));
            // More vibrance on weakly saturated colors.
            float satFactor = MathF.Max(0f, 1f + saturation + vibrance * (1f - Math.Clamp(chroma * 2f, 0f, 1f)) * 0.8f);
            r = y + (r - y) * satFactor;
            g = y + (g - y) * satFactor;
            b = y + (b - y) * satFactor;

            output[i]     = EncodeSrgb(r);
            output[i + 1] = EncodeSrgb(g);
            output[i + 2] = EncodeSrgb(b);
            output[i + 3] = input[i + 3];
        }
        return new PixelBuffer(source.Width, source.Height, output);
    }

    private static float[] BuildDecodeLut()
    {
        var lut = new float[256];
        for (int i = 0; i < lut.Length; i++)
        {
            float s = i / 255f;
            lut[i] = s <= 0.04045f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f);
        }
        return lut;
    }

    private static byte EncodeSrgb(float linear)
    {
        float c = Math.Clamp(linear, 0f, 1f);
        float encoded = c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
        return (byte)Math.Clamp((int)MathF.Round(encoded * 255f), 0, 255);
    }

    private static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
