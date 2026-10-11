namespace PhotoLab.Infrastructure;

/// <summary>Writes beside the destination and replaces its directory entry only after a complete write.</summary>
public static class AtomicFileStore
{
    public static Task SaveAsync(string destinationPath, byte[] bytes, CancellationToken cancellation = default)
        => SaveAsync(destinationPath, async (stream, token) => await stream.WriteAsync(bytes, token), cancellation);

    /// <summary>Stream a complete result into the temporary file before committing the destination entry.</summary>
    public static async Task SaveAsync(string destinationPath, Func<Stream, CancellationToken, Task> write,
        CancellationToken cancellation = default)
    {
        string path = Path.GetFullPath(destinationPath);
        string temporary = Path.Combine(Path.GetDirectoryName(path)!, ".photolab-write-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 65536, FileOptions.Asynchronous))
            {
                await write(file, cancellation);
                await file.FlushAsync(cancellation);
            }
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
