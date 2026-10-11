using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using PhotoLab.Core;
using PhotoLab.Desktop;
using PhotoLab.Imaging;
using PhotoLab.Imaging.Decoding;
using PhotoLab.Infrastructure;
using static EditorUiTests;

/// <summary>Observes actual editor Image updates and headless Skia frames while a mouse drag is held.</summary>
static class EditorFrameTests
{
    public static async Task RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "photolab-frame-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var window = new EditorWindow();
        try
        {
            var bytes = new byte[1800 * 1200 * 4];
            for (int i = 0; i < bytes.Length; i += 4)
            { bytes[i] = bytes[i + 1] = bytes[i + 2] = 100; bytes[i + 3] = 255; }
            var pixels = new PixelBuffer(1800, 1200, bytes);
            string source = Path.Combine(directory, "frame.png");
            await File.WriteAllBytesAsync(source, SkiaImageCodec.Encode(pixels, jpeg: false));
            window.Show(); await window.OpenPhotoAsync(source);
            if (ExpectVulkan) Assert(Find<TextBlock>(window, "Status").Text!.Contains("Vulkan"), "actual editor preview selects the Vulkan processor");
            var slider = Find<Slider>(window, "ToneExposure");
            var image = Find<Image>(window, "PreviewImage");
            var save = Find<Button>(window, "SaveEdits");
            slider.BringIntoView();
            using var initialFrame = window.CaptureRenderedFrame();
            var start = slider.TranslatePoint(new Point(slider.Bounds.Width / 2, slider.Bounds.Height / 2), window)!.Value;
            var center = image.TranslatePoint(new Point(image.Bounds.Width / 2, image.Bounds.Height / 2), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            var duration = Stopwatch.StartNew();
            var latencies = new List<double>();
            var displayed = new HashSet<byte>();
            int observations = 0, changedInputs = 0;
            Bitmap previous = (Bitmap)image.Source!;
            double latestValue = 0;
            for (int i = 0; i < 60; i++)
            {
                double before = slider.Value;
                var latency = Stopwatch.StartNew();
                window.MouseMove(new Point(start.X - 60 + i * 2, start.Y), RawInputModifiers.LeftMouseButton);
                latestValue = slider.Value;
                if (before != latestValue) changedInputs++;
                await Task.Delay(16);
                var current = (Bitmap)image.Source!;
                if (ReferenceEquals(current, previous)) continue;
                using var frame = window.CaptureRenderedFrame() ?? throw new Exception("No Skia frame.");
                byte value = GrayPixel(current, 0, 0);
                Assert(GrayPixel(frame, (int)center.X, (int)center.Y) == value,
                    "captured editor image matches the presented preview during held drag", print: false);
                Assert(current.PixelSize.Width == 600 && current.PixelSize.Height == 400,
                    "drag uses the interactive surface instead of full quality", print: false);
                previous = current; observations++; displayed.Add(value);
                if (before != latestValue) latencies.Add(latency.Elapsed.TotalMilliseconds);
            }
            double seconds = duration.Elapsed.TotalSeconds;
            window.MouseUp(new Point(start.X + 58, start.Y), MouseButton.Left);
            await WaitAsync(() => ((Bitmap)image.Source!).PixelSize.Width == 1800);
            using var linear = ImageConvert.FromSrgb8(pixels);
            using var expected = new FloatImageProcessor().Process(linear, new EditSettings { Exposure = latestValue });
            byte expectedGray = ImageConvert.ToSrgb8(expected).Data[0];
            Assert(observations > 1 && displayed.Count > 1, "editor displays multiple changed Skia image frames before pointer release");
            Assert(GrayPixel((Bitmap)image.Source!, 0, 0) == expectedGray,
                "idle editor quality presents the final pointer value");
            Click(window, save);
            await WaitAsync(() => Find<TextBlock>(window, "Status").Text!.StartsWith("Edits saved"));
            Assert((await new EditFileStore().LoadStateAsync(source)).Adjustments.Exposure == latestValue,
                "editor saves the final drag state");
            Click(window, Find<Button>(window, "Undo"));
            await WaitAsync(() => GrayPixel((Bitmap)image.Source!, 0, 0) == 100);
            Assert(!Find<Button>(window, "Undo").IsEnabled && Find<Button>(window, "Redo").IsEnabled,
                "one Undo returns the complete tone drag to its initial state");
            Click(window, Find<Button>(window, "Redo"));
            await WaitAsync(() => GrayPixel((Bitmap)image.Source!, 0, 0) == expectedGray);
            Assert(slider.Value == latestValue && !Find<Button>(window, "Redo").IsEnabled,
                "Redo restores the final tone value and rendered pixels");
            latencies.Sort();
            double p95 = latencies.Count == 0 ? double.NaN : latencies[(int)Math.Ceiling(latencies.Count * 0.95) - 1];
            Console.WriteLine($"EDITOR_FRAMES: 1800x1200 source / 600x400 interaction, 60 mouse moves / {changedInputs} changed inputs over {seconds:F3}s, {observations} observed image frames ({observations / seconds:F1}/s), " +
                $"{displayed.Count} distinct gray values, observation p95 {p95:F2}ms; held mouse + Image + captured Skia frame, 16ms polling/capture overhead included, native OS presentation excluded.");
        }
        finally { window.Close(); Directory.Delete(directory, recursive: true); }
    }

