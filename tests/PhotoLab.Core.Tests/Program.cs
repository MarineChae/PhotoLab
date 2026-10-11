using PhotoLab.Core;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

var processor = new CpuImageProcessor();
var original = new PixelBuffer(2, 1, [64, 80, 100, 255, 200, 100, 80, 127]);
var noOp = processor.Process(original, new EditSettings());
Assert(noOp.Data.SequenceEqual(original.Data), "Zero settings preserve image bytes");
Assert(!ReferenceEquals(noOp.Data, original.Data), "Processing does not alias input buffer");
var lighter = processor.Process(original, new EditSettings { Exposure = 1 });
Assert(lighter.Data[0] > original.Data[0], "Positive exposure brightens non-clipped pixels");
Assert(lighter.Data[3] == 255 && lighter.Data[7] == 127, "Alpha is preserved");
Assert(original.Data[0] == 64, "Original input is never modified");
var mono = processor.Process(original, new EditSettings { Saturation = -100 });
Assert(Math.Abs(mono.Data[0] - mono.Data[1]) <= 1 && Math.Abs(mono.Data[1] - mono.Data[2]) <= 1,
       "Desaturation creates grayscale pixel");
var hist = Histogram.Compute(original);
Assert(hist.red[64] == 1 && hist.green[100] == 1, "Histogram returns channel counts");
var path = Path.Combine(Path.GetTempPath(), "photolab-test-" + Guid.NewGuid().ToString("N") + ".jpg");
try
{
    var store = new SidecarStore();
    var edits = new EditSettings { Exposure = 0.75, Temperature = 15 };
    await store.SaveAsync(path, edits);
    var reloaded = await store.LoadAsync(path);
    Assert(edits == reloaded, "Sidecar JSON round-trip");
}
finally { if (File.Exists(SidecarStore.GetPath(path))) File.Delete(SidecarStore.GetPath(path)); }
Assert(PhotoFormats.IsRaw("Nikon.NEF"), "Nikon NEF routes to shared RAW backend");
Assert(PhotoFormats.IsRaw("Sony.ARW") && PhotoFormats.IsRaw("Fuji.RAF") && PhotoFormats.IsRaw("iPhone.DNG"),
       "Sony/Fuji/DNG extensions route to shared RAW backend");
Assert(!PhotoFormats.IsRaw("portrait.jpg") && PhotoFormats.IsStandardImage("portrait.JPG"),
       "JPEG remains on the standard decoder path");
Assert(!PhotoFormats.CanOpen("random.pdf"), "Other extensions are not advertised");

// P6: 8-bit RGB, first pixel begins with whitespace/# byte values. Do not
// accidentally skip these bytes after reading the PPM header.
using (var ppm8 = new MemoryStream([..System.Text.Encoding.ASCII.GetBytes("P6\n# comment\n2 1\n255\n"),
                                    32, 35, 0, 255, 10, 50]))
{
    var decoded = PpmImageCodec.Read(ppm8);
    Assert(decoded.Width == 2 && decoded.Height == 1, "PPM 8-bit dimensions");
    Assert(decoded.Data.SequenceEqual(new byte[] { 32, 35, 0, 255, 255, 10, 50, 255 }),
           "PPM 8-bit pixel values, including header-like bytes, are preserved");
}
// P6: 16-bit big-endian samples 0, 32768, 65535.
using (var ppm16 = new MemoryStream([..System.Text.Encoding.ASCII.GetBytes("P6\n1 1\n65535\n"),
                                     0, 0, 0x80, 0, 0xFF, 0xFF]))
{
    var decoded = PpmImageCodec.Read(ppm16);
    Assert(decoded.Data.SequenceEqual(new byte[] { 0, 128, 255, 255 }),
           "PPM 16-bit big-endian channel values are converted to RGBA8");
}
// Keep the PGM stream name distinct from the desaturated image above.
using (var pgm = new MemoryStream([..System.Text.Encoding.ASCII.GetBytes("P5\n1 1\n255\n"), 76]))
{
    Assert(PpmImageCodec.Read(pgm).Data.SequenceEqual(new byte[] { 76, 76, 76, 255 }),
           "Grayscale PGM fallback is supported");
}
Console.WriteLine("All core tests passed.");
