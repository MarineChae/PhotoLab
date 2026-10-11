using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PhotoLab.Core;
using PhotoLab.Desktop;
using PhotoLab.Imaging;
using PhotoLab.Imaging.Decoding;
using PhotoLab.Infrastructure;

/// <summary>Real control input/routing on a headless UI thread; native RAW correctness is tested separately.</summary>
public static class EditorUiTests
{
    internal static bool ExpectVulkan { get; private set; }

    public static async Task RunVulkanAsync(string library)
    {
        string? previous = Environment.GetEnvironmentVariable("PHOTOLAB_VULKAN_LIBRARY");
        Environment.SetEnvironmentVariable("PHOTOLAB_VULKAN_LIBRARY", Path.GetFullPath(library));
        ExpectVulkan = true;
        try { await RunAsync(); }
        finally { ExpectVulkan = false; Environment.SetEnvironmentVariable("PHOTOLAB_VULKAN_LIBRARY", previous); }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public static async Task RunAsync()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(EditorUiTests));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await session.Dispatch(async () => { PreviewDisplayTests.Check(); await TestAsync(); await TestPresetsAsync(); await TestFolderAsync(); await FastPreviewAsync(); await WbDragAsync(); await EditorFrameTests.RunAsync(); return 0; }, timeout.Token); }
        finally
        {
            // Headless Dispatch completion can inline this continuation on its own worker.
            // Dispose waits for that worker, so it must run elsewhere while this method yields.
            await Task.Run(session.Dispose);
        }
        Console.WriteLine("All headless editor input tests passed.");
    }

    public static async Task RunDisplayBenchmarkAsync()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(EditorUiTests));
        try
        {
            await session.Dispatch(async () => { await PreviewDisplayTests.BenchmarkAsync(); return 0; }, CancellationToken.None);
        }
        finally { await Task.Run(session.Dispose); }
    }

    public static async Task RunNativeAsync(string bridge, string[] sources)
    {
        string? previous = Environment.GetEnvironmentVariable("PHOTOLAB_NATIVE_RAW_BRIDGE");
        Environment.SetEnvironmentVariable("PHOTOLAB_NATIVE_RAW_BRIDGE", Path.GetFullPath(bridge));
        var session = HeadlessUnitTestSession.StartNew(typeof(EditorUiTests));
        try
        {
            await session.Dispatch(async () =>
            {
                foreach (string source in sources)
                {
                    var window = new EditorWindow(); // Exercise the same opt-in decoder construction as the actual app.
                    try
                    {
                        window.Show();
                        await window.OpenPhotoAsync(source);
                        var image = Find<Image>(window, "PreviewImage");
                        var original = Find<ToggleButton>(window, "Original");
                        byte[] originalPixels = ImagePixels(image);
                        Type(window, Find<TextBox>(window, "RawWarmth"), "50");
                        Type(window, Find<TextBox>(window, "RawTint"), "20");
                        Click(window, Find<Button>(window, "ApplyWhiteBalance"));
                        await WaitAsync(() => Find<Button>(window, "SaveEdits").IsEnabled && Find<TextBlock>(window, "CaptureInfo").Text!.Contains("Custom WB"));
                        Check(((Bitmap)image.Source!).PixelSize.Width <= 600 && ((Bitmap)image.Source!).PixelSize.Height <= 600,
                            "native WB first presents an interactive-size actual RAW frame: " + Path.GetFileName(source));
                        using var baseRaw = await new LibRawDecoder().DecodeAsync(source);
                        var wb = CameraWhiteBalanceAdjust.Apply(baseRaw.Metadata.CameraColor!.AsShot, 50, 20);
                        using var expectedRaw = await new LibRawDecoder().DevelopAsync(source, wb);
                        using var expectedPreview = ImageResize.Preview(expectedRaw.Pixels, 1800);
                        await WaitAsync(() => ((Bitmap)image.Source!).PixelSize.Width == expectedPreview.Width && ((Bitmap)image.Source!).PixelSize.Height == expectedPreview.Height);
                        Check(ImagePixels(image).SequenceEqual(ImageConvert.ToSrgb8(expectedPreview).Data),
                            "native opt-in WB control displays CLI-equivalent actual RAW pixels: " + Path.GetFileName(source));
                        Click(window, original);
                        Check(ImagePixels(image).SequenceEqual(originalPixels), "native WB refresh preserves the original comparison bitmap: " + Path.GetFileName(source));
                        Click(window, original);
                        Click(window, Find<Button>(window, "Undo"));
                        await WaitAsync(() => Find<Button>(window, "SaveEdits").IsEnabled && !Find<TextBlock>(window, "CaptureInfo").Text!.Contains("Custom WB"));
                        Check(ImagePixels(image).SequenceEqual(originalPixels), "native WB Undo returns to camera-default actual RAW pixels: " + Path.GetFileName(source));
                        var slider = Find<Slider>(window, "RawWarmthSlider");
                        slider.BringIntoView();
                        using var dragFrame = window.CaptureRenderedFrame();
                        var start = slider.TranslatePoint(new Point(slider.Bounds.Width / 2, slider.Bounds.Height / 2), window)!.Value;
                        window.MouseDown(start, MouseButton.Left);
                        var latency = System.Diagnostics.Stopwatch.StartNew();
                        window.MouseMove(new Point(start.X + 10, start.Y), RawInputModifiers.LeftMouseButton);
                        await WaitAsync(() => Find<Button>(window, "SaveEdits").IsEnabled && Find<TextBlock>(window, "CaptureInfo").Text!.Contains("Custom WB"));
                        Check(!ImagePixels(image).SequenceEqual(originalPixels) && slider.IsEnabled,
                            "native RAW pixels update during held mouse drag: " + Path.GetFileName(source));
                        Console.WriteLine($"HEADLESS_DRAG_FIRST_FRAME: {Path.GetFileName(source)} {latency.Elapsed.TotalMilliseconds:F2}ms (includes input/bitmap, polling; excludes native OS presentation)");
                        window.MouseMove(new Point(start.X + 80, start.Y), RawInputModifiers.LeftMouseButton);
                        double latestValue = slider.Value;
                        window.MouseUp(new Point(start.X + 80, start.Y), MouseButton.Left);
                        await WaitAsync(() => Find<Button>(window, "SaveEdits").IsEnabled);
                        using var dragRaw = await new LibRawDecoder().DevelopAsync(source,
                            CameraWhiteBalanceAdjust.Apply(baseRaw.Metadata.CameraColor!.AsShot, latestValue, 0));
                        using var dragPreview = ImageResize.Preview(dragRaw.Pixels, 1800);
                        await WaitAsync(() => ((Bitmap)image.Source!).PixelSize.Width == dragPreview.Width && ((Bitmap)image.Source!).PixelSize.Height == dragPreview.Height);
                        Check(ImagePixels(image).SequenceEqual(ImageConvert.ToSrgb8(dragPreview).Data),
                            "native drag release upgrades to CLI-equivalent latest WB: " + Path.GetFileName(source));
                        Click(window, Find<Button>(window, "Undo"));
                        await WaitAsync(() => Find<Button>(window, "SaveEdits").IsEnabled && !Find<TextBlock>(window, "CaptureInfo").Text!.Contains("Custom WB"));
                        Check(ImagePixels(image).SequenceEqual(originalPixels),
                            "native complete drag uses one Undo: " + Path.GetFileName(source));
                        await EditorFrameTests.RunRawAsync(window, source, baseRaw.Metadata.CameraColor!.AsShot, originalPixels);
                    }
                    finally { window.Close(); }
                }
                return 0;
            }, CancellationToken.None);
        }
        finally
        {
            await Task.Run(session.Dispose);
            Environment.SetEnvironmentVariable("PHOTOLAB_NATIVE_RAW_BRIDGE", previous);
        }
        Console.WriteLine("All actual-RAW native editor input tests passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: UI " + message);
        Console.WriteLine("PASS: UI " + message);
    }

    internal static T Find<T>(EditorWindow window, string name) where T : Control
        => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);

    internal static void Click(EditorWindow window, Control control)
    {
        control.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new Exception("Control is not attached to the window.");
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static void Type(EditorWindow window, TextBox input, string text)
    {
        input.Focus(); input.SelectAll();
        window.KeyTextInput(text);
    }

    internal static async Task WaitAsync(Func<bool> ready)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!ready())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Headless UI operation did not finish.");
            await Task.Delay(10);
        }
    }

    internal static byte[] ImagePixels(Image image)
    {
        using var png = new MemoryStream();
        ((Bitmap)image.Source!).Save(png);
        using var decoded = SkiaSharp.SKBitmap.Decode(png.ToArray());
        var pixels = new List<byte>();
        for (int y = 0; y < decoded.Height; y++)
            for (int x = 0; x < decoded.Width; x++)
            {
                var color = decoded.GetPixel(x, y);
                pixels.AddRange([color.Red, color.Green, color.Blue, color.Alpha]);
            }
        return pixels.ToArray();
    }

    private static async Task TestFolderAsync()
    {
        string directory=Path.Combine(Path.GetTempPath(),"photolab-folder-ui-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var window=new EditorWindow(new PhotoImageLoader(),new PresetStore(Path.Combine(directory,"presets")));
        try
        {
            foreach(var (name,value) in new[]{("a.png",32),("b.png",64),("c.png",96)})
                await File.WriteAllBytesAsync(Path.Combine(directory,name),SkiaImageCodec.Encode(new PixelBuffer(1,1,new byte[]{(byte)value,40,80,255}),false));
            await File.WriteAllBytesAsync(Path.Combine(directory,"d.png"),new byte[]{1,2,3});
            await File.WriteAllTextAsync(Path.Combine(directory,"ignore.txt"),"Not a photo.");
            window.Show();await window.OpenFolderAsync(directory);
            var list=Find<ListBox>(window,"PhotoList");var nameLabel=Find<TextBlock>(window,"PhotoName");
            var status=Find<TextBlock>(window,"Status");var exposure=Find<Slider>(window,"ToneExposure");
            Check(list.ItemCount==4 && list.SelectedIndex==0 && nameLabel.Text=="a.png","folder opening lists candidates and displays its first actual photo");
            exposure.Value=1;await WaitAsync(()=>status.Text!.StartsWith("Ready"));
            byte[] edited=ImagePixels(Find<Image>(window,"PreviewImage"));
            var next=Find<Button>(window,"NextPhoto");next.BringIntoView();using(var frame=window.CaptureRenderedFrame()) {Click(window,next);}
            await WaitAsync(()=>nameLabel.Text=="b.png" && status.Text!.StartsWith("Ready"));
            Check(list.SelectedIndex==1 && exposure.Value==0,"next-photo mouse input selects and renders the next file with its own settings");
            list.SelectedIndex=0;list.SelectedIndex=2;
            await WaitAsync(()=>nameLabel.Text=="c.png" && status.Text!.StartsWith("Ready"));
            Check(list.SelectedIndex==2,"rapid folder selection presents the latest requested photo");
            next.BringIntoView();using(var frame=window.CaptureRenderedFrame()) {Click(window,next);}
            await WaitAsync(()=>status.Text!.Contains("error",StringComparison.OrdinalIgnoreCase));
            Check(nameLabel.Text=="c.png" && list.SelectedIndex==2,"corrupt candidate keeps accepted image and restores selected file");
            list.SelectedIndex=0;await WaitAsync(()=>nameLabel.Text=="a.png" && status.Text!.StartsWith("Ready"));
            Check(exposure.Value==1 && ImagePixels(Find<Image>(window,"PreviewImage")).SequenceEqual(edited) && Find<Button>(window,"Undo").IsEnabled,
                "returning to a photo restores its unsaved edits and own Undo history");
            Click(window,Find<Button>(window,"Undo"));await WaitAsync(()=>exposure.Value==0 && status.Text!.StartsWith("Ready"));
            Check(!Find<Button>(window,"Undo").IsEnabled && Find<Button>(window,"Redo").IsEnabled,
                "restored photo Undo reaches its own original state and retains Redo");
            list.SelectedIndex=1;await WaitAsync(()=>nameLabel.Text=="b.png" && status.Text!.StartsWith("Ready"));
            Check(!Find<Button>(window,"Undo").IsEnabled && !Find<Button>(window,"Redo").IsEnabled,
                "another photo never inherits a neighbor's undo or redo entries");
            list.SelectedIndex=0;await WaitAsync(()=>nameLabel.Text=="a.png" && status.Text!.StartsWith("Ready"));
            Click(window,Find<Button>(window,"Redo"));await WaitAsync(()=>exposure.Value==1 && status.Text!.StartsWith("Ready"));
            Check(ImagePixels(Find<Image>(window,"PreviewImage")).SequenceEqual(edited),
                "Redo survives a second folder roundtrip and restores actual rendered pixels");
            await window.OpenFolderAsync(Path.Combine(directory,"missing"));
            Check(list.ItemCount==4 && nameLabel.Text=="a.png","failed folder discovery preserves accepted folder and image");
            string empty=Path.Combine(directory,"empty");Directory.CreateDirectory(empty);await window.OpenFolderAsync(empty);
            Check(list.ItemCount==0 && nameLabel.Text=="a.png" && !next.IsEnabled,"empty folder leaves the accepted photo available and disables navigation");
            var obsolete=window.OpenFolderAsync(directory);var latest=window.OpenFolderAsync(empty);
            await Task.WhenAll(obsolete,latest);
            Check(list.ItemCount==0 && nameLabel.Text=="a.png","superseded folder discovery cannot replace the newest empty-folder result");
        }
        finally {window.Close();Directory.Delete(directory,recursive:true);}
    }

    private static async Task TestPresetsAsync()
    {
        string directory=Path.Combine(Path.GetTempPath(),"photolab-preset-ui-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);var store=new PresetStore(Path.Combine(directory,"presets"));
        var decoder=new UiDecoder();var window=new EditorWindow(new PhotoImageLoader(decoder),store);
        try
        {
            string source=Path.Combine(directory,"photo.NEF");await File.WriteAllTextAsync(source,"Synthetic preset UI input.");
            window.Show();await window.OpenPhotoAsync(source);
            Type(window,Find<TextBox>(window,"RawWarmth"),"50");Click(window,Find<Button>(window,"ApplyWhiteBalance"));
            await WaitAsync(()=>Find<Button>(window,"SaveEdits").IsEnabled);
            var wb=decoder.LastWhiteBalance;int developments=decoder.Developments;
            var exposure=Find<Slider>(window,"ToneExposure");exposure.Value=1;
            await WaitAsync(()=>Find<TextBlock>(window,"Status").Text!.StartsWith("Ready"));
            byte[] expected=ImagePixels(Find<Image>(window,"PreviewImage"));
            Type(window,Find<TextBox>(window,"PresetName"),"Bright");
            var savePreset=Find<Button>(window,"SavePreset");savePreset.BringIntoView();using(var frame=window.CaptureRenderedFrame()) {Click(window,savePreset);}
            var list=Find<ComboBox>(window,"PresetList");
            await WaitAsync(()=>list.SelectedItem is AdjustmentPreset && savePreset.IsEnabled);
            Check((await store.ListAsync()).Single().Settings.Exposure==1,"mouse preset save persists applied basic controls");
            exposure.Value=0;await WaitAsync(()=>Find<TextBlock>(window,"Status").Text!.StartsWith("Ready"));
            byte[] baseline=ImagePixels(Find<Image>(window,"PreviewImage"));
            var preview=Find<ToggleButton>(window,"PreviewPreset");preview.BringIntoView();using(var frame=window.CaptureRenderedFrame()) {Click(window,preview);}
            await WaitAsync(()=>Find<TextBlock>(window,"Status").Text!.StartsWith("Preset preview"));
            Check(exposure.Value==0 && ImagePixels(Find<Image>(window,"PreviewImage")).SequenceEqual(expected) && !Find<Button>(window,"SaveEdits").IsEnabled,
                "preset preview changes pixels without committing controls and disables saving");
            Click(window,preview);await WaitAsync(()=>Find<TextBlock>(window,"Status").Text!.StartsWith("Ready"));
            Check(ImagePixels(Find<Image>(window,"PreviewImage")).SequenceEqual(baseline),"canceling preset preview restores accepted current pixels");
            var apply=Find<Button>(window,"ApplyPreset");apply.BringIntoView();using(var frame=window.CaptureRenderedFrame()) {Click(window,apply);}
            await WaitAsync(()=>Find<TextBlock>(window,"Status").Text!.StartsWith("Ready") && exposure.Value==1);
            Check(ImagePixels(Find<Image>(window,"PreviewImage")).SequenceEqual(expected) && decoder.LastWhiteBalance==wb && decoder.Developments==developments,
                "preset apply reproduces preview while preserving camera WB without redeveloping RAW");
            Click(window,Find<Button>(window,"Undo"));await WaitAsync(()=>exposure.Value==0 && Find<TextBlock>(window,"Status").Text!.StartsWith("Ready"));
            Check(ImagePixels(Find<Image>(window,"PreviewImage")).SequenceEqual(baseline) && decoder.LastWhiteBalance==wb,
                "one Undo restores pre-preset adjustments and keeps camera white balance");
            Click(window,Find<Button>(window,"Redo"));await WaitAsync(()=>exposure.Value==1 && Find<TextBlock>(window,"Status").Text!.StartsWith("Ready"));
            Check(ImagePixels(Find<Image>(window,"PreviewImage")).SequenceEqual(expected),"Redo restores the complete applied preset");
            string second=Path.Combine(directory,"second.NEF");await File.WriteAllTextAsync(second,"Synthetic second photo.");await window.OpenPhotoAsync(second);
            apply.BringIntoView();using(var frame=window.CaptureRenderedFrame()) {Click(window,apply);}
            await WaitAsync(()=>exposure.Value==1 && Find<TextBlock>(window,"Status").Text!.StartsWith("Ready"));
            Check(Find<TextBox>(window,"RawWarmth").Text=="0","same preset applies to another photo without importing the first camera WB");
            var delete=Find<Button>(window,"DeletePreset");delete.BringIntoView();using(var frame=window.CaptureRenderedFrame()) {Click(window,delete);}
            await WaitAsync(()=>list.SelectedItem==null && Find<TextBlock>(window,"Status").Text!.StartsWith("Preset deleted"));
            Check((await store.ListAsync()).Count==0 && exposure.Value==1,"mouse preset deletion updates the list without resetting applied edits");
        }
        finally {window.Close();Directory.Delete(directory,recursive:true);}
    }

    private static async Task TestAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "photolab-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var decoder = new UiDecoder();
        var window = new EditorWindow(new PhotoImageLoader(decoder));
        try
        {
            string raw = Path.Combine(directory, "fixture.NEF");
            await File.WriteAllTextAsync(raw, "Synthetic UI input, not a claimed camera RAW.");
            window.Show();
            await window.OpenPhotoAsync(raw);
            var warmth = Find<TextBox>(window, "RawWarmth");
            var tint = Find<TextBox>(window, "RawTint");
            var apply = Find<Button>(window, "ApplyWhiteBalance");
            var asShot = Find<Button>(window, "AsShotWhiteBalance");
            var save = Find<Button>(window, "SaveEdits");
            var undo = Find<Button>(window, "Undo");
            var redo = Find<Button>(window, "Redo");
            var capture = Find<TextBlock>(window, "CaptureInfo");
            var status = Find<TextBlock>(window, "Status");
            var image = Find<Image>(window, "PreviewImage");
            var original = Find<ToggleButton>(window, "Original");
            byte[] originalPixels = ImagePixels(image);
            var histogram=Find<HistogramView>(window,"Histogram");
            Check(histogram.Histogram != null && histogram.Histogram.PixelCount>0,"accepted photo presents a populated float histogram");
            using (var frame=window.CaptureRenderedFrame() ?? throw new Exception("No histogram Skia frame."))
            {
                var origin=histogram.TranslatePoint(new Point(0,0),window)!.Value;
                double scale=frame.PixelSize.Width/window.Bounds.Width;
                int width=(int)(histogram.Bounds.Width*scale),height=(int)(histogram.Bounds.Height*scale);
                var bytes=new byte[width*height*4];var pinned=System.Runtime.InteropServices.GCHandle.Alloc(bytes,System.Runtime.InteropServices.GCHandleType.Pinned);
                try {frame.CopyPixels(new PixelRect((int)(origin.X*scale),(int)(origin.Y*scale),width,height),pinned.AddrOfPinnedObject(),bytes.Length,width*4);}
                finally {pinned.Free();}
                int colored=0;
                for(int i=0;i<bytes.Length;i+=4)
                    if(Math.Max(bytes[i],Math.Max(bytes[i+1],bytes[i+2]))-Math.Min(bytes[i],Math.Min(bytes[i+1],bytes[i+2]))>30) colored++;
                Check(colored>10,"captured Skia histogram region contains drawn RGB curves");
            }
            var originalHistogram=histogram.Histogram;
            var png16 = Find<CheckBox>(window, "Png16Export");
            Check(png16.IsChecked != true, "PNG16 export precision is opt-in and keeps the existing default");
            Click(window, png16);
            Check(png16.IsChecked == true && !undo.IsEnabled && ImagePixels(image).SequenceEqual(originalPixels),
                "PNG16 mouse selection changes export precision without changing edits or preview");
            Check(apply.IsEnabled && warmth.Text == "0" && tint.Text == "0", "camera metadata enables as-shot controls");

            Type(window, warmth, "NaN"); Click(window, apply);
            Check(decoder.Developments == 0 && !undo.IsEnabled && status.Text!.Contains("finite"), "invalid text never develops RAW or adds history");
            Type(window, warmth, "50"); Type(window, tint, "20"); Click(window, apply);
            await WaitAsync(() => save.IsEnabled && capture.Text!.Contains("Custom WB"));
            Check(decoder.Developments == 1 && !ImagePixels(image).SequenceEqual(originalPixels), "mouse Apply develops WB and presents changed pixels");
            Check(histogram.Histogram != null && !ReferenceEquals(histogram.Histogram,originalHistogram),
                "accepted WB frame replaces histogram together with displayed pixels");
            Check(warmth.Text == "50" && tint.Text == "20" && undo.IsEnabled, "applied WB restores controls and creates undo step");
            Click(window, apply);
            Check(decoder.Developments == 1, "unchanged Apply does not redevelop or create history");
            Click(window, original);
            Check(ImagePixels(image).SequenceEqual(originalPixels), "Original shows camera-default pixels after custom WB");
            Click(window, original);
            Click(window, save);
            await WaitAsync(() => status.Text!.StartsWith("Edits saved"));
            var saved = await new EditFileStore().LoadStateAsync(raw);
            Check(saved.WhiteBalance == decoder.LastWhiteBalance, "mouse Save writes the applied full WB state");
            await window.OpenPhotoAsync(raw);
            Check(warmth.Text == "50" && tint.Text == "20" && !undo.IsEnabled, "reopening restores saved controls and resets history");
            Click(window, asShot);
            await WaitAsync(() => save.IsEnabled && !capture.Text!.Contains("Custom WB"));
            Check(ImagePixels(image).SequenceEqual(originalPixels) && undo.IsEnabled, "As shot resets WB and preserves an undo step");
            Click(window, undo);
            await WaitAsync(() => save.IsEnabled && capture.Text!.Contains("Custom WB"));
            Check(warmth.Text == "50" && tint.Text == "20" && redo.IsEnabled, "mouse Undo restores WB pixels and controls");
            Click(window, redo);
            await WaitAsync(() => save.IsEnabled && !capture.Text!.Contains("Custom WB"));
            Check(ImagePixels(image).SequenceEqual(originalPixels), "mouse Redo returns to as-shot pixels");

            decoder.FailNext = true;
            Type(window, warmth, "30"); Click(window, apply);
            await WaitAsync(() => save.IsEnabled && status.Text!.StartsWith("Open error"));
            Check(ImagePixels(image).SequenceEqual(originalPixels) && !redo.IsEnabled, "failed development keeps prior image and history");
            Click(window, undo);
            await WaitAsync(() => save.IsEnabled && capture.Text!.Contains("Custom WB"));
            Check(warmth.Text == "50", "undo after failed development still targets the last successful state");

            using (var frame = window.CaptureRenderedFrame())
            {
                Check(frame != null, "headless Skia produces a complete editor frame");
                frame!.Save(Path.Combine(Path.GetTempPath(), "PhotoLab-editor-ui.png"));
            }
            string png = Path.Combine(directory, "standard.png");
            await File.WriteAllBytesAsync(png, SkiaImageCodec.Encode(new PixelBuffer(1, 1, [32, 64, 128, 255]), jpeg: false));
            await window.OpenPhotoAsync(png);
            Check(!Find<StackPanel>(window, "RawWhiteBalancePanel").IsVisible, "standard photos hide RAW controls");
            decoder.MetadataAvailable = false;
            await window.OpenPhotoAsync(raw);
            Check(!apply.IsEnabled && asShot.IsEnabled, "missing metadata disables new WB but permits resetting saved WB");
            decoder.MetadataAvailable = true;
            await window.OpenPhotoAsync(raw);
            decoder.HoldNext = new TaskCompletionSource<RawImage>(TaskCreationOptions.RunContinuationsAsynchronously);
            Type(window, warmth, "10"); Click(window, apply);
            await WaitAsync(() => decoder.HeldWhiteBalance != null);
            window.Close();
            var heldPixels = decoder.MakePixels(decoder.HeldWhiteBalance!);
            decoder.HoldNext.SetResult(new RawImage(heldPixels, decoder.Metadata(decoder.HeldWhiteBalance)));
            await WaitAsync(() => IsDisposed(heldPixels));
            Check(IsDisposed(heldPixels), "closing during non-cooperative development releases late pixels");
        }
        finally { window.Close(); Directory.Delete(directory, recursive: true); }
    }

    private static bool IsDisposed(FloatImageBuffer pixels)
    {
        try { _ = pixels.Pixels.Length; return false; }
        catch (ObjectDisposedException) { return true; }
    }

    private static async Task FastPreviewAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "photolab-fast-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var decoder = new ControlledPreviewDecoder();
        var window = new EditorWindow(new PhotoImageLoader(decoder));
        try
        {
            string source = Path.Combine(directory, "synthetic.NEF");
            await File.WriteAllTextAsync(source, "Controlled UI fixture, not an actual RAW.");
            window.Show(); await window.OpenPhotoAsync(source);
            var warmth = Find<TextBox>(window, "RawWarmth");
            var tint = Find<TextBox>(window, "RawTint");
            var apply = Find<Button>(window, "ApplyWhiteBalance");
            var save = Find<Button>(window, "SaveEdits");
            var image = Find<Image>(window, "PreviewImage");
            Type(window, warmth, "50"); Type(window, tint, "20"); Click(window, apply);
            await WaitAsync(() => save.IsEnabled && warmth.Text == "50");
            byte[] firstFast = ImagePixels(image);
            await WaitAsync(() => decoder.FullRequests.Count == 1);
            Check(save.IsEnabled && ImagePixels(image).SequenceEqual(firstFast), "interactive WB stays visible while full quality is blocked");
            Type(window, warmth, "25"); Click(window, apply);
            await WaitAsync(() => save.IsEnabled && warmth.Text == "25");
            byte[] secondFast = ImagePixels(image);
            Check(!secondFast.SequenceEqual(firstFast), "new WB input presents a new fast frame during old quality development");
            var stale = FloatImageBuffer.FromLinearRgba32(1, 1, [0.6f, 0.3f, 0.4f, 1]);
            decoder.FullRequests[0].SetResult(new RawImage(stale, decoder.Metadata));
            await WaitAsync(() => decoder.FullRequests.Count == 2);
            Check(IsDisposed(stale) && ImagePixels(image).SequenceEqual(secondFast), "obsolete full-quality pixels are released without replacing latest WB");
            var latest = FloatImageBuffer.FromLinearRgba32(1, 1, [0.9f, 0.2f, 0.1f, 1]);
            byte[] expected = ImageConvert.ToSrgb8(latest).Data;
            decoder.FullRequests[1].SetResult(new RawImage(latest, decoder.Metadata));
            await WaitAsync(() => ImagePixels(image).SequenceEqual(expected));
            Check(warmth.Text == "25", "idle quality upgrade preserves latest WB controls");
            Click(window, Find<Button>(window, "Undo"));
            await WaitAsync(() => save.IsEnabled && warmth.Text == "50");
            Check(warmth.Text == "50", "quality upgrade adds no undo entry");
            await WaitAsync(() => decoder.FullRequests.Count == 3);
            byte[] acceptedFast = ImagePixels(image);
            decoder.FailNextPreview = true;
            Type(window, warmth, "30"); Click(window, apply);
            await WaitAsync(() => save.IsEnabled && Find<TextBlock>(window, "Status").Text!.StartsWith("Open error"));
            Check(ImagePixels(image).SequenceEqual(acceptedFast), "failed replacement preserves accepted fast WB pixels");
            var cancelledQuality = FloatImageBuffer.FromLinearRgba32(1, 1, [0.6f, 0.3f, 0.4f, 1]);
            decoder.FullRequests[2].SetResult(new RawImage(cancelledQuality, decoder.Metadata));
            await WaitAsync(() => decoder.FullRequests.Count == 4);
            Check(IsDisposed(cancelledQuality), "failed replacement still releases the cancelled previous quality result");
            var recovered = FloatImageBuffer.FromLinearRgba32(1, 1, [0.7f, 0.3f, 0.4f, 1]);
            byte[] recoveredPixels = ImageConvert.ToSrgb8(recovered).Data;
            decoder.FullRequests[3].SetResult(new RawImage(recovered, decoder.Metadata));
            await WaitAsync(() => ImagePixels(image).SequenceEqual(recoveredPixels));
            Click(window, save);
            await WaitAsync(() => Find<TextBlock>(window, "Status").Text!.StartsWith("Edits saved"));
            var acceptedState = await new EditFileStore().LoadStateAsync(source);
            Check(acceptedState.WhiteBalance == CameraWhiteBalanceAdjust.Apply(decoder.Metadata.CameraColor!.AsShot, 50, 20),
                "failed WB replacement resumes accepted quality and preserves saved WB");
            Type(window, warmth, "10"); Click(window, apply);
            await WaitAsync(() => save.IsEnabled && warmth.Text == "10");
            await WaitAsync(() => decoder.FullRequests.Count == 5);
            window.Close();
            var afterClose = FloatImageBuffer.FromLinearRgba32(1, 1, [0.5f, 0.2f, 0.1f, 1]);
            decoder.FullRequests[4].SetResult(new RawImage(afterClose, decoder.Metadata));
            await WaitAsync(() => IsDisposed(afterClose));
            Check(IsDisposed(afterClose), "closing during full-quality upgrade releases late owned pixels");
        }
        finally { window.Close(); Directory.Delete(directory, recursive: true); }
    }

    private static async Task WbDragAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "photolab-drag-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var decoder = new DragPreviewDecoder();
        var window = new EditorWindow(new PhotoImageLoader(decoder));
        try
        {
            string source = Path.Combine(directory, "drag.NEF");
            await File.WriteAllTextAsync(source, "Controlled drag input, not a camera RAW.");
            window.Show(); await window.OpenPhotoAsync(source);
            var slider = Find<Slider>(window, "RawWarmthSlider");
            var image = Find<Image>(window, "PreviewImage");
            var save = Find<Button>(window, "SaveEdits");
            byte[] initial = ImagePixels(image);
            slider.BringIntoView();
            using var frame = window.CaptureRenderedFrame();
            var start = slider.TranslatePoint(new Point(slider.Bounds.Width / 2, slider.Bounds.Height / 2), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X + 10, start.Y), RawInputModifiers.LeftMouseButton);
            await WaitAsync(() => decoder.PreviewRequests.Count == 1);
            Check(slider.IsEnabled && !save.IsEnabled, "WB slider remains enabled while its first preview is pending");
            for (int offset = 12; offset <= 80; offset += 2)
                window.MouseMove(new Point(start.X + offset, start.Y), RawInputModifiers.LeftMouseButton);
            double latestValue = slider.Value;
            Check(decoder.PreviewRequests.Count == 1, "many drag samples retain only one pending native request");
            decoder.PreviewRequests[0].Completion.SetResult(decoder.Result(0.3f));
            await WaitAsync(() => decoder.PreviewRequests.Count == 2);
            Check(!ImagePixels(image).SequenceEqual(initial) && slider.Value == latestValue,
                "WB frame updates before pointer release without moving the current thumb back");
            Check(decoder.PreviewRequests[1].WhiteBalance == CameraWhiteBalanceAdjust.Apply(decoder.Metadata.CameraColor!.AsShot, latestValue, 0),
                "next native preview uses the newest drag value");
            bool flashedOriginal = false;
            void ObserveSource(object? sender, AvaloniaPropertyChangedEventArgs args)
            {
                if (args.Property == Image.SourceProperty && image.Source is Bitmap)
                    flashedOriginal |= ImagePixels(image).SequenceEqual(initial);
            }
            image.PropertyChanged += ObserveSource;
            decoder.PreviewRequests[1].Completion.SetResult(decoder.Result(0.7f));
            await WaitAsync(() => save.IsEnabled);
            Check(slider.Value == latestValue && Find<Slider>(window, "RawTintSlider").Value == 0,
                "idle WB pump preserves exact thumb inputs while the pointer is held");
            window.MouseUp(new Point(start.X + 80, start.Y), MouseButton.Left);
            image.PropertyChanged -= ObserveSource;
            Check(!flashedOriginal, "same-photo WB refresh retains its accepted frame until the new frame is ready");
            Check(decoder.PreviewRequests.Count == 2, "coalesced WB drag skips obsolete intermediate values");
            Click(window, Find<Button>(window, "Undo"));
            await WaitAsync(() => save.IsEnabled && slider.Value == 0);
            Check(ImagePixels(image).SequenceEqual(initial) && Find<Button>(window, "Redo").IsEnabled,
                "one Undo restores the complete pre-drag WB state");
            Click(window, Find<Button>(window, "Redo"));
            await WaitAsync(() => decoder.PreviewRequests.Count == 3);
            decoder.PreviewRequests[2].Completion.SetResult(decoder.Result(0.7f));
            await WaitAsync(() => save.IsEnabled);
            Check(Math.Abs(slider.Value - latestValue) < 0.0001,
                $"Redo restores the final coalesced WB drag value ({slider.Value:G9}, expected {latestValue:G9})");
            var custom = new CameraWhiteBalance(2, 1, 1.5f, 2);
            await new EditFileStore().SaveStateAsync(source, new PhotoEditState(new EditSettings(), custom));
            await window.OpenPhotoAsync(source);
            Check(!slider.IsEnabled && Find<TextBox>(window, "RawWarmth").Text == "" && Find<Button>(window, "AsShotWhiteBalance").IsEnabled,
                "unrepresentable saved WB disables stale thumbs while allowing explicit reset");
            Click(window, save);
            await WaitAsync(() => Find<TextBlock>(window, "Status").Text!.StartsWith("Edits saved"));
            Check((await new EditFileStore().LoadStateAsync(source)).WhiteBalance == custom,
                "unrepresentable saved WB is preserved by the slider UI");
            Click(window, Find<Button>(window, "AsShotWhiteBalance"));
            await WaitAsync(() => save.IsEnabled && slider.IsEnabled);
            slider.BringIntoView();
            using var resetFrame = window.CaptureRenderedFrame();
            start = slider.TranslatePoint(new Point(slider.Bounds.Width / 2, slider.Bounds.Height / 2), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X + 10, start.Y), RawInputModifiers.LeftMouseButton);
            await WaitAsync(() => decoder.PreviewRequests.Count == 4);
            window.MouseMove(new Point(start.X + 80, start.Y), RawInputModifiers.LeftMouseButton);
            string replacement = Path.Combine(directory, "replacement.NEF");
            await File.WriteAllTextAsync(replacement, "Controlled replacement, not a camera RAW.");
            await window.OpenPhotoAsync(replacement);
            window.MouseUp(new Point(start.X + 80, start.Y), MouseButton.Left);
            var late = decoder.Result(0.9f);
            var latePixels = late.Pixels;
            decoder.PreviewRequests[3].Completion.SetResult(late);
            await WaitAsync(() => save.IsEnabled && IsDisposed(latePixels));
            Check(decoder.PreviewRequests.Count == 4 && ImagePixels(image).SequenceEqual(initial) && !Find<Button>(window, "Undo").IsEnabled,
                "photo switch discards the pending drag value and releases late old-photo pixels");
            slider.BringIntoView();
            using var closeFrame = window.CaptureRenderedFrame();
            start = slider.TranslatePoint(new Point(slider.Bounds.Width / 2, slider.Bounds.Height / 2), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X + 10, start.Y), RawInputModifiers.LeftMouseButton);
            await WaitAsync(() => decoder.PreviewRequests.Count == 5);
            window.Close();
            var afterClose = decoder.Result(0.9f);
            var closedPixels = afterClose.Pixels;
            decoder.PreviewRequests[4].Completion.SetResult(afterClose);
            await WaitAsync(() => IsDisposed(closedPixels));
            Check(IsDisposed(closedPixels), "closing during a WB drag releases late native-preview pixels");
        }
        finally { window.Close(); Directory.Delete(directory, recursive: true); }
    }

    private sealed class DragPreviewDecoder : IRawPreviewDeveloper
    {
        public List<(CameraWhiteBalance WhiteBalance, TaskCompletionSource<RawImage> Completion)> PreviewRequests { get; } = new();
        public RawMetadata Metadata { get; } = new(1, 1, ".NEF", "Synthetic", "Drag fixture")
        { CameraColor = new RawColorInfo(new CameraWhiteBalance(2, 1, 1.5f, 1), [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0], "test") };
        public RawImage Result(float red) => new(FloatImageBuffer.FromLinearRgba32(1, 1, [red, 0.4f, 0.5f, 1]), Metadata);
        public Task<RawImage> DecodeAsync(string path, CancellationToken cancellation = default) => Task.FromResult(Result(0.2f));
        public Task<RawImage> DevelopAsync(string path, CameraWhiteBalance wb, CancellationToken cancellation = default) => Task.FromResult(Result(0.7f));
        public Task<RawImage> DevelopPreviewAsync(string path, CameraWhiteBalance wb, int maxEdge, CancellationToken cancellation = default)
        {
            var completion = new TaskCompletionSource<RawImage>(TaskCreationOptions.RunContinuationsAsynchronously);
            PreviewRequests.Add((wb, completion));
            return completion.Task;
        }
    }

    private sealed class ControlledPreviewDecoder : IRawPreviewDeveloper
    {
        public List<TaskCompletionSource<RawImage>> FullRequests { get; } = new();
        public bool FailNextPreview { get; set; }
        public RawMetadata Metadata { get; } = new(1, 1, ".NEF", "Synthetic", "Quality fixture")
        { CameraColor = new RawColorInfo(new CameraWhiteBalance(2, 1, 1.5f, 1), [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0], "test") };
        public Task<RawImage> DecodeAsync(string path, CancellationToken cancellation = default)
            => Task.FromResult(new RawImage(FloatImageBuffer.FromLinearRgba32(1, 1, [0.25f, 0.5f, 0.75f, 1]), Metadata));
        public Task<RawImage> DevelopPreviewAsync(string path, CameraWhiteBalance wb, int maxEdge, CancellationToken cancellation = default)
        {
            if (FailNextPreview)
            { FailNextPreview = false; return Task.FromException<RawImage>(new InvalidDataException("Preview failure fixture")); }
            CameraWhiteBalanceAdjust.TryGetOffsets(Metadata.CameraColor!.AsShot, wb, out double warmth, out _);
            var pixels = FloatImageBuffer.FromLinearRgba32(1, 1, warmth > 40 ? [0.2f, 0.3f, 0.4f, 1] : [0.8f, 0.2f, 0.1f, 1]);
            return Task.FromResult(new RawImage(pixels, Metadata with { AppliedWhiteBalance = wb }));
        }
        public Task<RawImage> DevelopAsync(string path, CameraWhiteBalance wb, CancellationToken cancellation = default)
        {
            var completion = new TaskCompletionSource<RawImage>(TaskCreationOptions.RunContinuationsAsynchronously);
            FullRequests.Add(completion);
            return completion.Task; // Deliberately ignore cancellation to verify stale output disposal.
        }
    }

    private sealed class UiDecoder : IRawDeveloper
    {
        private readonly CameraWhiteBalance _asShot = new(2, 1, 1.5f, 1);
        public int Developments { get; private set; }
        public CameraWhiteBalance? LastWhiteBalance { get; private set; }
        public bool FailNext { get; set; }
        public bool MetadataAvailable { get; set; } = true;
        public TaskCompletionSource<RawImage>? HoldNext { get; set; }
        public CameraWhiteBalance? HeldWhiteBalance { get; private set; }
        public RawMetadata Metadata(CameraWhiteBalance? wb) => new(2, 2, ".NEF", "Synthetic", "UI fixture")
        {
            AppliedWhiteBalance = wb,
            CameraColor = MetadataAvailable ? new RawColorInfo(_asShot, [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0], "UI fixture") : null
        };
        public FloatImageBuffer MakePixels(CameraWhiteBalance? wb)
        {
            float r = 0.25f * (wb?.Channel0 / _asShot.Channel0 ?? 1);
            float g = 0.5f * (wb?.Channel1 / _asShot.Channel1 ?? 1);
            float b = 0.75f * (wb?.Channel2 / _asShot.Channel2 ?? 1);
            return FloatImageBuffer.FromLinearRgba32(2, 2, [r, g, b, 1, r, g, b, 1, r, g, b, 1, r, g, b, 1]);
        }
        public Task<RawImage> DecodeAsync(string path, CancellationToken cancellation = default)
            => Task.FromResult(new RawImage(MakePixels(null), Metadata(null)));
        public Task<RawImage> DevelopAsync(string path, CameraWhiteBalance wb, CancellationToken cancellation = default)
        {
            Developments++; LastWhiteBalance = wb;
            if (FailNext) { FailNext = false; return Task.FromException<RawImage>(new InvalidDataException("UI failure fixture")); }
            if (HoldNext != null) { HeldWhiteBalance = wb; return HoldNext.Task; }
            return Task.FromResult(new RawImage(MakePixels(wb), Metadata(wb)));
        }
    }
}
