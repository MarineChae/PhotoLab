using PhotoLab.Core;

namespace PhotoLab.Imaging;

/// <summary>Produces an owned linear RGBA32F result without modifying the source.</summary>
public interface IFloatImageProcessor
{
    string Name { get; }
    FloatImageBuffer Process(FloatImageBuffer source, EditSettings edit, CancellationToken cancellation = default);
}
