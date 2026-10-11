using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PhotoLab.Imaging;

namespace PhotoLab.Desktop;

/// <summary>Linear RGB/Y bins with shared logarithmic frequency scale; data arrives with the accepted frame.</summary>
public sealed class HistogramView : Control
{
    private readonly Pen _red = new(new SolidColorBrush(Color.Parse("#E96767")), 1);
    private readonly Pen _green = new(new SolidColorBrush(Color.Parse("#70CA89")), 1);
    private readonly Pen _blue = new(new SolidColorBrush(Color.Parse("#70A5EA")), 1);
    private readonly Pen _luminance = new(new SolidColorBrush(Color.Parse("#CCD2DC")), 1);
    public ImageHistogram? Histogram { get; private set; }

    public void SetHistogram(ImageHistogram? histogram)
    {
        Histogram = histogram;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (Histogram == null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        int peak = 0;
        for (int i = 0; i < ImageHistogram.BIN_COUNT; i++)
            peak = Math.Max(peak, Math.Max(Math.Max(Histogram.Red[i], Histogram.Green[i]),
                Math.Max(Histogram.Blue[i], Histogram.Luminance[i])));
        if (peak == 0) return;
        double scale = Math.Log(1.0 + peak);
        Draw(context, Histogram.Red, _red, scale); Draw(context, Histogram.Green, _green, scale);
        Draw(context, Histogram.Blue, _blue, scale); Draw(context, Histogram.Luminance, _luminance, scale);
    }

    private void Draw(DrawingContext context, ReadOnlySpan<int> counts, Pen pen, double scale)
    {
        // Explicit loop keeps drawing allocation independent of image size.
        var previous = new Point(0, Bounds.Height - 1 - Math.Log(1.0 + counts[0]) / scale * (Bounds.Height - 2));
        for (int i = 1; i < counts.Length; i++)
        {
            var current = new Point(i * Bounds.Width / (ImageHistogram.BIN_COUNT - 1),
                Bounds.Height - 1 - Math.Log(1.0 + counts[i]) / scale * (Bounds.Height - 2));
            context.DrawLine(pen, previous, current);previous = current;
        }
    }
}
