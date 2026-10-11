using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PhotoLab.Core;

namespace PhotoLab.Desktop;

/// <summary>Copies display sRGB RGBA8 into an immutable Skia bitmap without a PNG round-trip.</summary>
public static class PreviewBitmap
{
    public static Bitmap Create(PixelBuffer pixels)
    {
        var pinned = GCHandle.Alloc(pixels.Data, GCHandleType.Pinned);
        try
        {
            // Avalonia's Skia backend copies the bytes during construction; the pin ends before returning.
            return new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Unpremul, pinned.AddrOfPinnedObject(),
                new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), checked(pixels.Width * 4));
        }
        finally { pinned.Free(); }
    }
}
