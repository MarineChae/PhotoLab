using PhotoLab.Infrastructure;
using PhotoLab.Imaging;

namespace PhotoLab.Desktop;

/// <summary>Commits local exports atomically without truncating the original or its hard links.</summary>
public static class PhotoExportStore
{
    /// <summary>Encode on a worker and commit through the same source/link/cancellation guards as byte exports.</summary>
    public static async Task SavePng16Async(string sourcePath, string destinationPath, FloatImageBuffer pixels,
        CancellationToken cancellation = default)
    {
        PhotoFileGuard.EnsureDistinct(sourcePath,destinationPath);
        string library = ColorLibraries.Png16 ?? throw new NotSupportedException("16-bit PNG export is unavailable in this build.");
        await AtomicFileStore.SaveAsync(destinationPath,
            (stream, token) => Task.Run(() => Png16Encoder.Write(pixels,library,stream,token), token),cancellation);
    }

    public static Task SaveAsync(string sourcePath, string destinationPath, byte[] bytes,
        CancellationToken cancellation = default)
    {
        PhotoFileGuard.EnsureDistinct(sourcePath, destinationPath);
        return AtomicFileStore.SaveAsync(destinationPath, bytes, cancellation);
    }
}
