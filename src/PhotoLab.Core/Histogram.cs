namespace PhotoLab.Core;

public static class Histogram
{
    public static (int[] red, int[] green, int[] blue) Compute(PixelBuffer image)
    {
        var r = new int[256]; var g = new int[256]; var b = new int[256];
        var data = image.Data;
        for (int i = 0; i < data.Length; i += 4)
        {
            if (data[i + 3] == 0) continue;
            r[data[i]]++; g[data[i + 1]]++; b[data[i + 2]]++;
        }
        return (r, g, b);
    }
}
