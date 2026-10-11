using System.Buffers;
using System.Globalization;
using System.Text;

namespace PhotoLab.Imaging;

/// <summary>Reads LibRaw's linear, 16-bit P6 output directly into float RGB without 8-bit quantization.</summary>
public static class LinearPpmDecoder
{
    private const long MAX_PIXELS = 120_000_000;
    private const int MAX_DIMENSION = 50_000;

    public static FloatImageBuffer Read(string path, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var stream = File.OpenRead(path);
        if (ReadToken(stream) != "P6") throw new InvalidDataException("Expected linear 16-bit RGB PPM from LibRaw.");
        int width = ReadNumber(stream);
        int height = ReadNumber(stream);
        int maximum = ReadNumber(stream);
        if (width > MAX_DIMENSION || height > MAX_DIMENSION || (long)width * height > MAX_PIXELS)
            throw new InvalidDataException("RAW image dimensions exceed the safety limit.");
        if (maximum != 65535) throw new InvalidDataException("Expected 16-bit PPM samples with maximum 65535.");
        var result = new FloatImageBuffer(width, height);
        byte[] row = ArrayPool<byte>.Shared.Rent(checked(width * 6));
        try
        {
            var output = result.WritablePixels;
            for (int y = 0; y < height; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                stream.ReadExactly(row.AsSpan(0, width * 6));
                for (int x = 0; x < width; x++)
                {
                    int sample = x * 6;
                    int pixel = (y * width + x) * 4;
                    // PPM stores unsigned 16-bit RGB in big-endian order; alpha is opaque.
                    output[pixel] = ((row[sample] << 8) | row[sample + 1]) / 65535f;
                    output[pixel + 1] = ((row[sample + 2] << 8) | row[sample + 3]) / 65535f;
                    output[pixel + 2] = ((row[sample + 4] << 8) | row[sample + 5]) / 65535f;
                    output[pixel + 3] = 1;
                }
            }
            return result;
        }
        catch { result.Dispose(); throw; }
        finally { ArrayPool<byte>.Shared.Return(row); }
    }

    private static int ReadNumber(Stream stream)
    {
        if (!int.TryParse(ReadToken(stream), NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value <= 0)
            throw new InvalidDataException("Invalid PPM dimension or sample maximum.");
        return value;
    }

    private static string ReadToken(Stream stream)
    {
        var token = new StringBuilder();
        int value;
        while (true)
        {
            value = stream.ReadByte();
            if (value < 0) throw new EndOfStreamException("Incomplete PPM header.");
            if (value == '#') { while ((value = stream.ReadByte()) >= 0 && value != '\n') { } continue; }
            if (!IsWhitespace(value)) break;
        }
        while (value >= 0 && !IsWhitespace(value))
        {
            if (value > 127 || token.Length >= 30) throw new InvalidDataException("Malformed PPM header.");
            token.Append((char)value);
            value = stream.ReadByte();
        }
        // Consume only the header separator; binary samples can start with whitespace or '#'.
        if (value == '\r')
        {
            int next = stream.ReadByte();
            if (next != '\n' && next >= 0) stream.Position--;
        }
        return token.ToString();
    }

    private static bool IsWhitespace(int value) => value is 9 or 10 or 11 or 12 or 13 or 32;
}
