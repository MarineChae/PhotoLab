using PhotoLab.Core;
using PhotoLab.Desktop;
using PhotoLab.Imaging;
using PhotoLab.Infrastructure;
using PhotoLab.Imaging.Decoding;

if (args.Contains("--photo-folder"))
{
    await PhotoFolderTests.RunAsync();
    return;
}

if (args.Contains("--presets"))
{
    await PresetTests.RunAsync();
    return;
}

if (args.Length > 0 && args[0] == "--png-export-benchmark")
{
    PngExportBenchmark.Run(args[1],args[2]);
    return;
}

if (args.Length > 0 && args[0] == "--png16-export")
{
    await Png16ExportTests.RunAsync(args[1],args.Skip(2).ToArray());
    return;
}

if (args.Contains("--bundled-color"))
{
    await Png16Tests.RunBundledAsync();
    return;
}

if (args.Length > 0 && args[0] == "--png16")
{
    await Png16Tests.RunAsync(args[1],args.Length > 2 ? args[2] : null);
    return;
}

if (args.Length > 0 && args[0] == "--lcms")
{
    await LittleCmsTests.RunAsync(args[1]);
    return;
}

if (args.Contains("--color-profile"))
{
    await ColorProfileTests.RunAsync();
    return;
}

if (args.Length > 0 && args[0] == "--native-preview-benchmark")
{
    await NativePreviewBenchmark.RunAsync(args[1], args.Skip(2).ToArray());
    return;
}

if (args.Length > 0 && args[0] == "--native-session")
{
    await NativeSessionTests.RunAsync(args[1], args.Skip(2).ToArray());
    return;
}

if (args.Length > 0 && args[0] == "--cached-loader")
{
    await CachedLoaderTests.RunAsync(args[1], args.Skip(2).ToArray());
    return;
}

if (args.Length > 0 && args[0] == "--ui-native")
{
    await EditorUiTests.RunNativeAsync(args[1], args.Skip(2).ToArray());
    return;
}

if (args.Length > 0 && args[0] == "--ui-vulkan")
{
    await EditorUiTests.RunVulkanAsync(args[1]);
    return;
}

if (args.Contains("--ui"))
{
    await EditorUiTests.RunAsync();
    return;
}

