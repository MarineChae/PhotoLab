namespace PhotoLab.Infrastructure;

/// <summary>Lightweight cache invalidation for normal external file edits; not a cryptographic identity.</summary>
public sealed record PhotoSourceStamp(string Path, long Length, DateTime LastWriteUtc)
{
    public static PhotoSourceStamp Read(string path)
    {
        var file = new FileInfo(System.IO.Path.GetFullPath(path));
        return new PhotoSourceStamp(file.FullName, file.Length, file.LastWriteTimeUtc);
    }
}
