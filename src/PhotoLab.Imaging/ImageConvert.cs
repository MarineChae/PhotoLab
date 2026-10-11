using PhotoLab.Core;

namespace PhotoLab.Imaging;

/// <summary>sRGB transfer is applied at encoded input/display boundaries; alpha stays linear.</summary>
public static class ImageConvert
{
    public static FloatImageBuffer FromSrgb8(PixelBuffer source)
    {
        var result = new FloatImageBuffer(source.Width, source.Height);
        var output = result.WritablePixels;
        var input = source.Data;
        for (int i = 0; i < input.Length; i += 4)
        {
            output[i] = Decode(input[i] / 255f);
            output[i + 1] = Decode(input[i + 1] / 255f);
            output[i + 2] = Decode(input[i + 2] / 255f);
            output[i + 3] = input[i + 3] / 255f;
        }
        return result;
    }

    /// <summary>Decode normalized [0,1] sRGB float without an 8-bit or half-float intermediate.</summary>
    public static FloatImageBuffer FromSrgb32(int width, int height, ReadOnlySpan<float> rgba,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var result = FloatImageBuffer.FromLinearRgba32(width, height, rgba);
        try
        {
            var output = result.WritablePixels;
            for (int i = 0; i < output.Length; i += 4)
            {
                if (i % 65_536 == 0) cancellation.ThrowIfCancellationRequested();
                if (output[i] < 0 || output[i] > 1 || output[i + 1] < 0 || output[i + 1] > 1 ||
                    output[i + 2] < 0 || output[i + 2] > 1)
                    throw new ArgumentException("Encoded sRGB channels must be between zero and one.", nameof(rgba));
                output[i] = Decode(output[i]);
                output[i + 1] = Decode(output[i + 1]);
                output[i + 2] = Decode(output[i + 2]);
            }
            cancellation.ThrowIfCancellationRequested();
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    public static PixelBuffer ToSrgb8(FloatImageBuffer source)
    {
        var input = source.Pixels;
        var output = GC.AllocateUninitializedArray<byte>(input.Length);
        for (int i = 0; i < input.Length; i += 4)
        {
            output[i] = Encode(input[i]);
            output[i + 1] = Encode(input[i + 1]);
            output[i + 2] = Encode(input[i + 2]);
            output[i + 3] = (byte)Math.Clamp((int)MathF.Round(input[i + 3] * 255f), 0, 255);
        }
        return new PixelBuffer(source.Width, source.Height, output);
    }

    /// <summary>Quantize only at the PNG16 export boundary; RGB is sRGB encoded, alpha stays linear.</summary>
    public static ushort[] ToSrgb16(FloatImageBuffer source, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var input = source.Pixels;
        var output = GC.AllocateUninitializedArray<ushort>(input.Length);
        WriteSrgb16(input, output, cancellation);
        return output;
    }

    /// <summary>Fill a caller-owned row with encoded RGBA16; supports bounded-memory PNG writing.</summary>
    public static void WriteSrgb16(ReadOnlySpan<float> input, Span<ushort> output, CancellationToken cancellation = default)
    {
        if (input.Length != output.Length || input.Length % 4 != 0)
            throw new ArgumentException("RGBA sample counts must match.");
        cancellation.ThrowIfCancellationRequested();
        for (int i = 0; i < input.Length; i += 4)
        {
            if (i % 65_536 == 0) cancellation.ThrowIfCancellationRequested();
            output[i] = (ushort)Math.Clamp((int)MathF.Round(EncodeValue(input[i]) * 65535f), 0, 65535);
            output[i + 1] = (ushort)Math.Clamp((int)MathF.Round(EncodeValue(input[i + 1]) * 65535f), 0, 65535);
            output[i + 2] = (ushort)Math.Clamp((int)MathF.Round(EncodeValue(input[i + 2]) * 65535f), 0, 65535);
            output[i + 3] = (ushort)Math.Clamp((int)MathF.Round(input[i + 3] * 65535f), 0, 65535);
        }
        cancellation.ThrowIfCancellationRequested();
    }

    private static float Decode(float value) => value <= 0.04045f
        ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    private static byte Encode(float value)
    {
        return (byte)Math.Clamp((int)MathF.Round(EncodeValue(value) * 255f), 0, 255);
    }

    private static float EncodeValue(float value)
    {
        value = Math.Clamp(value, 0, 1);
        return value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;
    }
}