if (args.Contains("--display-benchmark"))
{
    await EditorUiTests.RunDisplayBenchmarkAsync();
    return;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

static async Task RejectAsync(Func<Task> action, string message)
{
    try { await action(); }
    catch (Exception error) when (error is IOException or InvalidDataException)
    { Console.WriteLine("PASS: " + message); return; }
    throw new Exception("FAIL: " + message);
}

static int CheckRaw(FloatImageBuffer image)
{
    var levels = new HashSet<float>();
    var samples = image.Pixels;
    for (int i = 0; i < samples.Length; i++)
    {
        if (!float.IsFinite(samples[i])) throw new Exception("Non-finite RAW sample.");
        if (i % 4 == 0 && levels.Count <= 256) levels.Add(samples[i]);
    }
    return levels.Count;
}

string directory = Path.Combine(Path.GetTempPath(), "photolab-desktop-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var pixels = new PixelBuffer(2, 1, [32, 64, 128, 255, 200, 100, 80, 255]);
    string source = Path.Combine(directory, "original.png");
    byte[] original = SkiaImageCodec.Encode(pixels, jpeg: false);
    await File.WriteAllBytesAsync(source, original);
    Assert(SkiaImageCodec.LoadFull(source).Data.SequenceEqual(pixels.Data), "PNG lossless pixel round-trip");
    var loader = new PhotoImageLoader();
    using var loadedLinear = await loader.LoadLinearFullAsync(source);
    Assert(ImageConvert.ToSrgb8(loadedLinear).Data.SequenceEqual(pixels.Data), "App loader uses float processing for PNG");
    var loadedPreview = await loader.LoadLinearPreviewAsync(source);
    using (loadedPreview.preview)
    {
        Assert(loadedPreview.originalWidth == 2 && ImageConvert.ToSrgb8(loadedPreview.preview).Data.SequenceEqual(pixels.Data),
            "App preview retains float data and original dimensions");
    }
    var preview = SkiaImageCodec.LoadPreview(source, maxEdge: 1);
    Assert(preview.preview.Width == 1 && preview.preview.Height == 1 && preview.originalWidth == 2,
        "Preview resizes while retaining original dimensions");
    string jpeg = Path.Combine(directory, "edited.jpg");
    var processor = new CpuImageProcessor();
    var edited = processor.Process(pixels, new EditSettings { Exposure = 1 });
    await PhotoExportStore.SaveAsync(source, jpeg, SkiaImageCodec.Encode(edited, jpeg: true));
    var decoded = SkiaImageCodec.LoadFull(jpeg);
    Assert(decoded.Width == 2 && decoded.Height == 1, "JPEG export decodes at full dimensions");
    Assert((await File.ReadAllBytesAsync(source)).SequenceEqual(original), "Editing and export preserve source bytes");
    await RejectAsync(() => PhotoExportStore.SaveAsync(source, source, [1, 2, 3]), "Direct original overwrite rejected");
    await RejectAsync(() => PhotoExportStore.SaveAsync(source, Path.Combine(directory, ".", "original.png"), [1]),
        "Normalized original path rejected");
    // Unix links do not require special privileges. Windows link tests need a privileged/developer-mode runner.
    if (!OperatingSystem.IsWindows())
    {
        string link = Path.Combine(directory, "linked.png");
        File.CreateSymbolicLink(link, source);
        await RejectAsync(() => PhotoExportStore.SaveAsync(source, link, [1]), "Symlink to source rejected");
        string directoryLink = Path.Combine(directory, "alias");
        Directory.CreateSymbolicLink(directoryLink, directory);
        await RejectAsync(() => PhotoExportStore.SaveAsync(source, Path.Combine(directoryLink, "original.png"), [1]),
            "Source through directory symlink rejected");
        Directory.Delete(directoryLink);
        string hardLink = Path.Combine(directory, "hard-linked.png");
        using var linkProcess = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo("/bin/ln") { UseShellExecute = false }
        };
        linkProcess.StartInfo.ArgumentList.Add(source);
        linkProcess.StartInfo.ArgumentList.Add(hardLink);
        linkProcess.Start();
        await linkProcess.WaitForExitAsync();
        Assert(linkProcess.ExitCode == 0, "Hard-link test fixture created");
        await PhotoExportStore.SaveAsync(source, hardLink, [1, 2, 3]);
        Assert((await File.ReadAllBytesAsync(source)).SequenceEqual(original), "Export replaces hard link without modifying source inode");
    }
    string output = Path.Combine(directory, "edited.png");
    await File.WriteAllBytesAsync(output, [4, 5, 6, 7, 8]);
    await PhotoExportStore.SaveAsync(source, output, original);
    Assert((await File.ReadAllBytesAsync(output)).SequenceEqual(original), "Existing destination replaced without trailing bytes");
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    try
    {
        await PhotoExportStore.SaveAsync(source, output, [9], cancellation.Token);
        throw new Exception("FAIL: cancelled export succeeded");
    }
    catch (OperationCanceledException) { Console.WriteLine("PASS: cancelled export rejected"); }
    Assert((await File.ReadAllBytesAsync(output)).SequenceEqual(original), "Cancelled export preserves existing destination");
    Assert(!Directory.EnumerateFiles(directory, ".photolab-write-*").Any(), "Temporary export files cleaned up");
    Assert((await File.ReadAllBytesAsync(source)).SequenceEqual(original), "Source unchanged after all rejected exports");
    string broken = Path.Combine(directory, "broken.png");
    await File.WriteAllBytesAsync(broken, [1, 2, 3]);
    try { SkiaImageCodec.LoadFull(broken); throw new Exception("FAIL: corrupt PNG accepted"); }
    catch (InvalidDataException) { Console.WriteLine("PASS: corrupt image rejected"); }

    using var blocking = new BlockingProcessor();
    using var renderer = new PreviewRenderer(blocking);
    var first = renderer.RenderAsync(pixels, new EditSettings());
    await blocking.Started.WaitAsync(TimeSpan.FromSeconds(5));
    var middle = renderer.RenderAsync(pixels, new EditSettings { Exposure = 1 });
    var latest = renderer.RenderAsync(edited, new EditSettings());
    blocking.Release();
    Assert(await first == null, "Non-cancellable obsolete render discarded");
    Assert(await middle == null, "Superseded queued render cancelled");
    var frame = await latest.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(frame != null && renderer.IsCurrent(frame.Version) && frame.Pixels.Data.SequenceEqual(edited.Data),
        "Latest render returned without overlapping CPU work");
    Assert(frame!.Histogram != null && frame.Histogram.PixelCount==edited.Width*edited.Height,
        "only the accepted legacy frame publishes its owned histogram");
    renderer.Invalidate();
    Assert(!renderer.IsCurrent(frame!.Version), "Image switch invalidates completed preview");
    var following = await renderer.RenderAsync(pixels, new EditSettings());
    Assert(following != null && renderer.IsCurrent(following.Version), "Rendering resumes after invalidation");
    renderer.Dispose();
    Assert(!renderer.IsCurrent(following!.Version), "Closing invalidates preview results");

    using var floatRenderer = new PreviewRenderer(new CpuImageProcessor());
    using var floatSource = FloatImageBuffer.FromLinearRgba16(1, 1, [32768, 32769, 65535, 65535]);
    var floatFrame = await floatRenderer.RenderAsync(floatSource, new EditSettings { Exposure = -1 });
    using var floatExpected = new FloatImageProcessor().Process(floatSource, new EditSettings { Exposure = -1 });
    Assert(floatFrame != null && floatFrame.Pixels.Data.SequenceEqual(ImageConvert.ToSrgb8(floatExpected).Data),
        "App renderer quantizes only after float adjustment");
    var expectedHistogram=ImageHistogram.Compute(floatExpected);
    Assert(floatFrame!.Histogram != null && floatFrame.Histogram.Red.SequenceEqual(expectedHistogram.Red) &&
        floatFrame.Histogram.Luminance.SequenceEqual(expectedHistogram.Luminance),
        "renderer histogram matches adjusted float before display quantization");
    await floatRenderer.DrainAsync();
    Assert(!floatRenderer.IsCurrent(floatFrame!.Version), "Source drain invalidates rendered frame before buffer disposal");

    var controlled = new BlockingFloatProcessor();
    using var closing = new PreviewRenderer(new CpuImageProcessor(), controlled);
    var closingFrame = closing.RenderAsync(floatSource, new EditSettings());
    await controlled.Started.WaitAsync(TimeSpan.FromSeconds(5));
    closing.Dispose();
    Assert(!controlled.Disposed.IsCompleted, "renderer close retains its owned processor while a frame is in flight");
    controlled.Release();
    Assert(await closingFrame == null, "renderer close discards the non-cooperative float result");
    await controlled.Disposed.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(controlled.Disposed.IsCompleted, "renderer releases its processor after the last worker reader finishes");

    var history = new EditHistory();
    history.BeginGroup();
    history.Apply(new EditSettings { Exposure = 0.25 });
    history.Apply(new EditSettings { Exposure = 0.5 });
    history.Apply(new EditSettings { Exposure = 1 });
    history.EndGroup();
    Assert(history.Count == 2 && history.Current.Exposure == 1, "Slider drag retains only the final history state");
    Assert(history.Move(-1) && history.Current == new EditSettings(), "One undo restores state before slider drag");
    Assert(history.Move(1) && history.Current.Exposure == 1, "Redo restores final slider state");
    history.Move(-1);
    history.Apply(new EditSettings { Contrast = 20 });
    Assert(!history.CanRedo && history.Current.Contrast == 20, "New edit removes abandoned redo branch");
    for (int i = 0; i < 200; i++) history.Apply(new EditSettings { Contrast = i });
    Assert(history.Count == EditHistory.MAX_HISTORY_COUNT && history.Current.Contrast == 199, "History remains bounded and keeps latest edit");
    history.BeginGroup();
    history.Apply(new EditSettings { Contrast = 198 });
    history.Apply(new EditSettings { Contrast = 197 });
    history.EndGroup();
    Assert(history.Move(-1) && history.Current.Contrast == 199, "Grouped undo survives history trimming");
    history.Reset(new EditSettings { Exposure = 2 });
    Assert(!history.CanUndo && !history.CanRedo && history.Count == 1 && history.Current.Exposure == 2,
        "Opening a photo resets history to its restored settings");

    var editFiles = new EditFileStore();
    var edits = new EditSettings { Exposure = 0.5, Tint = 10 };
    await new SidecarStore().SaveAsync(source, edits);
    Assert(await editFiles.LoadAsync(source) == edits, "Versioned store reads existing legacy edit files");
    await editFiles.SaveAsync(source, edits);
    Assert(await editFiles.LoadAsync(source) == edits && await new SidecarStore().LoadAsync(source) == edits,
        "Versioned edit files round-trip and remain compatible with the Core reader");
    string editPath = EditFileStore.GetPath(source);
    byte[] savedEdits = await File.ReadAllBytesAsync(editPath);
    try
    {
        await editFiles.SaveAsync(source, new EditSettings { Exposure = 1 }, cancellation.Token);
        throw new Exception("FAIL: cancelled edit save succeeded");
    }
    catch (OperationCanceledException) { Console.WriteLine("PASS: cancelled edit save rejected"); }
    Assert((await File.ReadAllBytesAsync(editPath)).SequenceEqual(savedEdits), "Cancelled edit save preserves previous JSON");
    await RejectAsync(() => editFiles.SaveAsync(source, new EditSettings { Exposure = double.NaN }), "Invalid settings rejected before writing");
    Assert((await File.ReadAllBytesAsync(editPath)).SequenceEqual(savedEdits), "Invalid settings preserve previous JSON");
    await File.WriteAllTextAsync(editPath, "{\"version\":99,\"Exposure\":1}");
    await RejectAsync(async () => { await editFiles.LoadAsync(source); }, "Unknown edit version rejected");
    await File.WriteAllTextAsync(editPath, "{");
    await RejectAsync(async () => { await editFiles.LoadAsync(source); }, "Malformed edit JSON rejected");
    File.Delete(editPath);
    if (!OperatingSystem.IsWindows())
    {
        File.CreateSymbolicLink(editPath, source);
        await RejectAsync(() => editFiles.SaveAsync(source, edits), "Edit file symlink to original rejected");
        File.Delete(editPath);
    }
    Assert((await File.ReadAllBytesAsync(source)).SequenceEqual(original), "Versioned edit saves preserve photograph bytes");

    var stateWb = new CameraWhiteBalance(1.8710938f, 1, 1.546875f, 0);
    var photoState = new PhotoEditState(edits, stateWb);
    history.ResetState(photoState);
    history.Apply(edits with { Exposure = 1 });
    Assert(history.CurrentState.WhiteBalance == stateWb && history.Current.Exposure == 1,
        "Legacy tone edit history API retains camera WB");
    history.ApplyState(history.CurrentState with { WhiteBalance = null });
    Assert(history.Move(-1) && history.CurrentState.WhiteBalance == stateWb && history.Current.Exposure == 1,
        "Undo restores camera WB and tone state together");
    var currentPhoto = history.CurrentState;
    Assert(history.Peek(1)?.WhiteBalance == null && history.CurrentState == currentPhoto && history.CanRedo,
        "Inspecting redo does not commit history before RAW development succeeds");
    Assert(history.Peek(int.MaxValue) == null && history.Peek(int.MinValue) == null && history.CurrentState == currentPhoto,
        "Out-of-range history inspection never mutates state");
    Assert(history.Move(1) && history.CurrentState.WhiteBalance == null, "Redo restores camera-WB reset");
    history.Move(-1);
    history.ApplyState(new PhotoEditState(edits, stateWb with { Channel0 = 2 }));
    Assert(!history.CanRedo && history.CurrentState.WhiteBalance?.Channel0 == 2,
        "New camera WB edit removes abandoned redo branch");
    history.ResetState(photoState);
    history.BeginGroup();
    for (int i = 1; i <= 30; i++) history.Apply(edits with { Contrast = i });
    history.EndGroup();
    Assert(history.Count == 2 && history.Move(-1) && history.CurrentState == photoState,
        "Grouped tone undo keeps restored camera WB and initial state");

    string stateSource = Path.Combine(directory, "state.NEF");
    await File.WriteAllBytesAsync(stateSource, original); // Storage fixture only, not a claimed decodable RAW.
    string statePath = EditFileStore.GetPath(stateSource);
    await editFiles.SaveStateAsync(stateSource, photoState);
    Assert(await editFiles.LoadStateAsync(stateSource) == photoState,
        "Version 2 restores float camera WB and legacy tone values exactly");
    byte[] savedState = await File.ReadAllBytesAsync(statePath);
    await RejectAsync(async () => { await editFiles.LoadAsync(stateSource); }, "Tone-only reader refuses WB state instead of discarding it");
    await RejectAsync(() => editFiles.SaveAsync(stateSource, new EditSettings()), "Tone-only writer refuses to overwrite camera WB state");
    Assert((await File.ReadAllBytesAsync(statePath)).SequenceEqual(savedState), "Rejected legacy save preserves version 2 bytes");
    bool concurrentStatePreserved = true;
    for (int attempt = 0; attempt < 20; attempt++)
    {
        await editFiles.SaveStateAsync(stateSource, new PhotoEditState(edits));
        var startSaves = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var modernSave = Task.Run(async () => { await startSaves.Task; await new EditFileStore().SaveStateAsync(stateSource, photoState); });
        var legacySave = Task.Run(async () =>
        {
            await startSaves.Task;
            try { await new EditFileStore().SaveAsync(stateSource, new EditSettings { Contrast = 20 }); }
            catch (InvalidDataException) { /* Expected when the modern writer wins the gate first. */ }
        });
        startSaves.SetResult();
        await Task.WhenAll(modernSave, legacySave);
        concurrentStatePreserved &= await editFiles.LoadStateAsync(stateSource) == photoState;
    }
    Assert(concurrentStatePreserved, "Twenty racing modern/legacy saves preserve camera WB across store instances");
    try { await editFiles.SaveStateAsync(stateSource, photoState with { WhiteBalance = null }, cancellation.Token); throw new Exception("FAIL: cancelled full state saved"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: cancelled full-state save rejected"); }
    Assert((await File.ReadAllBytesAsync(statePath)).SequenceEqual(savedState), "Cancelled full-state save preserves camera WB file");
    await RejectAsync(() => editFiles.SaveStateAsync(stateSource, photoState with { WhiteBalance = new CameraWhiteBalance(float.NaN, 1, 1, 0) }),
        "Invalid WB state rejected before file replacement");
    Assert((await File.ReadAllBytesAsync(statePath)).SequenceEqual(savedState), "Invalid WB save preserves previous state");
    await RejectAsync(() => editFiles.SaveStateAsync(source, photoState), "Camera WB cannot be saved for standard image inputs");
    foreach (string invalidState in new[] {
        "{\"version\":2}",
        "{\"version\":2,\"CameraWhiteBalance\":null}",
        "{\"version\":2,\"CameraWhiteBalance\":{\"Channel0\":2,\"Channel1\":1,\"Channel2\":1}}",
        "{\"version\":2,\"CameraWhiteBalance\":{\"Channel0\":0,\"Channel1\":1,\"Channel2\":1,\"Channel3\":0}}",
        "{\"version\":2,\"CameraWhiteBalance\":{\"Channel0\":2,\"Channel1\":1,\"Channel2\":1,\"Channel3\":0,\"channel3\":1}}",
        "{\"version\":2,\"Version\":1}",
        "{\"version\":1,\"CameraWhiteBalance\":{\"Channel0\":2,\"Channel1\":1,\"Channel2\":1,\"Channel3\":0}}" })
    {
        await File.WriteAllTextAsync(statePath, invalidState);
        await RejectAsync(async () => { await editFiles.LoadStateAsync(stateSource); }, "Malformed/ambiguous WB schema rejected: " + invalidState);
    }
    await File.WriteAllTextAsync(statePath, "{\"VERSION\":2,\"camerawhitebalance\":{\"channel0\":2,\"channel1\":1,\"channel2\":1.5,\"channel3\":0}}");
    Assert((await editFiles.LoadStateAsync(stateSource)).WhiteBalance == new CameraWhiteBalance(2, 1, 1.5f, 0),
        "Unambiguous case-insensitive WB schema remains readable");
    await editFiles.SaveStateAsync(stateSource, new PhotoEditState(edits));
    Assert(await editFiles.LoadAsync(stateSource) == edits && await new SidecarStore().LoadAsync(stateSource) == edits,
        "Explicit WB reset writes compatible version 1 for legacy readers");
    Assert((await File.ReadAllBytesAsync(stateSource)).SequenceEqual(original), "Full-state storage preserves source bytes");

    FloatImageBuffer? decodedPixels = null;
    string? decodedPath = null;
    var replaceable = new StubRawDecoder((path, token) =>
    {
        token.ThrowIfCancellationRequested();
        decodedPath = path;
        decodedPixels = FloatImageBuffer.FromLinearRgba16(1, 1, [32768, 32769, 65535, 65535]);
        return Task.FromResult(new RawImage(decodedPixels, new RawMetadata(1, 1, Path.GetExtension(path), "Test fixture", "Synthetic")));
    });
    var replaceableLoader = new PhotoImageLoader(replaceable);
    using (var replaced = await replaceableLoader.LoadLinearFullAsync("fixture.NEF"))
        Assert(decodedPath == "fixture.NEF" && replaced.Pixels[1] > replaced.Pixels[0],
            "App full-resolution loader uses injected decoder without precision loss");
    var replacedPreview = await replaceableLoader.LoadPreviewWithMetadataAsync("fixture.NEF");
    using (replacedPreview.preview)
    {
        Assert(replacedPreview.metadata?.CameraModel == "Synthetic" && replacedPreview.metadata.SourceExtension == ".NEF",
            "App preview passes decoder metadata without extension-based camera inference");
    }
    try { _ = decodedPixels!.Pixels.Length; throw new Exception("FAIL: decoded source not released after preview resize"); }
    catch (ObjectDisposedException) { Console.WriteLine("PASS: loader releases full decoded surface after creating preview"); }
    var (compatPreview, compatWidth, compatHeight) = await replaceableLoader.LoadLinearPreviewAsync("fixture.NEF");
    using (compatPreview)
        Assert(compatWidth == 1 && compatHeight == 1 && compatPreview.Pixels[1] > compatPreview.Pixels[0],
            "Existing three-value preview API remains compatible");
    var compatRgba = await replaceableLoader.LoadFullAsync("fixture.NEF");
    Assert(compatRgba.Data.SequenceEqual(new byte[] { 188, 188, 255, 255 }),
        "Injected RAW decoder also serves the legacy RGBA8 API");
    var failingLoader = new PhotoImageLoader(new StubRawDecoder((_, _) => Task.FromException<RawImage>(new InvalidDataException("Unsupported test fixture"))));
    await RejectAsync(async () => { using var rejected = await failingLoader.LoadLinearFullAsync("broken.NEF"); },
        "Injected decoder failure reaches caller");
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var completion = new TaskCompletionSource<RawImage>(TaskCreationOptions.RunContinuationsAsynchronously);
    var lateLoader = new PhotoImageLoader(new StubRawDecoder((_, _) => { started.TrySetResult(); return completion.Task; }));
    using var lateCancellation = new CancellationTokenSource();
    var lateLoad = lateLoader.LoadLinearFullAsync("late.NEF", lateCancellation.Token);
    await started.Task;
    lateCancellation.Cancel();
    var latePixels = FloatImageBuffer.FromLinearRgba32(1, 1, [0, 0, 0, 1]);
    completion.SetResult(new RawImage(latePixels, new RawMetadata(1, 1, ".NEF")));
    try { using var cancelledLoad = await lateLoad; throw new Exception("FAIL: late decoded RAW accepted after cancellation"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: non-cooperative RAW completion rejected after cancellation"); }
    try { _ = latePixels.Pixels.Length; throw new Exception("FAIL: late RAW pixels leaked"); }
    catch (ObjectDisposedException) { Console.WriteLine("PASS: cancelled late RAW result releases pixels"); }

    var baseMetadata = new RawMetadata(1, 1, ".NEF");
    var requestedWb = new CameraWhiteBalance(2.125f, 1, 1.375f, 0);
    CameraWhiteBalance? forwardedWb = null;
    FloatImageBuffer? developedPixels = null;
    var developerLoader = new PhotoImageLoader(new StubRawDeveloper((path, wb, token) =>
    {
        token.ThrowIfCancellationRequested();
        forwardedWb = wb;
        developedPixels = FloatImageBuffer.FromLinearRgba32(1, 1, [0.25f, 0.5f, 0.75f, 1]);
        return Task.FromResult(new RawImage(developedPixels, baseMetadata with { AppliedWhiteBalance = wb }));
    }));
    using (var developedFull = await developerLoader.LoadLinearFullAsync("fixture.NEF", requestedWb))
        Assert(forwardedWb == requestedWb && developedFull.Pixels[2] == 0.75f,
            "Full loader forwards camera WB to developer without post-sRGB gains");
    var developedPreview = await developerLoader.LoadPreviewWithMetadataAsync("fixture.NEF", requestedWb);
    using (developedPreview.preview)
        Assert(developedPreview.metadata?.AppliedWhiteBalance == requestedWb && developedPreview.preview.Pixels[0] == 0.25f,
            "Preview loader preserves explicit WB metadata and developed pixels");
    try { _ = developedPixels!.Pixels.Length; throw new Exception("FAIL: developed preview source leaked"); }
    catch (ObjectDisposedException) { Console.WriteLine("PASS: developed full surface released after preview creation"); }
    try { using var unexpected = await replaceableLoader.LoadLinearFullAsync("fixture.NEF", requestedWb); throw new Exception("FAIL: unsupported WB silently ignored"); }
    catch (NotSupportedException) { Console.WriteLine("PASS: decoder without development capability rejects explicit WB"); }
    try { using var unexpected = await developerLoader.LoadLinearFullAsync(source, requestedWb); throw new Exception("FAIL: camera WB applied to PNG"); }
    catch (NotSupportedException) { Console.WriteLine("PASS: camera WB cannot be applied to standard image inputs"); }
    try { using var unexpected = await new LibRawDecoder().DevelopAsync("missing.NEF", new CameraWhiteBalance(float.NaN, 1, 1, 0)); throw new Exception("FAIL: invalid WB reached native decoder"); }
    catch (ArgumentException) { Console.WriteLine("PASS: invalid WB rejected before source or native access"); }
    try { using var unexpected = await developerLoader.LoadLinearFullAsync("fixture.NEF", requestedWb, cancellation.Token); throw new Exception("FAIL: cancelled development accepted"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: explicit development honors pre-cancellation"); }
    using var lateDevelopmentCancellation = new CancellationTokenSource();
    var developmentCompletion = new TaskCompletionSource<RawImage>(TaskCreationOptions.RunContinuationsAsynchronously);
    var lateDeveloperLoader = new PhotoImageLoader(new StubRawDeveloper((_, _, _) => developmentCompletion.Task));
    var lateDevelopment = lateDeveloperLoader.LoadLinearFullAsync("fixture.NEF", requestedWb, lateDevelopmentCancellation.Token);
    lateDevelopmentCancellation.Cancel();
    var lateDevelopedPixels = FloatImageBuffer.FromLinearRgba32(1, 1, [0, 0, 0, 1]);
    developmentCompletion.SetResult(new RawImage(lateDevelopedPixels, baseMetadata));
    try { using var unexpected = await lateDevelopment; throw new Exception("FAIL: late developed pixels accepted"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: late non-cooperative development rejected after cancellation"); }
    try { _ = lateDevelopedPixels.Pixels.Length; throw new Exception("FAIL: cancelled development pixels leaked"); }
    catch (ObjectDisposedException) { Console.WriteLine("PASS: late development surface disposed on cancellation"); }
    var camera = RawMetadataReader.Parse("Camera: misleading label\r\nNormalized Make/Model: =Nikon/D2H= CamMaker ID: 43\r\n", baseMetadata);
    Assert(camera.CameraMake == "Nikon" && camera.CameraModel == "D2H" && camera.MetadataWarning == null,
        "Camera identity uses normalized native fields and handles CRLF");
    var capture = RawMetadataReader.Parse("Normalized Make/Model: =Nikon/D2H= CamMaker ID: 43\nISO speed: 200\nShutter: 1/500.0 sec\nAperture: f/8.0\n", baseMetadata);
    Assert(capture.IsoSpeed == 200 && capture.ExposureSeconds == 0.002 && capture.Aperture == 8,
        "Capture fields after camera identity include fractional shutter, ISO and aperture");
    var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
    try
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        var decimalCapture = RawMetadataReader.Parse("Shutter: 1.3 sec\r\nAperture: f/6.3\r\n", baseMetadata);
        Assert(decimalCapture.ExposureSeconds == 1.3 && decimalCapture.Aperture == 6.3,
            "Native capture decimal parsing is independent of UI culture");
    }
    finally { System.Globalization.CultureInfo.CurrentCulture = previousCulture; }
    var invalidCapture = RawMetadataReader.Parse("ISO speed: NaN\nShutter: 1/0 sec\nAperture: f/0.0\n", baseMetadata);
    Assert(invalidCapture.IsoSpeed == null && invalidCapture.ExposureSeconds == null && invalidCapture.Aperture == null,
        "Non-finite, zero and invalid capture fields remain unavailable");
    var negativeCapture = RawMetadataReader.Parse("ISO speed: -100\nShutter: -1 sec\nAperture: f/Infinity\n", baseMetadata);
    Assert(negativeCapture.IsoSpeed == null && negativeCapture.ExposureSeconds == null && negativeCapture.Aperture == null,
        "Negative and infinite capture fields remain unavailable");
    string capturePath = "/tmp/raw\nISO speed: 999\nShutter: 99 sec\nfile.NEF";
    var safeCapture = RawMetadataReader.Parse("Filename: " + capturePath + "\nISO speed: 200\n", baseMetadata, capturePath);
    Assert(safeCapture.IsoSpeed == 200 && safeCapture.ExposureSeconds == null,
        "Multiline filename cannot inject capture values");
    Assert(NativeRawColorReader.FindLibrary(Path.Combine(directory, "missing-decoder")) == null,
        "Color metadata lookup does not load unrelated global libraries");
    try { NativeRawColorReader.Read("missing-library", source, cancellation.Token); throw new Exception("FAIL: cancelled native read started"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: native metadata read honors pre-cancellation before loading"); }
    string misleadingPath = "/tmp/raw\nNormalized Make/Model: =Fake/Camera= CamMaker ID: 0\nfile.NEF";
    var actualCamera = RawMetadataReader.Parse("Filename: " + misleadingPath +
        "\nNormalized Make/Model: =Nikon/D2H= CamMaker ID: 43\n", baseMetadata, misleadingPath);
    Assert(actualCamera.CameraMake == "Nikon" && actualCamera.CameraModel == "D2H",
        "Multiline filename cannot inject camera metadata into helper output");
    var unknownCamera = RawMetadataReader.Parse("Camera: guessed from file.NEF", baseMetadata);
    Assert(unknownCamera.CameraMake == null && unknownCamera.CameraModel == null && unknownCamera.MetadataWarning != null,
        "Unknown metadata output does not invent camera identity");
    var missingMetadata = await RawMetadataReader.ReadAsync(Path.Combine(directory, "missing-raw-identify"), source, baseMetadata);
    Assert(missingMetadata.CameraModel == null && missingMetadata.MetadataWarning != null,
        "Unavailable metadata helper reports a warning without rejecting the decoded image");
    if (!OperatingSystem.IsWindows())
    {
        string slowHelper = Path.Combine(directory, "slow metadata helper");
        await File.WriteAllTextAsync(slowHelper, "#!/bin/sh\nexec sleep 10\n");
        File.SetUnixFileMode(slowHelper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var timeoutResult = await RawMetadataReader.ReadAsync(slowHelper, source, baseMetadata);
        Assert(timeoutResult.MetadataWarning?.Contains("timed out", StringComparison.Ordinal) == true,
            "Slow metadata helper is terminated and reported as unavailable");
    }
}
finally { Directory.Delete(directory, recursive: true); }

// Optional real camera files are local test inputs, never bundled or substituted with synthetic RAWs.
for (int argument = 0; argument < args.Length; argument++)
{
    if (args[argument] == "--benchmark") { await PreviewBenchmark.RunAsync(); continue; }
    bool rejectNotRaw = args[argument] == "--reject-not-raw";
    string rawPath = args[rejectNotRaw ? ++argument : argument];
    byte[] before;
    using (var file = File.OpenRead(rawPath)) before = System.Security.Cryptography.SHA256.HashData(file);
    if (rejectNotRaw)
    {
        bool rejected = false;
        try { using var unexpected = await new PhotoImageLoader().LoadLinearFullAsync(rawPath); }
        catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "Non-RAW payload with RAW extension is rejected by the decoder");
        using var file = File.OpenRead(rawPath);
        Assert(System.Security.Cryptography.SHA256.HashData(file).SequenceEqual(before), "Rejected RAW payload remains unchanged");
        continue;
    }
    var elapsed = System.Diagnostics.Stopwatch.StartNew();
    CameraWhiteBalance? asShot = null;
    var cameraPreview = await new PhotoImageLoader().LoadPreviewWithMetadataAsync(rawPath);
    using (cameraPreview.preview)
    {
        Assert(!string.IsNullOrEmpty(cameraPreview.metadata?.CameraMake) && !string.IsNullOrEmpty(cameraPreview.metadata.CameraModel),
            "Real RAW camera identity extracted: " + Path.GetFileName(rawPath));
        Console.WriteLine($"CAMERA: {Path.GetFileName(rawPath)} {cameraPreview.metadata!.CameraMake} {cameraPreview.metadata.CameraModel}");
        var realMetadata = cameraPreview.metadata;
        Assert(realMetadata.IsoSpeed > 0 && realMetadata.ExposureSeconds > 0,
            "Real RAW ISO and shutter extracted: " + Path.GetFileName(rawPath));
        var realColor = realMetadata.CameraColor;
        Assert(realColor != null && realColor.CameraToSrgb.Length == 12 && !string.IsNullOrEmpty(realColor.LibRawVersion),
            "Real RAW native WB and camera matrix extracted: " + Path.GetFileName(rawPath));
        asShot = realColor!.AsShot;
        if (Path.GetFileName(rawPath) == "nef.nef")
            Assert(realColor!.AsShot == new CameraWhiteBalance(563, 256, 373, 256) &&
                Math.Abs(realColor.CameraToSrgb[0] - 1.7988f) < 0.00005f &&
                Math.Abs(realColor.CameraToSrgb[5] - 1.3266f) < 0.00005f && realMetadata.Aperture == 8,
                "Nikon D2H fixture matches archived WB, matrix orientation and aperture");
        if (Path.GetFileName(rawPath) == "arw.arw") Assert(realMetadata.Aperture == null, "Sony fixture unknown f/0 is not displayed as aperture");
        if (Path.GetFileName(rawPath) == "dng.dng") Assert(realColor!.AsShot.Channel3 == 0, "Pentax DNG absent fourth channel is not treated as alpha");
        Console.WriteLine($"COLOR: {Path.GetFileName(rawPath)} LibRaw {realColor!.LibRawVersion}, WB {realColor.AsShot}, ISO {realMetadata.IsoSpeed}, shutter {realMetadata.ExposureSeconds}, f/{realMetadata.Aperture}");
    }
    using var raw = await new PhotoImageLoader().LoadLinearFullAsync(rawPath);
    // Explicit WB belongs to original development, not to the already color-converted surface above.
    var customWb = CameraWhiteBalanceAdjust.Apply(asShot!, warmth: 50, tint: 20);
    var developmentCulture = System.Globalization.CultureInfo.CurrentCulture;
    RawImage customRaw;
    try
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        customRaw = await new LibRawDecoder().DevelopAsync(rawPath, customWb);
    }
    finally { System.Globalization.CultureInfo.CurrentCulture = developmentCulture; }
    using (customRaw)
    {
        Assert(customRaw.Pixels.Width == raw.Width && customRaw.Pixels.Height == raw.Height &&
            customRaw.Metadata.AppliedWhiteBalance == customWb && customRaw.Metadata.CameraColor?.AsShot == asShot,
            "Custom WB development preserves dimensions, requested WB and original AsShot: " + Path.GetFileName(rawPath));
        Assert(!customRaw.Pixels.Pixels.SequenceEqual(raw.Pixels) && CheckRaw(customRaw.Pixels) > 256,
            "Custom WB changes actual RAW development while retaining precision: " + Path.GetFileName(rawPath));
        var wbPreview = await new PhotoImageLoader().LoadPreviewWithMetadataAsync(rawPath, customWb);
        using (wbPreview.preview)
        using (var expectedPreview = ImageResize.Preview(customRaw.Pixels, 1800))
            Assert(wbPreview.preview.Pixels.SequenceEqual(expectedPreview.Pixels) && wbPreview.metadata?.AppliedWhiteBalance == customWb,
                "Actual app WB preview matches full development at the same settings: " + Path.GetFileName(rawPath));
        string referencePath = Path.Combine(Path.GetTempPath(), "photolab-wb-reference-" + Guid.NewGuid().ToString("N") + ".ppm");
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo(Environment.GetEnvironmentVariable("PHOTOLAB_DCRAW_EMU") ?? "dcraw_emu")
                { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            // Independent reference uses original, unnormalized coefficients in LibRaw's own pipeline.
            foreach (string value in new[] { "-r", customWb.Channel0.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                customWb.Channel1.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                customWb.Channel2.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                customWb.Channel3.ToString("R", System.Globalization.CultureInfo.InvariantCulture), "+M", "-o", "1", "-4", "-Z", referencePath, Path.GetFullPath(rawPath) })
                start.ArgumentList.Add(value);
            using var referenceProcess = System.Diagnostics.Process.Start(start)!;
            Task<string> referenceError = referenceProcess.StandardError.ReadToEndAsync();
            Task<string> referenceOutput = referenceProcess.StandardOutput.ReadToEndAsync();
            using var referenceTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            try { await referenceProcess.WaitForExitAsync(referenceTimeout.Token); }
            finally
            {
                if (!referenceProcess.HasExited) { referenceProcess.Kill(entireProcessTree: true); await referenceProcess.WaitForExitAsync(); }
                await Task.WhenAll(referenceError, referenceOutput);
            }
            Assert(referenceProcess.ExitCode == 0, "Independent LibRaw custom-WB reference succeeds: " + Path.GetFileName(rawPath));
            using var reference = LinearPpmDecoder.Read(referencePath);
            float maximumDifference = 0;
            for (int sample = 0; sample < reference.Pixels.Length; sample++)
                maximumDifference = Math.Max(maximumDifference, Math.Abs(reference.Pixels[sample] - customRaw.Pixels.Pixels[sample]));
            Assert(maximumDifference <= 4 / 65535f,
                "Normalized WB agrees with independent native reference within four 16-bit levels: " + Path.GetFileName(rawPath));
            Console.WriteLine($"WB: {Path.GetFileName(rawPath)}, reference max error {maximumDifference:G9}, >256 levels, culture-independent development");
        }
        finally { File.Delete(referencePath); }
        string wbExportPath = Path.Combine(Path.GetTempPath(), "photolab-wb-export-" + Guid.NewGuid().ToString("N") + ".jpg");
        string restoredDirectory = Path.Combine(Path.GetTempPath(), "photolab-restored-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(restoredDirectory);
        try
        {
            string restoredSource = Path.Combine(restoredDirectory, Path.GetFileName(rawPath));
            File.Copy(rawPath, restoredSource); // Own the sidecar test directory; never overwrite a user's existing edits.
            var savedPhoto = new PhotoEditState(new EditSettings { Exposure = 0.5 }, customWb);
            var stateStore = new EditFileStore();
            await stateStore.SaveStateAsync(restoredSource, savedPhoto);
            var restoredPhoto = await stateStore.LoadStateAsync(restoredSource);
            Assert(restoredPhoto == savedPhoto, "Actual RAW sidecar restores WB and exposure exactly: " + Path.GetFileName(rawPath));
            Assert(CameraWhiteBalanceAdjust.TryGetOffsets(asShot!, restoredPhoto.WhiteBalance, out double savedWarmth, out double savedTint) &&
                Math.Abs(savedWarmth - 50) < 0.0001 && Math.Abs(savedTint - 20) < 0.0001,
                "Actual RAW restored WB maps back to relative control values: " + Path.GetFileName(rawPath));
            using var restoredFull = await new PhotoImageLoader().LoadLinearFullAsync(restoredSource, restoredPhoto.WhiteBalance!);
            Assert(restoredFull.Pixels.SequenceEqual(customRaw.Pixels.Pixels),
                "Reopened RAW with persisted WB reproduces developed pixels: " + Path.GetFileName(rawPath));
            using var wbAdjusted = new FloatImageProcessor().Process(restoredFull, restoredPhoto.Adjustments);
            await PhotoExportStore.SaveAsync(restoredSource, wbExportPath, SkiaImageCodec.Encode(ImageConvert.ToSrgb8(wbAdjusted), jpeg: true));
            var wbExport = SkiaImageCodec.LoadFull(wbExportPath);
            Assert(wbExport.Width == raw.Width && wbExport.Height == raw.Height,
                "Custom WB float processing exports at full source resolution: " + Path.GetFileName(rawPath));
            using var restoredFile = File.OpenRead(restoredSource);
            Assert(System.Security.Cryptography.SHA256.HashData(restoredFile).SequenceEqual(before),
                "Persisted WB development/export preserves copied RAW bytes: " + Path.GetFileName(rawPath));
        }
        finally { File.Delete(wbExportPath); Directory.Delete(restoredDirectory, recursive: true); }
    }
    int levels = CheckRaw(raw);
    Assert(raw.Width > 0 && raw.Height > 0 && levels > 256, "Real RAW retains more than 256 red-channel levels: " + Path.GetFileName(rawPath));
    using var preview = ImageResize.Preview(raw, 600);
    using var adjusted = new FloatImageProcessor().Process(preview, new EditSettings { Exposure = 0.5 });
    byte[] encoded = SkiaImageCodec.Encode(ImageConvert.ToSrgb8(adjusted), jpeg: true);
    Assert(encoded.Length > 0, "Real RAW float preview and JPEG encoding: " + Path.GetFileName(rawPath));
    string exportPath = Path.Combine(Path.GetTempPath(), "photolab-raw-export-" + Guid.NewGuid().ToString("N") + ".jpg");
    try
    {
        using var fullAdjusted = new FloatImageProcessor().Process(raw, new EditSettings { Exposure = 0.5 });
        await PhotoExportStore.SaveAsync(rawPath, exportPath, SkiaImageCodec.Encode(ImageConvert.ToSrgb8(fullAdjusted), jpeg: true));
        var exported = SkiaImageCodec.LoadFull(exportPath);
        Assert(exported.Width == raw.Width && exported.Height == raw.Height,
            "Real RAW full-resolution atomic export decodes: " + Path.GetFileName(rawPath));
    }
    finally { if (File.Exists(exportPath)) File.Delete(exportPath); }
    using (var file = File.OpenRead(rawPath))
        Assert(System.Security.Cryptography.SHA256.HashData(file).SequenceEqual(before), "Real RAW source hash preserved: " + Path.GetFileName(rawPath));
    Console.WriteLine($"RAW: {Path.GetFileName(rawPath)} {raw.Width}x{raw.Height}, metadata/preview/full-export {elapsed.Elapsed.TotalSeconds:F2}s");
}
Console.WriteLine("All desktop tests passed.");
