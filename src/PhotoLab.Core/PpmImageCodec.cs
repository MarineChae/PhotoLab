using System.Globalization;
using System.Text;

namespace PhotoLab.Core;

/// <summary>
/// Reads the binary PPM (P6) and grayscale PGM (P5) outputs from LibRaw's dcraw_emu.
/// The temporary PPM can carry 16-bit samples, but the current editor returns RGBA8.
/// This is a compatibility bridge, NOT a high dynamic range RAW processing pipeline.
/// </summary>
public static class PpmImageCodec
{
    private const long MaxPixels = 120_000_000;
    private const int MaxDimension = 50_000;

    public static PixelBuffer Read(string path)
    {
        using var fs = File.OpenRead(path);
        return Read(fs);
    }

    public static PixelBuffer Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var magic = ReadToken(stream);
        if (magic != "P6" && magic != "P5")
            throw new InvalidDataException("Expected binary PPM (P6) or PGM (P5) data from LibRaw.");

        int width = ReadPositiveInt(stream, "width");
        int height = ReadPositiveInt(stream, "height");
        int maxValue = ReadPositiveInt(stream, "maximum component value");
        if (width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
            throw new InvalidDataException("Image dimensions exceed the prototype's safety limit.");
        if (maxValue > 65535)
            throw new InvalidDataException("PPM component values must be at most 65535.");

        int channels = magic == "P6" ? 3 : 1;
        int bytesPerSample = maxValue <= 255 ? 1 : 2;
        // The token parser consumed exactly one whitespace separator after maxValue.
        // Do not skip further bytes: pixel data may legally start with whitespace or '#'.
        int rowSamples = checked(width * channels);
        var row = new byte[checked(rowSamples * bytesPerSample)];
        var rgba = new byte[checked(width * height * 4)];

        for (int y = 0; y < height; y++)
        {
            stream.ReadExactly(row);
            for (int x = 0; x < width; x++)
            {
                int dst = (y * width + x) * 4;
                if (channels == 1)
                {
                    byte c = ToByte(ReadSample(row, x, bytesPerSample), maxValue);
                    rgba[dst] = c;
                    rgba[dst + 1] = c;
                    rgba[dst + 2] = c;
                }
                else
                {
                    int offset = x * 3;
                    rgba[dst] = ToByte(ReadSample(row, offset, bytesPerSample), maxValue);
                    rgba[dst + 1] = ToByte(ReadSample(row, offset + 1, bytesPerSample), maxValue);
                    rgba[dst + 2] = ToByte(ReadSample(row, offset + 2, bytesPerSample), maxValue);
                }
                rgba[dst + 3] = 255;
            }
        }
        return new PixelBuffer(width, height, rgba);
    }

    private static byte ToByte(int value, int max) => (byte)Math.Clamp((value * 255L + max / 2L) / max, 0, 255);

    private static int ReadSample(byte[] row, int sampleIndex, int bytesPerSample)
    {
        int index = sampleIndex * bytesPerSample;
        // PPM/PGM stores 16-bit values in network byte order (big endian).
        return bytesPerSample == 1 ? row[index] : (row[index] << 8) | row[index + 1];
    }

    private static int ReadPositiveInt(Stream stream, string name)
    {
        if (!int.TryParse(ReadToken(stream), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
            throw new InvalidDataException($"Invalid PPM {name}.");
        return number;
    }

    private static string ReadToken(Stream stream)
    {
        var token = new StringBuilder();
        int b;
        while (true)
        {
            b = stream.ReadByte();
            if (b < 0) throw new EndOfStreamException("Unexpected end of PPM header.");
            if (b == '#')
            {
                SkipComment(stream);
                continue;
            }
            if (!IsWhitespace(b)) break;
        }
        do
        {
            if (b == '#')
            {
                SkipComment(stream);
                break;
            }
            if (IsWhitespace(b))
            {
                // CRLF is one header newline on Windows. Peek requires a seekable stream.
                if (b == '\r' && stream.CanSeek)
                {
                    int next = stream.ReadByte();
                    if (next != '\n' && next >= 0) stream.Position--;
                }
                break;
            }
            if (b > 127 || token.Length >= 30)
                throw new InvalidDataException("Malformed PPM header token.");
            token.Append((char)b);
            b = stream.ReadByte();
        } while (b >= 0);
        if (token.Length == 0) throw new InvalidDataException("Missing PPM header token.");
        return token.ToString();
    }

    private static void SkipComment(Stream stream)
    {
        int b;
        while ((b = stream.ReadByte()) >= 0 && b != '\n') { }
    }

    private static bool IsWhitespace(int b) => b is 9 or 10 or 11 or 12 or 13 or 32;
}
