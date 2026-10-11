namespace PhotoLab.Infrastructure;

/// <summary>Protects the source path, including symbolic links and aliased parent directories.</summary>
public static class PhotoFileGuard
{
    public static void EnsureDistinct(string sourcePath, string destinationPath)
    {
        string source = ResolvePath(sourcePath);
        string destination = ResolvePath(destinationPath);
        // Conservatively protect case-insensitive Windows/macOS volumes.
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(source, destination, comparison))
            throw new IOException("Choose a different output filename to preserve the original photograph.");
    }

    private static string ResolvePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string current = Path.GetPathRoot(fullPath)!;
        foreach (string part in fullPath[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            // A link target can itself use an aliased parent (for example /var on macOS).
            if (entry.LinkTarget != null) current = ResolvePath(entry.ResolveLinkTarget(returnFinalTarget: true)!.FullName);
        }
        return current;
    }
}
