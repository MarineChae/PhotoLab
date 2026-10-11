using System.ComponentModel;
using System.Globalization;
using PhotoLab.Imaging.Decoding;

namespace PhotoLab.Infrastructure;

/// <summary>Optional metadata from the raw-identify helper in the same LibRaw installation.</summary>
public static class RawMetadataReader
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);
    private const string CAMERA_PREFIX = "Normalized Make/Model: =";

    public static async Task<RawMetadata> ReadAsync(string executable, string sourcePath, RawMetadata metadata,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        sourcePath = Path.GetFullPath(sourcePath);
        try
        {
            var result = await LibRawProcess.RunAsync(executable, ["-v", sourcePath], _timeout, cancellation);
            if (result.ExitCode != 0)
                return metadata with { MetadataWarning = "raw-identify failed to read camera metadata." };
            return Parse(result.Output, metadata, sourcePath);
        }
        catch (Exception error) when (error is Win32Exception or IOException or TimeoutException)
        {
            return metadata with { MetadataWarning = "Camera metadata unavailable: " + error.Message };
        }
    }

    public static RawMetadata Parse(string output, RawMetadata metadata, string? sourcePath = null)
    {
        // Native output echoes the path verbatim. Remove it before parsing: Unix filenames may contain newlines.
        if (sourcePath != null) output = output.Replace("Filename: " + sourcePath, "", StringComparison.Ordinal);
        // Normalized fields come from LibRaw's decoded camera identity, not the filename or Camera: label.
        foreach (string line in output.Split('\n'))
        {
            if (line.StartsWith("ISO speed: ", StringComparison.Ordinal))
                metadata = metadata with { IsoSpeed = PositiveNumber(line[11..].Trim()) };
            else if (line.StartsWith("Aperture: f/", StringComparison.Ordinal))
                metadata = metadata with { Aperture = PositiveNumber(line[12..].Trim()) };
            else if (line.StartsWith("Shutter: ", StringComparison.Ordinal))
            {
                string shutter = line[9..].Trim();
                if (shutter.EndsWith(" sec", StringComparison.Ordinal)) shutter = shutter[..^4];
                double? seconds = PositiveNumber(shutter);
                if (shutter.StartsWith("1/", StringComparison.Ordinal))
                {
                    double? denominator = PositiveNumber(shutter[2..]);
                    seconds = denominator.HasValue ? 1 / denominator.Value : null;
                    if (seconds.HasValue && !double.IsFinite(seconds.Value)) seconds = null;
                }
                metadata = metadata with { ExposureSeconds = seconds };
            }
            if (!line.StartsWith(CAMERA_PREFIX, StringComparison.Ordinal)) continue;
            int end = line.IndexOf("= CamMaker ID:", CAMERA_PREFIX.Length, StringComparison.Ordinal);
            int separator = line.IndexOf('/', CAMERA_PREFIX.Length);
            if (end <= separator || separator <= CAMERA_PREFIX.Length) continue;
            string make = line[CAMERA_PREFIX.Length..separator].Trim();
            string model = line[(separator + 1)..end].Trim();
            if (make.Length > 0 && model.Length > 0)
                metadata = metadata with { CameraMake = make, CameraModel = model, MetadataWarning = null };
        }
        if (metadata.CameraMake != null && metadata.CameraModel != null) return metadata;
        return metadata with { MetadataWarning = "raw-identify output has no recognized normalized camera identity." };
    }

    // LibRaw diagnostic output uses a decimal point; zero capture values mean unavailable, not f/0.
    private static double? PositiveNumber(string value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) &&
            double.IsFinite(number) && number > 0 ? number : null;
}
