namespace PhotoLab.Imaging;

/// <summary>Area-averaged preview downsampling in linear light; transparent RGB is weighted by alpha.</summary>
public static class ImageResize
{
    public static FloatImageBuffer Preview(FloatImageBuffer source, int maxEdge,
        CancellationToken cancellation = default)
        => PreviewCore(source.Width, source.Height, source.Pixels, [], maxEdge, cancellation);

    /// <summary>Downsample opaque linear RGB16 without allocating an input-sized RGBA32F surface.</summary>
    public static FloatImageBuffer PreviewRgb16(int sourceWidth, int sourceHeight, ReadOnlySpan<ushort> rgb,
        int maxEdge, CancellationToken cancellation = default)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || (long)sourceWidth * sourceHeight * 3 != rgb.Length)
            throw new ArgumentException("RGB16 dimensions do not match the input.");
        return PreviewCore(sourceWidth, sourceHeight, [], rgb, maxEdge, cancellation);
    }

    private static FloatImageBuffer PreviewCore(int sourceWidth, int sourceHeight, ReadOnlySpan<float> input,
        ReadOnlySpan<ushort> rgb, int maxEdge, CancellationToken cancellation)
    {
        if (maxEdge <= 0) throw new ArgumentOutOfRangeException(nameof(maxEdge));
        cancellation.ThrowIfCancellationRequested();
        double ratio = Math.Min(1, maxEdge / (double)Math.Max(sourceWidth, sourceHeight));
        int width = Math.Max(1, (int)Math.Round(sourceWidth * ratio));
        int height = Math.Max(1, (int)Math.Round(sourceHeight * ratio));
        bool opaqueRgb = !rgb.IsEmpty;
        int channels = opaqueRgb ? 3 : 4;
        if (opaqueRgb && width == sourceWidth && height == sourceHeight)
            return FloatImageBuffer.FromLinearRgb16(sourceWidth, sourceHeight, rgb, cancellation);
        var result = new FloatImageBuffer(width, height);
        try
        {
            var output = result.WritablePixels;
            if (width == sourceWidth && height == sourceHeight) { input.CopyTo(output); return result; }
            double scaleX = sourceWidth / (double)width;
            double scaleY = sourceHeight / (double)height;
            // Each axis footprint is shared by every row/column; avoid recomputing bounds and overlaps per tap.
            var horizontal = Coverage(sourceWidth, width);
            var vertical = Coverage(sourceHeight, height);
            for (int y = 0; y < height; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    double red = 0, green = 0, blue = 0, alpha = 0;
                    for (int yt = vertical.Offsets[y]; yt < vertical.Offsets[y + 1]; yt++)
                    {
                        int sy = vertical.Starts[y] + yt - vertical.Offsets[y];
                        double weightY = vertical.Weights[yt];
                        for (int xt = horizontal.Offsets[x]; xt < horizontal.Offsets[x + 1]; xt++)
                        {
                            int sx = horizontal.Starts[x] + xt - horizontal.Offsets[x];
                            double weight = weightY * horizontal.Weights[xt];
                            int pixel = (sy * sourceWidth + sx) * channels;
                            float r, g, b;
                            double coverage;
                            if (opaqueRgb)
                            {
                                r = rgb[pixel] / 65535f; g = rgb[pixel + 1] / 65535f; b = rgb[pixel + 2] / 65535f;
                                coverage = weight;
                            }
                            else
                            {
                                r = input[pixel]; g = input[pixel + 1]; b = input[pixel + 2];
                                coverage = input[pixel + 3] * weight;
                            }
                            red += r * coverage;
                            green += g * coverage;
                            blue += b * coverage;
                            alpha += coverage;
                        }
                    }
                    int target = (y * width + x) * 4;
                    output[target] = alpha > 0 ? (float)(red / alpha) : 0;
                    output[target + 1] = alpha > 0 ? (float)(green / alpha) : 0;
                    output[target + 2] = alpha > 0 ? (float)(blue / alpha) : 0;
                    output[target + 3] = (float)(alpha / (scaleX * scaleY));
                }
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static (int[] Starts, int[] Offsets, double[] Weights) Coverage(int size, int target)
    {
        double scale = size / (double)target;
        var starts = new int[target];
        var offsets = new int[target + 1];
        for (int i = 0; i < target; i++)
        {
            starts[i] = (int)(i * scale);
            int end = Math.Min(size, (int)Math.Ceiling((i + 1) * scale));
            offsets[i + 1] = checked(offsets[i] + end - starts[i]);
        }
        var weights = new double[offsets[target]];
        for (int i = 0; i < target; i++)
            for (int tap = offsets[i]; tap < offsets[i + 1]; tap++)
            {
                int sample = starts[i] + tap - offsets[i];
                weights[tap] = Math.Min((i + 1) * scale, sample + 1) - Math.Max(i * scale, sample);
            }
        return (starts, offsets, weights);
    }
}
