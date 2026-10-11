using PhotoLab.Core;

namespace PhotoLab.Infrastructure;

/// <summary>Read-only discovery entry; an extension identifies a candidate, not decoder/camera compatibility.</summary>
public sealed record FolderPhoto(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public override string ToString() => Name;
}

public static class PhotoFolder
{
    /// <summary>Enumerate only the selected directory on a worker; decoding is deferred until selection.</summary>
    public static Task<IReadOnlyList<FolderPhoto>> ScanAsync(string directory, CancellationToken cancellation = default)
        => Task.Run<IReadOnlyList<FolderPhoto>>(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            string path = Path.GetFullPath(directory);
            var photos = new List<FolderPhoto>();
            foreach (string file in Directory.EnumerateFiles(path))
            {
                cancellation.ThrowIfCancellationRequested();
                if (PhotoFormats.CanOpen(file)) photos.Add(new FolderPhoto(file));
            }
            photos.Sort((a,b) =>
            {
                int order = StringComparer.OrdinalIgnoreCase.Compare(a.Name,b.Name);
                return order == 0 ? StringComparer.Ordinal.Compare(a.Name,b.Name) : order;
            });
            cancellation.ThrowIfCancellationRequested();
            return photos;
        },cancellation);
}
