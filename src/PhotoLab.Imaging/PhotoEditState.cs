using PhotoLab.Core;
using PhotoLab.Imaging.Decoding;

namespace PhotoLab.Imaging;

/// <summary>Non-destructive tone edits plus optional camera-space WB, separate from the unchanged Core contract.</summary>
public sealed record PhotoEditState(EditSettings Adjustments, CameraWhiteBalance? WhiteBalance = null);
