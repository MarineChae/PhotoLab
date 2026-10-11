namespace PhotoLab.Core;

/// <summary>
/// File extensions used for discovery and routing only. An extension does not prove
/// that a particular camera model or compression scheme is supported by LibRaw.
/// </summary>
public static class PhotoFormats
{
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png"
    };

    private static readonly HashSet<string> CameraRaw = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cr2", ".cr3", ".crw",   // Canon
        ".nef", ".nrw",             // Nikon
        ".arw", ".srf", ".sr2",   // Sony
        ".raf",                       // Fujifilm
        ".orf",                       // Olympus / OM System
        ".rw2", ".raw",             // Panasonic and other makers
        ".dng",                       // Adobe / smartphones / other cameras
        ".pef", ".ptx",             // Pentax
        ".rwl",                       // Leica
        ".srw",                       // Samsung
        ".3fr", ".fff",             // Hasselblad
        ".iiq", ".eip",             // Phase One
        ".erf",                       // Epson
        ".kdc", ".dcr",             // Kodak
        ".mrw",                       // Minolta
        ".mos",                       // Leaf
        ".x3f"                        // Sigma
    };

    public static bool IsRaw(string path) => CameraRaw.Contains(Path.GetExtension(path));
    public static bool IsStandardImage(string path) => Common.Contains(Path.GetExtension(path));
    public static bool CanOpen(string path) => IsRaw(path) || IsStandardImage(path);

    public static string[] StandardPatterns => Common.Select(ext => "*" + ext).OrderBy(s => s).ToArray();
    public static string[] RawPatterns => CameraRaw.Select(ext => "*" + ext).OrderBy(s => s).ToArray();
    public static string[] AllPatterns => StandardPatterns.Concat(RawPatterns).ToArray();
}
