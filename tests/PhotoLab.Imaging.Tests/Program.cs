using PhotoLab.Core;
using PhotoLab.Imaging;
using PhotoLab.Imaging.Decoding;

if (args.Contains("--rgb-preview-benchmark"))
{
    RgbPreviewTests.Benchmark();
    return;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

static void Reject(Action action, string message)
{
    try { action(); }
    catch (ArgumentException) { Console.WriteLine("PASS: " + message); return; }
    throw new Exception("FAIL: " + message);
}

using var source = FloatImageBuffer.FromLinearRgba16(2, 1, [32768, 32769, 65535, 32768, 1, 2, 3, 65535]);
Assert(source.Pixels[1] > source.Pixels[0] && source.Pixels[4] > 0, "Adjacent 16-bit samples remain distinct");
Assert(source.Stride == 8 && source.StrideBytes == 32 && source.PixelFormat == "RGBA32F" && source.ColorSpace == "Linear sRGB D65",
    "Buffer identifies stride, precision and linear working color space");
var processor = new FloatImageProcessor();
using var neutral = processor.Process(source, new EditSettings());
Assert(neutral.Pixels.SequenceEqual(source.Pixels), "Neutral edits preserve every float sample exactly");
using var exposed = processor.Process(source, new EditSettings { Exposure = 2 });
Assert(exposed.Pixels[2] > 1 && Math.Abs(exposed.Pixels[4] - source.Pixels[4] * 4) < 1e-7,
    "Exposure retains values above display white and sub-8-bit detail");
Assert(exposed.Pixels[3] == source.Pixels[3], "Float processing preserves alpha");
using var recovered = processor.Process(exposed, new EditSettings { Exposure = -2 });
Assert(Math.Abs(recovered.Pixels[2] - source.Pixels[2]) < 1e-6, "Exposure recovery survives without intermediate clipping");
Assert(source.Pixels[2] == 1 && source.Pixels[4] == 1 / 65535f, "Float processing leaves source untouched");
var bytes = new byte[256 * 4];
for (int i = 0; i < 256; i++) { bytes[i * 4] = bytes[i * 4 + 1] = bytes[i * 4 + 2] = (byte)i; bytes[i * 4 + 3] = (byte)i; }
var srgb = new PixelBuffer(256, 1, bytes);
using var linear = ImageConvert.FromSrgb8(srgb);
Assert(ImageConvert.ToSrgb8(linear).Data.SequenceEqual(bytes), "All 256 sRGB and alpha values round-trip exactly");
var baseline = new CpuImageProcessor();
var settings = new EditSettings { Exposure = 0.75, Contrast = 10, Highlights = -20, Shadows = 15,
    Temperature = 10, Tint = -5, Saturation = 20, Vibrance = 15 };
using var adjusted = processor.Process(linear, settings);
Assert(ImageConvert.ToSrgb8(adjusted).Data.SequenceEqual(baseline.Process(srgb, settings).Data),
    "Float pipeline matches existing basic adjustments at the RGBA8 display boundary");
Reject(() => FloatImageBuffer.FromLinearRgba32(1, 1, [float.NaN, 0, 0, 1]), "Non-finite input samples rejected");
Reject(() => FloatImageBuffer.FromLinearRgba32(1, 1, [0, 0, 0, 2]), "Invalid alpha rejected");
Reject(() => FloatImageBuffer.FromLinearRgba16(2, 1, [0, 0, 0, 0]), "Mismatched dimensions rejected");
Reject(() => processor.Process(source, new EditSettings { Exposure = double.PositiveInfinity }), "Non-finite settings rejected");
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try { processor.Process(source, new EditSettings(), cancelled.Token); throw new Exception("FAIL: cancelled processing succeeded"); }
catch (OperationCanceledException) { Console.WriteLine("PASS: cancellation checked before neutral fast path"); }
var disposed = FloatImageBuffer.FromLinearRgba32(1, 1, [0, 0, 0, 1]);
disposed.Dispose(); disposed.Dispose();
try { _ = disposed.Pixels.Length; throw new Exception("FAIL: disposed buffer remained accessible"); }
catch (ObjectDisposedException) { Console.WriteLine("PASS: disposed pooled buffer inaccessible and double disposal safe"); }
Assert(neutral.Pixels[0] == source.Pixels[0], "Separate processed buffer remains valid after other buffers are disposed");

string ppmPath = Path.Combine(Path.GetTempPath(), "photolab-linear-" + Guid.NewGuid().ToString("N") + ".ppm");
try
{
    File.WriteAllBytes(ppmPath, [..System.Text.Encoding.ASCII.GetBytes("P6\r\n# linear fixture\r\n1 1\r\n65535\r\n"),
        0x80, 0x00, 0x80, 0x01, 0xFF, 0xFF]);
    using var decoded = LinearPpmDecoder.Read(ppmPath);
    Assert(decoded.Pixels[0] == 32768 / 65535f && decoded.Pixels[1] > decoded.Pixels[0] && decoded.Pixels[2] == 1,
        "Linear PPM retains adjacent 16-bit big-endian samples");
    File.WriteAllBytes(ppmPath, [..System.Text.Encoding.ASCII.GetBytes("P6\n1 1\n65535\n"), 32, 35, 0, 0, 0, 0]);
    using var binary = LinearPpmDecoder.Read(ppmPath);
    Assert(binary.Pixels[0] == 0x2023 / 65535f, "Binary sample bytes resembling header characters preserved");
    File.WriteAllBytes(ppmPath, [..System.Text.Encoding.ASCII.GetBytes("P6\n1 1\n65535\n"), 0]);
    try { LinearPpmDecoder.Read(ppmPath); throw new Exception("FAIL: truncated PPM accepted"); }
    catch (EndOfStreamException) { Console.WriteLine("PASS: truncated PPM rejected"); }
    File.WriteAllBytes(ppmPath, System.Text.Encoding.ASCII.GetBytes("P6\n1 1\n255\n"));
    try { LinearPpmDecoder.Read(ppmPath); throw new Exception("FAIL: 8-bit RAW intermediate accepted"); }
    catch (InvalidDataException) { Console.WriteLine("PASS: low-precision RAW intermediate rejected"); }
}
finally { File.Delete(ppmPath); }

using var blackWhite = FloatImageBuffer.FromLinearRgba32(2, 1, [0, 0, 0, 1, 1, 1, 1, 1]);
using var average = ImageResize.Preview(blackWhite, 1);
Assert(average.Pixels[0] == 0.5f && ImageConvert.ToSrgb8(average).Data[0] == 188,
    "Preview averages in linear light rather than gamma-encoded values");
using var transparent = FloatImageBuffer.FromLinearRgba32(2, 1, [1, 0, 0, 0, 0, 0, 1, 1]);
using var transparentAverage = ImageResize.Preview(transparent, 1);
Assert(transparentAverage.Pixels.SequenceEqual(new float[] { 0, 0, 1, 0.5f }),
    "Transparent pixels do not bleed hidden colors into previews");
using var sameSize = ImageResize.Preview(transparent, 2);
Assert(sameSize.Pixels.SequenceEqual(transparent.Pixels), "Same-size preview preserves all samples including hidden RGB");
using var extreme = FloatImageBuffer.FromLinearRgba32(1, 1, [float.MaxValue, float.MaxValue, float.MaxValue, 1]);
try { processor.Process(extreme, new EditSettings { Exposure = 4 }); throw new Exception("FAIL: overflowing float result accepted"); }
catch (ArithmeticException) { Console.WriteLine("PASS: float overflow rejected without returning invalid pixels"); }

using var middleGray = FloatImageBuffer.FromLinearRgba32(1, 1, [0.25f, 0.25f, 0.25f, 0.75f]);
using var orderedContrast = processor.Process(middleGray, new EditSettings { Exposure = 1, Contrast = 100 });
Assert(Math.Abs(orderedContrast.Pixels[0] - 0.724f) < 1e-6 && orderedContrast.Pixels[3] == 0.75f,
    "Exposure precedes contrast about the 0.18 pivot");
using var dimGray = FloatImageBuffer.FromLinearRgba32(1, 1, [0.1f, 0.1f, 0.1f, 1]);
using var orderedHighlights = processor.Process(dimGray, new EditSettings { Exposure = 4, Highlights = -100 });
Assert(Math.Abs(orderedHighlights.Pixels[0] - 0.56f) < 1e-6, "Highlight mask uses luminance after exposure");
bool deterministic = true;
for (int repeat = 0; repeat < 10; repeat++)
{
    using var repeated = processor.Process(linear, settings);
    deterministic &= repeated.Pixels.SequenceEqual(adjusted.Pixels);
}
Assert(deterministic, "Ten repeated pipeline executions are float-deterministic");
var ownedPixels = FloatImageBuffer.FromLinearRgba32(1, 1, [0, 0, 0, 1]);
var rawResult = new RawImage(ownedPixels, new RawMetadata(1, 1, ".dng"));
using var transferred = rawResult.TakePixels();
rawResult.Dispose();
Assert(transferred.Pixels.Length == 4, "RAW result ownership transfer survives wrapper disposal");
try { rawResult.TakePixels(); throw new Exception("FAIL: RAW pixels transferred twice"); }
catch (ObjectDisposedException) { Console.WriteLine("PASS: RAW ownership can transfer only once"); }
var disposedPixels = FloatImageBuffer.FromLinearRgba32(1, 1, [0, 0, 0, 1]);
var disposedRaw = new RawImage(disposedPixels, new RawMetadata(1, 1, ".nef"));
disposedRaw.Dispose();
try { _ = disposedPixels.Pixels.Length; throw new Exception("FAIL: RAW pixels not disposed"); }
catch (ObjectDisposedException) { Console.WriteLine("PASS: disposing RAW result releases owned pixels"); }
var matrixValues = new float[] { 1.8f, -0.7f, -0.1f, 0, 0, 1, 0, 0, 0, 0, 1, 0 };
var colorInfo = new RawColorInfo(new CameraWhiteBalance(2, 1, 1.5f, 0), matrixValues, "fixture");
matrixValues[0] = 99;
Assert(colorInfo.CameraToSrgb[0] == 1.8f && colorInfo.AsShot.Channel3 == 0,
    "Camera matrix owns its values and absent fourth WB channel is preserved");
try { _ = new RawColorInfo(colorInfo.AsShot, new float[9], "fixture"); throw new Exception("FAIL: wrong matrix dimensions accepted"); }
catch (ArgumentException) { Console.WriteLine("PASS: camera matrix requires three rows and four columns"); }
matrixValues[0] = float.NaN;
try { _ = new RawColorInfo(colorInfo.AsShot, matrixValues, "fixture"); throw new Exception("FAIL: NaN camera matrix accepted"); }
catch (ArgumentException) { Console.WriteLine("PASS: non-finite camera matrix rejected"); }
try { _ = new RawColorInfo(new CameraWhiteBalance(1, 0, 1, 0), new float[12], "fixture"); throw new Exception("FAIL: invalid WB accepted"); }
catch (ArgumentException) { Console.WriteLine("PASS: missing primary WB coefficient rejected"); }
var normalizedWb = new CameraWhiteBalance(0.0000002f, 0.0000001f, 0.00000015f, 0).Normalize();
Assert(normalizedWb.Channel0 == 2 && normalizedWb.Channel1 == 1 &&
    Math.Abs(normalizedWb.Channel2 - 1.5f) < 1e-6 && normalizedWb.Channel3 == 0,
    "Camera WB normalization preserves ratios and absent fourth channel at tiny common scales");
Assert(new CameraWhiteBalance(4, 2, 3, 1).Normalize() == new CameraWhiteBalance(4, 2, 3, 1),
    "Camera WB normalization includes the fourth physical channel");
Reject(() => new CameraWhiteBalance(float.MaxValue, float.Epsilon, 1, 0).Normalize(),
    "Unrepresentable camera WB ratios rejected before native processing");
Reject(() => new CameraWhiteBalance(1, 1, 1, -1).Normalize(), "Negative fourth camera WB channel rejected");
Reject(() => new CameraWhiteBalance(float.PositiveInfinity, 1, 1, 0).Normalize(), "Infinite camera WB input rejected");
var wbBasis = new CameraWhiteBalance(2, 1, 1.5f, 1);
var warmWb = CameraWhiteBalanceAdjust.Apply(wbBasis, 100, 0);
Assert(Math.Abs(warmWb.Channel0 / wbBasis.Channel0 - Math.Pow(2, 0.32)) < 1e-6 &&
    Math.Abs(warmWb.Channel2 / wbBasis.Channel2 - Math.Pow(2, -0.32)) < 1e-6,
    "Positive camera warmth increases red and decreases blue before color conversion");
var tintedWb = CameraWhiteBalanceAdjust.Apply(wbBasis, 0, 100);
Assert(Math.Abs((tintedWb.Channel0 / tintedWb.Channel1) / (wbBasis.Channel0 / wbBasis.Channel1) - Math.Pow(2, 0.28)) < 1e-6 &&
    tintedWb.Channel3 == tintedWb.Channel1,
    "Camera tint raises red/blue relative to both physical green channels");
bool wbOffsetsRoundTrip = true;
foreach (double warmth in new double[] { -100, 0, 100 })
    foreach (double tint in new double[] { -100, 0, 100 })
    {
        var wb = CameraWhiteBalanceAdjust.Apply(wbBasis, warmth, tint);
        wbOffsetsRoundTrip &= CameraWhiteBalanceAdjust.TryGetOffsets(wbBasis, wb, out double restoredWarmth, out double restoredTint) &&
            Math.Abs(restoredWarmth - warmth) < 0.0001 && Math.Abs(restoredTint - tint) < 0.0001;
    }
Assert(wbOffsetsRoundTrip, "Camera WB control endpoints restore from saved channel ratios");
Assert(CameraWhiteBalanceAdjust.TryGetOffsets(wbBasis, null, out double defaultWarmth, out double defaultTint) &&
    defaultWarmth == 0 && defaultTint == 0, "As-shot mode restores zero relative WB controls");
Assert(!CameraWhiteBalanceAdjust.TryGetOffsets(wbBasis, new CameraWhiteBalance(2, 1, 1.5f, 0), out _, out _),
    "Unrepresentable fourth-channel WB is never silently mapped to relative controls");
Assert(!CameraWhiteBalanceAdjust.TryGetOffsets(wbBasis, new CameraWhiteBalance(200, 1, 1.5f, 1), out _, out _),
    "Out-of-range saved WB remains custom instead of silently clamping controls");
Assert(CameraWhiteBalanceAdjust.Apply(new CameraWhiteBalance(2, 1, 1.5f, 0), 50, 20).Channel3 == 0,
    "Relative camera WB leaves absent fourth channel absent");
Reject(() => CameraWhiteBalanceAdjust.Apply(wbBasis, 101, 0), "Out-of-range camera warmth rejected");
Reject(() => CameraWhiteBalanceAdjust.Apply(wbBasis, 0, double.NaN), "Non-finite camera tint rejected");
using var nativeRgb = FloatImageBuffer.FromLinearRgb16(2, 1, [32768, 32769, 65535, 0, 1, 2]);
Assert(nativeRgb.Pixels.SequenceEqual(new float[] { 32768 / 65535f, 32769 / 65535f, 1, 1, 0, 1 / 65535f, 2 / 65535f, 1 }),
    "Native RGB16 expands to opaque linear float without an 8-bit or RGBA16 intermediate");
Reject(() => FloatImageBuffer.FromLinearRgb16(1, 1, [1, 2]), "Native RGB16 dimensions validated");
try { FloatImageBuffer.FromLinearRgb16(1, 1, [1, 2, 3], cancelled.Token); throw new Exception("FAIL: cancelled RGB expansion succeeded"); }
catch (OperationCanceledException) { Console.WriteLine("PASS: native RGB16 expansion honors pre-cancellation"); }
RgbPreviewTests.Run();
HistogramTests.Run();
Console.WriteLine("All imaging tests passed.");