    public static async Task RunRawAsync(EditorWindow window, string source, CameraWhiteBalance asShot, byte[] originalPixels)
    {
        var slider = Find<Slider>(window, "RawWarmthSlider");
        var image = Find<Image>(window, "PreviewImage");
        var save = Find<Button>(window, "SaveEdits");
        slider.BringIntoView();
        using var initial = window.CaptureRenderedFrame();
        var start = slider.TranslatePoint(new Point(slider.Bounds.Width / 2, slider.Bounds.Height / 2), window)!.Value;
        var center = image.TranslatePoint(new Point(image.Bounds.Width / 2, image.Bounds.Height / 2), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        Bitmap previous = (Bitmap)image.Source!;
        int changedInputs = 0, observations = 0;
        var regionHashes = new HashSet<string>();
        var gaps = new List<double>();
        var duration = Stopwatch.StartNew();
        double previousMs = 0, latestValue = 0;
        for (int i = 0; i < 60; i++)
        {
            double before = slider.Value;
            window.MouseMove(new Point(start.X - 60 + i * 2, start.Y), RawInputModifiers.LeftMouseButton);
            latestValue = slider.Value;
            if (before != latestValue) changedInputs++;
            await Task.Delay(16);
            if (latestValue == 0)
            {
                await WaitAsync(() => save.IsEnabled && !Find<TextBlock>(window, "CaptureInfo").Text!.Contains("Custom WB"));
                var cachedBitmap = (Bitmap)image.Source!;
                Assert(cachedBitmap.PixelSize.Width <= 600 && cachedBitmap.PixelSize.Height <= 600,
                    "continuous WB zero uses a cached as-shot interactive frame: " + Path.GetFileName(source));
            }
            var current = (Bitmap)image.Source!;
            if (ReferenceEquals(current, previous)) continue;
            Assert(current.PixelSize.Width <= 600 && current.PixelSize.Height <= 600,
                "continuous WB keeps interactive frames while held", print: false);
            using var frame = window.CaptureRenderedFrame() ?? throw new Exception("No Skia frame.");
            regionHashes.Add(PhotoRegionHash(frame, (int)center.X, (int)center.Y));
            double now = duration.Elapsed.TotalMilliseconds;
            if (observations > 0) gaps.Add(now - previousMs);
            previousMs = now; previous = current; observations++;
        }
        double seconds = duration.Elapsed.TotalSeconds;
        window.MouseUp(new Point(start.X + 58, start.Y), MouseButton.Left);
        await WaitAsync(() => save.IsEnabled);
        Assert(observations > 1 && regionHashes.Count > 1,
            "continuous actual RAW WB changes multiple captured photo-region frames: " + Path.GetFileName(source));
        using var reference = await new LibRawDecoder().DevelopAsync(source, CameraWhiteBalanceAdjust.Apply(asShot, latestValue, 0));
        using var preview = ImageResize.Preview(reference.Pixels, 1800);
        await WaitAsync(() => ((Bitmap)image.Source!).PixelSize.Width == preview.Width && ((Bitmap)image.Source!).PixelSize.Height == preview.Height);
        Assert(ImagePixels(image).SequenceEqual(ImageConvert.ToSrgb8(preview).Data),
            "continuous actual RAW WB final quality matches the latest CLI reference: " + Path.GetFileName(source));
        Click(window, Find<Button>(window, "Undo"));
        await WaitAsync(() => save.IsEnabled && !Find<TextBlock>(window, "CaptureInfo").Text!.Contains("Custom WB"));
        Assert(ImagePixels(image).SequenceEqual(originalPixels) && !Find<Button>(window, "Undo").IsEnabled,
            "continuous actual RAW WB has one complete Undo: " + Path.GetFileName(source));
        // End a second drag exactly at zero to exercise the camera-default idle quality branch.
        slider.BringIntoView();
        using var zeroStart = window.CaptureRenderedFrame();
        start = slider.TranslatePoint(new Point(slider.Bounds.Width / 2, slider.Bounds.Height / 2), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X + 10, start.Y), RawInputModifiers.LeftMouseButton);
        await WaitAsync(() => save.IsEnabled && Find<TextBlock>(window, "CaptureInfo").Text!.Contains("Custom WB"));
        window.MouseMove(start, RawInputModifiers.LeftMouseButton);
        window.MouseUp(start, MouseButton.Left);
        await WaitAsync(() => save.IsEnabled && ((Bitmap)image.Source!).PixelSize.Width > 600);
        Assert(ImagePixels(image).SequenceEqual(originalPixels) && !Find<TextBlock>(window, "CaptureInfo").Text!.Contains("Custom WB"),
            "WB drag ending at zero upgrades to camera-default CLI quality: " + Path.GetFileName(source));
        gaps.Sort();
        double p95 = gaps.Count == 0 ? double.NaN : gaps[(int)Math.Ceiling(gaps.Count * 0.95) - 1];
        Console.WriteLine($"RAW_EDITOR_FRAMES: {Path.GetFileName(source)}, warm unpack cache, 60 mouse moves / {changedInputs} changed inputs over {seconds:F3}s, " +
            $"{observations} observed photo frames ({observations / seconds:F1}/s), {regionHashes.Count} distinct region hashes, observed inter-frame gap p95 {p95:F2}ms; " +
            "16ms polling/capture included, intermediate WB requests coalesced, native OS presentation excluded.");
    }

    private static string PhotoRegionHash(Bitmap bitmap, int x, int y)
    {
        var bytes = new byte[32 * 32 * 4];
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(x - 16, y - 16, 32, 32), pinned.AddrOfPinnedObject(), bytes.Length, 32 * 4); }
        finally { pinned.Free(); }
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static byte GrayPixel(Bitmap bitmap, int x, int y)
    {
        var bytes = new byte[4];
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(x, y, 1, 1), pinned.AddrOfPinnedObject(), 4, 4); }
        finally { pinned.Free(); }
        if (bytes[0] != bytes[1] || bytes[1] != bytes[2] || bytes[3] != 255)
            throw new Exception("Expected an opaque grayscale pixel.");
        return bytes[0];
    }

    private static void Assert(bool condition, string message, bool print = true)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        if (print) Console.WriteLine("PASS: " + message);
    }
}
