using System.Threading;

namespace PhotoLab.Core;

/// <summary>Stable boundary for future SIMD/GPU processing backends.</summary>
public interface IImageProcessor
{
    PixelBuffer Process(PixelBuffer source, EditSettings edit, CancellationToken cancellation = default);
}
