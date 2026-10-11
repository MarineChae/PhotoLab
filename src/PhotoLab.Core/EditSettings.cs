namespace PhotoLab.Core;

/// <summary>Non-destructive adjustment parameters. Values use a Lightroom-like UI scale,
/// but the rendering algorithms are independent and NOT intended to reproduce Adobe's results.</summary>
public sealed record EditSettings
{
    public double Exposure { get; init; } = 0;       // EV [-4,+4]
    public double Contrast { get; init; } = 0;       // [-100,+100]
    public double Highlights { get; init; } = 0;     // [-100,+100]
    public double Shadows { get; init; } = 0;        // [-100,+100]
    public double Temperature { get; init; } = 0;    // relative warmth [-100,+100]; not kelvin
    public double Tint { get; init; } = 0;           // green/magenta [-100,+100]
    public double Saturation { get; init; } = 0;     // [-100,+100]
    public double Vibrance { get; init; } = 0;       // [-100,+100]
}
