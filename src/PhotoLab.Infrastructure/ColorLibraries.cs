namespace PhotoLab.Infrastructure;

/// <summary>Explicit developer overrides take priority over the native libraries shipped beside the app.</summary>
public static class ColorLibraries
{
    public static string? LittleCms => Find("PHOTOLAB_LCMS_LIBRARY", OperatingSystem.IsWindows() ? "lcms2.dll" :
        OperatingSystem.IsMacOS() ? "liblcms2.dylib" : "liblcms2.so");

    public static string? Png16 => Find("PHOTOLAB_PNG16_BRIDGE", OperatingSystem.IsWindows() ? "PhotoLab.NativePng.dll" :
        OperatingSystem.IsMacOS() ? "libPhotoLab.NativePng.dylib" : "libPhotoLab.NativePng.so");

    private static string? Find(string variable, string filename)
    {
        string? selected = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(selected)) return selected;
        string bundled = Path.Combine(AppContext.BaseDirectory, filename);
        return File.Exists(bundled) ? bundled : null;
    }
}
