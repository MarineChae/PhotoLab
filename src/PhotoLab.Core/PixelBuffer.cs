namespace PhotoLab.Core;

/// <summary>Interleaved straight-alpha RGBA8 buffer. The engine reads without modifying source.</summary>
public sealed class PixelBuffer
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }

    public PixelBuffer(int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 4 != rgba.Length)
            throw new ArgumentException("RGBA8 buffer dimensions do not match data length.");
        Width = width;
        Height = height;
        Data = rgba;
    }
}
