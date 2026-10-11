using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotoLab.Core;
using PhotoLab.Imaging;
using PhotoLab.Imaging.Decoding;
using PhotoLab.Infrastructure;

namespace PhotoLab.Desktop;

public sealed class EditorWindow : Window
{
    private static readonly IBrush Bg = Solid("#10131A");
    private static readonly IBrush Panel = Solid("#181C25");
    private static readonly IBrush Muted = Solid("#9DA8BC");
    private static readonly IBrush Accent = Solid("#80AFFF");
    private static readonly IBrush Light = Solid("#EAF0FA");
    private static readonly IBrush Stroke = Solid("#303847");

    private readonly CpuImageProcessor _processor = new();
    private readonly FloatImageProcessor _floatProcessor = new();
    private readonly PreviewRenderer _renderer;
    private readonly EditFileStore _sidecars = new();
    private readonly PhotoImageLoader _loader;
    private readonly PresetStore _presets;
    private readonly ComboBox _presetList = new() { Name = "PresetList", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _presetName = new() { Name = "PresetName", Watermark = "Preset name" };
    private readonly Button _savePreset = new() { Name = "SavePreset", Content = "Save new" };
    private readonly Button _applyPreset = new() { Name = "ApplyPreset", Content = "Apply" };
    private readonly Button _deletePreset = new() { Name = "DeletePreset", Content = "Delete" };
    private readonly ToggleButton _previewPreset = new() { Name = "PreviewPreset", Content = "Preview preset" };
    private EditSettings? _presetPreview;
    private bool _presetBusy;
    private long _presetVersion;
    private readonly ListBox _photos = new() { Name = "PhotoList", Height = 150 };
    private readonly TextBlock _folderName = new() { Name = "FolderName", Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly Button _previousPhoto = new() { Name = "PreviousPhoto", Content = "← Prev" };
    private readonly Button _nextPhoto = new() { Name = "NextPhoto", Content = "Next →" };
    private CancellationTokenSource? _folderCts;
    private bool _syncingPhotos;
    private long _photoSelectionVersion;
    private readonly Dictionary<string,(PhotoSourceStamp Stamp,EditHistory.Snapshot History)> _photoDrafts = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _qualityTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly Dictionary<string, Slider> _sliders = new();
    private readonly EditHistory _history = new();
    private readonly Image _image = new() { Name = "PreviewImage", Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _fileName = Label("No image selected", 14, Light, true);
    private readonly TextBlock _dimensions = Label("Open JPEG, PNG or camera RAW to begin editing", 12, Muted);
    private readonly TextBlock _captureInfo = new() { Name = "CaptureInfo", Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = Label("Ready  •  CPU reference renderer", 12, Muted);
    private readonly TextBlock _editorCaption = Label("A quiet space for your photographs", 15, Muted);
    private readonly ToggleButton _before = new() { Name = "Original", Content = "Original", MinWidth = 95 };
    private readonly Button _undo = new() { Name = "Undo", Content = "↶ Undo", MinWidth = 82 };
    private readonly Button _redo = new() { Name = "Redo", Content = "↷ Redo", MinWidth = 82 };
    private readonly Button _save = new() { Name = "SaveEdits", Content = "Save edits", MinWidth = 96 };
    private readonly Button _export = new() { Content = "Export", MinWidth = 90 };
    private readonly CheckBox _png16Export = new() { Name = "Png16Export", Content = "16-bit PNG", Foreground = Light };
    private readonly HistogramView _histogram = new() { Name = "Histogram", Height = 85 };
    private readonly TextBlock _histogramClipping = new() { Name = "HistogramClipping", Foreground = Muted, FontSize = 11 };
    private readonly StackPanel _rawWhiteBalancePanel = new() { Name = "RawWhiteBalancePanel", Spacing = 8 };
    private readonly TextBox _rawWarmth = new() { Name = "RawWarmth", Text = "0", Watermark = "-100 to 100" };
    private readonly TextBox _rawTint = new() { Name = "RawTint", Text = "0", Watermark = "-100 to 100" };
    private readonly Slider _rawWarmthSlider = new() { Name = "RawWarmthSlider", Minimum = -100, Maximum = 100, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly Slider _rawTintSlider = new() { Name = "RawTintSlider", Minimum = -100, Maximum = 100, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly Button _applyWhiteBalance = new() { Name = "ApplyWhiteBalance", Content = "Apply WB" };
    private readonly Button _asShotWhiteBalance = new() { Name = "AsShotWhiteBalance", Content = "As shot" };
    private readonly TextBlock _whiteBalanceHint = new() { Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap };

    private FloatImageBuffer? _previewSource;
    private FloatImageBuffer? _asShotPreview;
    private RawMetadata? _asShotMetadata;
    private FloatImageBuffer? _interactiveSource;
    private Bitmap? _beforeBitmap;
    private Bitmap? _afterBitmap;
    private string? _sourcePath;
    private EditSettings _settings = new();
    private CameraWhiteBalance? _whiteBalance;
    private RawColorInfo? _cameraColor;
    private PhotoSourceStamp? _sourceStamp;
    private string? _displayedWarmth;
    private string? _displayedTint;
    private bool _syncing;
    private CancellationTokenSource? _loadCts;
    private bool _closed;
    private bool _pendingRawQuality;
    private long _photoGeneration;
    private CancellationTokenSource? _rawQualityCts;
    private bool _syncingWb;
    private bool _loadingWb;
    private bool _wbPumpRunning;
    private bool _wbRequestPending;
    private CameraWhiteBalance? _queuedWb;
    private bool _rawDragging;
    private bool _rawGroupOpen;
    private long _wbInputGeneration;
    private int _fullWidth, _fullHeight;

    public EditorWindow() : this(new PhotoImageLoader()) { }

    public EditorWindow(PhotoImageLoader loader, PresetStore? presets = null)
    {
        _loader = loader;
        _presets = presets ?? PresetStore.CreateDefault();
        _status.Name = "Status";
        _fileName.Name = "PhotoName";
        ToolTip.SetTip(_png16Export, "Preserve 16-bit color and alpha when saving PNG. JPEG remains 8-bit.");
        _renderer = new PreviewRenderer(_processor);
        Title = "PhotoLab — Multi-camera RAW editor prototype";
        Width = 1440; Height = 900; MinWidth = 940; MinHeight = 580;
        Background = Bg;

        var layout = new DockPanel();
        Content = layout;
        var top = BuildHeader();
        DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(top);
        var bottom = BuildFooter();
        DockPanel.SetDock(bottom, Dock.Bottom);
        layout.Children.Add(bottom);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("215,*,308") };
        var left = BuildLibraryPane();
        Grid.SetColumn(left, 0); grid.Children.Add(left);
        var center = BuildCanvas();
        Grid.SetColumn(center, 1); grid.Children.Add(center);
        var right = BuildSettingsPane();
        Grid.SetColumn(right, 2); grid.Children.Add(right);
        layout.Children.Add(grid);

        _qualityTimer.Tick += (_, _) => { _qualityTimer.Stop(); _ = UpgradePreviewAsync(); };
        _before.Checked += (_, _) => ShowCurrentBitmap();
        _before.Unchecked += (_, _) => ShowCurrentBitmap();
        _undo.Click += (_, _) => NavigateHistory(-1);
        _redo.Click += (_, _) => NavigateHistory(+1);
        _save.Click += async (_, _) => await SaveEditsAsync();
        _export.Click += async (_, _) => await ExportAsync();
        _savePreset.Click += async (_, _) => await SavePresetAsync();
        _applyPreset.Click += (_, _) =>
        {
            if (_presetList.SelectedItem is AdjustmentPreset preset)
            { _previewPreset.IsChecked = false;ApplyEdit(preset.Settings); }
        };
        _deletePreset.Click += async (_, _) => await DeletePresetAsync();
        _presetList.SelectionChanged += (_, _) => { _previewPreset.IsChecked = false;UpdateEnabled(); };
        _previousPhoto.Click += (_, _) => { if (_photos.SelectedIndex > 0) _photos.SelectedIndex--; };
        _nextPhoto.Click += (_, _) =>
        {
            if (_photos.SelectedIndex >= 0 && _photos.SelectedIndex + 1 < _photos.ItemCount) _photos.SelectedIndex++;
        };
        _photos.SelectionChanged += async (_, _) =>
        {
            UpdateEnabled();
            if (_syncingPhotos || _closed || _photos.SelectedItem is not FolderPhoto photo) return;
            long version = ++_photoSelectionVersion;
            await NavigateToPhotoAsync(photo.Path);
            if (!_closed && version == _photoSelectionVersion) SyncPhotoSelection();
        };
        _previewPreset.IsCheckedChanged += (_, _) =>
        {
            _presetPreview = _previewPreset.IsChecked == true && _presetList.SelectedItem is AdjustmentPreset preset
                ? preset.Settings : null;
            if (_presetPreview != null) _before.IsChecked = false;
            UpdateEnabled();RequestPreview();
        };
        Opened += async (_, _) => await RefreshPresetsAsync();
        _applyWhiteBalance.Click += (_, _) => ApplyWhiteBalance();
        _asShotWhiteBalance.Click += (_, _) => SetWhiteBalance(null);
        KeyDown += OnKeyDown;
        UpdateEnabled();
    }

    private Control BuildHeader()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Label("◉  PHOTOLAB", 19, Light, true));
        row.Children.Add(new Border { Width = 1, Height = 24, Margin = new Thickness(10, 0), Background = Stroke });
        row.Children.Add(Label("DEVELOP", 12, Accent, true));
        row.Children.Add(new Border { Width = 26, Background = Brushes.Transparent });
        var open = new Button { Content = "+  Open photo", MinWidth = 122 };
        open.Click += async (_, _) => await OpenAsync();
        row.Children.Add(open);
        row.Children.Add(_save);
        row.Children.Add(_export);
        var dock = new DockPanel();
        dock.Children.Add(row);
        return new Border { Background = Panel, BorderBrush = Stroke, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(22, 14), Child = dock };
    }

    private Control BuildLibraryPane()
    {
        var stack = new StackPanel { Spacing = 16, Margin = new Thickness(18, 20) };
        stack.Children.Add(Label("LIBRARY", 12, Accent, true));
        var openFolder = new Button { Name = "OpenFolder", Content = "Open folder" };
        openFolder.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Open photo folder", AllowMultiple = false });
            if (folders.Count == 0 || _closed) return;
            string? folder = folders[0].TryGetLocalPath();
            if (folder == null) { SetStatus("Please choose a local folder.");return; }
            await OpenFolderAsync(folder);
        };
        stack.Children.Add(openFolder);
        stack.Children.Add(_folderName);
        stack.Children.Add(_photos);
        stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4,
            Children = { _previousPhoto, _nextPhoto } });
        stack.Children.Add(new Border { Background = Solid("#252B38"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Child =
            new StackPanel { Spacing = 8, Children = { Label("CURRENT PHOTO", 10, Muted, true), _fileName, _dimensions, _captureInfo } } });
        stack.Children.Add(Label("WORKFLOW", 12, Accent, true));
        stack.Children.Add(Label("1   Import JPEG / PNG / RAW", 12, Light));
        stack.Children.Add(Label("2   Adjust develop sliders", 12, Light));
        stack.Children.Add(Label("3   Save sidecar settings", 12, Light));
        stack.Children.Add(Label("4   Export photo", 12, Light));
        stack.Children.Add(_png16Export);
        stack.Children.Add(Label("BASIC PRESETS", 12, Accent, true));
        stack.Children.Add(_presetList);
        stack.Children.Add(_previewPreset);
        stack.Children.Add(_presetName);
        stack.Children.Add(_savePreset);
        stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4,
            Children = { _applyPreset, _deletePreset } });
        stack.Children.Add(Label("Basic controls only. Camera RAW WB stays with this photo.", 11, Muted));
        stack.Children.Add(new Border { Height = 1, Background = Stroke, Margin = new Thickness(0, 4) });
        stack.Children.Add(Label("NON-DESTRUCTIVE", 11, Accent, true));
        stack.Children.Add(new TextBlock { Text = "The original file stays untouched. Edits are stored in a separate .photolab.json file.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 19 });
        return new Border { Background = Panel, BorderBrush = Stroke, BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
    }

    private Control BuildCanvas()
    {
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 12) };
        tools.Children.Add(_before);
        tools.Children.Add(_undo);
        tools.Children.Add(_redo);
        Grid.SetRow(tools, 0); body.Children.Add(tools);

        var photoPanel = new Grid { Background = Solid("#0C0F15"), Margin = new Thickness(16, 0, 16, 16) };
        photoPanel.Children.Add(_image);
        photoPanel.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = _editorCaption, IsHitTestVisible = false });
        Grid.SetRow(photoPanel, 1); body.Children.Add(photoPanel);
        return body;
    }

    private Control BuildSettingsPane()
    {
        var stack = new StackPanel { Spacing = 10, Margin = new Thickness(20, 18) };
        stack.Children.Add(Label("DEVELOP CONTROLS", 13, Light, true));
        stack.Children.Add(Label("Edited preview · linear RGB / Y", 11, Muted));
        stack.Children.Add(_histogram);
        stack.Children.Add(Label("R red · G green · B blue · Y gray · log count", 10, Muted));
        stack.Children.Add(_histogramClipping);
        stack.Children.Add(Label("Light", 13, Accent, true));
        AddSlider(stack, "Exposure", "EV", -4, 4, 0.05, s => s.Exposure, (s,v) => s with { Exposure = v });
        AddSlider(stack, "Contrast", "", -100, 100, 1, s => s.Contrast, (s,v) => s with { Contrast = v });
        AddSlider(stack, "Highlights", "", -100, 100, 1, s => s.Highlights, (s,v) => s with { Highlights = v });
        AddSlider(stack, "Shadows", "", -100, 100, 1, s => s.Shadows, (s,v) => s with { Shadows = v });
        stack.Children.Add(new Border { Height = 1, Background = Stroke, Margin = new Thickness(0, 6) });
        _rawWhiteBalancePanel.Children.Add(Label("RAW white balance", 12, Light, true));
        _rawWhiteBalancePanel.Children.Add(Label("Warmth (relative to as shot)", 11, Muted));
        _rawWhiteBalancePanel.Children.Add(_rawWarmth);
        _rawWhiteBalancePanel.Children.Add(_rawWarmthSlider);
        _rawWhiteBalancePanel.Children.Add(Label("Tint (green / magenta)", 11, Muted));
        _rawWhiteBalancePanel.Children.Add(_rawTint);
        _rawWhiteBalancePanel.Children.Add(_rawTintSlider);
        ConfigureWbSlider(_rawWarmthSlider);
        ConfigureWbSlider(_rawTintSlider);
        _rawWhiteBalancePanel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { _applyWhiteBalance, _asShotWhiteBalance } });
        _rawWhiteBalancePanel.Children.Add(_whiteBalanceHint);
        stack.Children.Add(_rawWhiteBalancePanel);
        stack.Children.Add(Label("Color adjustments", 13, Accent, true));
        AddSlider(stack, "Temperature", "", -100, 100, 1, s => s.Temperature, (s,v) => s with { Temperature = v });
        AddSlider(stack, "Tint", "", -100, 100, 1, s => s.Tint, (s,v) => s with { Tint = v });
        AddSlider(stack, "Saturation", "", -100, 100, 1, s => s.Saturation, (s,v) => s with { Saturation = v });
        AddSlider(stack, "Vibrance", "", -100, 100, 1, s => s.Vibrance, (s,v) => s with { Vibrance = v });
        var reset = new Button { Content = "Reset all adjustments", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 16, 0, 0) };
        reset.Click += (_, _) => ResetEdits();
        stack.Children.Add(reset);
        stack.Children.Add(new TextBlock { Text = "Processing: linear sRGB float · CPU. RAW imports retain 16-bit samples; ICC and GPU are pending.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 15, 0, 0) });
        return new Border { Background = Panel, BorderBrush = Stroke, BorderThickness = new Thickness(1, 0, 0, 0), Child = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
    }

    private Control BuildFooter()
    {
        return new Border { Background = Panel, BorderBrush = Stroke, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(22, 8), Child = _status };
    }

    private void AddSlider(StackPanel panel, string name, string suffix, double min, double max, double step,
                           Func<EditSettings, double> read, Func<EditSettings, double, EditSettings> write)
    {
        var value = Label("0", 11, Muted);
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(Label(name, 12, Light));
        Grid.SetColumn(value, 1); heading.Children.Add(value);
        var slider = new Slider { Name = "Tone" + name, Minimum = min, Maximum = max, Value = 0, TickFrequency = step,
            IsSnapToTickEnabled = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        // Tunnel through the slider's thumb so a complete pointer drag is one history entry.
        slider.AddHandler(PointerPressedEvent, (_, _) => _history.BeginGroup(), RoutingStrategies.Tunnel);
        slider.AddHandler(PointerReleasedEvent, (_, _) => _history.EndGroup(), RoutingStrategies.Tunnel);
        slider.AddHandler(Thumb.DragCompletedEvent, (_, _) => _history.EndGroup(), RoutingStrategies.Bubble, handledEventsToo: true);
        slider.ValueChanged += (_, e) =>
        {
            value.Text = e.NewValue.ToString(step < 1 ? "+0.00;-0.00;0.00" : "+0;-0;0", CultureInfo.InvariantCulture) + suffix;
            if (_syncing || _previewSource == null || _loadCts != null) return;
            ApplyEdit(write(_settings, e.NewValue), fromSlider: true);
        };
        _sliders[name] = slider;
        panel.Children.Add(heading);
        panel.Children.Add(slider);
    }

    private void SyncSliders()
    {
        _syncing = true;
        _sliders["Exposure"].Value = _settings.Exposure;
        _sliders["Contrast"].Value = _settings.Contrast;
        _sliders["Highlights"].Value = _settings.Highlights;
        _sliders["Shadows"].Value = _settings.Shadows;
        _sliders["Temperature"].Value = _settings.Temperature;
        _sliders["Tint"].Value = _settings.Tint;
        _sliders["Saturation"].Value = _settings.Saturation;
        _sliders["Vibrance"].Value = _settings.Vibrance;
        _syncing = false;
    }

    private async Task OpenAsync()
    {
        var items = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open photograph",
            AllowMultiple = false,
            FileTypeFilter = [
                new FilePickerFileType("All photographs") { Patterns = PhotoFormats.AllPatterns },
                new FilePickerFileType("Camera RAW") { Patterns = PhotoFormats.RawPatterns },
                new FilePickerFileType("JPEG / PNG") { Patterns = PhotoFormats.StandardPatterns }
            ]
        });
        if (items.Count == 0) return;
        var path = items[0].TryGetLocalPath();
        if (path == null) { SetStatus("Please choose a local file."); return; }
        if (_closed) return;

        await OpenPhotoAsync(path);
    }

    /// <summary>Open a local photo on the UI thread without requiring a platform file-picker interaction.</summary>
    public Task OpenPhotoAsync(string path)
    {
        CancelWbInput();
        return LoadPhotoAsync(path);
    }

    /// <summary>Discover local photos without a platform picker; old results never replace a newer folder request.</summary>
    public async Task OpenFolderAsync(string directory)
    {
        if (_closed) return;
        CancelWbInput();
        _photoSelectionVersion++;
        _loadCts?.Cancel();
        _renderer.Invalidate();
        _folderCts?.Cancel();
        var cancellation = new CancellationTokenSource();_folderCts = cancellation;
        try
        {
            SetStatus("Reading photo folder...");
            var photos = await PhotoFolder.ScanAsync(directory,cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested || _folderCts != cancellation) return;
            _syncingPhotos = true;
            try
            {
                _photos.ItemsSource = photos;
                _photos.SelectedItem = photos.FirstOrDefault(p => p.Path == _sourcePath);
                _folderName.Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory))) + $" · {photos.Count} photos";
            }
            finally { _syncingPhotos = false; }
            UpdateEnabled();
            if (_photos.SelectedIndex < 0 && photos.Count > 0)
            {
                _syncingPhotos = true;
                try { _photos.SelectedIndex = 0; }
                finally { _syncingPhotos = false; }
                await NavigateToPhotoAsync(photos[0].Path);
                if (!_closed && _folderCts == cancellation) SyncPhotoSelection();
            }
            else if (photos.Count == 0) SetStatus("No supported photo filenames in this folder.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { if (!_closed && _folderCts == cancellation) SetStatus("Folder error: " + error.Message); }
        finally
        {
            if (_folderCts == cancellation) _folderCts = null;
            cancellation.Dispose();
        }
    }

    private void SyncPhotoSelection()
    {
        if (_photos.ItemsSource is not IReadOnlyList<FolderPhoto> photos) return;
        _syncingPhotos = true;
        try { _photos.SelectedItem = photos.FirstOrDefault(p => p.Path == _sourcePath); }
        finally { _syncingPhotos = false; }
        UpdateEnabled();
    }

    private async Task NavigateToPhotoAsync(string path)
    {
        CancelWbInput();
        if (_sourceStamp != null)
            _photoDrafts[_sourceStamp.Path] = (_sourceStamp,_history.Capture());
        try
        {
            var stamp = PhotoSourceStamp.Read(path);
            if (_photoDrafts.TryGetValue(stamp.Path,out var draft) && draft.Stamp == stamp)
                await LoadPhotoAsync(path,draft.History.CurrentState,restoreHistory:draft.History);
            else await LoadPhotoAsync(path);
        }
        catch (Exception error) { if (!_closed) SetStatus("Photo error: " + error.Message); }
    }

    private async Task LoadPhotoAsync(string path, PhotoEditState? requested = null, int historyStep = 0, bool wbInput = false, EditHistory.Snapshot? restoreHistory = null)
    {
        if (_closed) return;

        _photoGeneration++;
        _rawQualityCts?.Cancel();
        // Keep the accepted photo's pending upgrade until a replacement succeeds.
        _loadCts?.Cancel();
        var cancellation = new CancellationTokenSource();
        _loadCts = cancellation;
        _previewPreset.IsChecked = false;
        _loadingWb = wbInput;
        _qualityTimer.Stop();
        _renderer.Invalidate();
        UpdateEnabled();
        FloatImageBuffer? nextPreview = null;
        FloatImageBuffer? nextInteractive = null;
        FloatImageBuffer? nextAsShot = null;
        RawMetadata? nextAsShotMetadata = null;
        try
        {
            SetStatus("Opening image...");
            var stamp = PhotoSourceStamp.Read(path);
            if (requested == null) await _loader.ResetAsync(cancellation.Token);
            var edits = requested ?? await _sidecars.LoadStateAsync(path, cancellation.Token);
            bool cachedAsShot = wbInput && edits.WhiteBalance == null && _sourceStamp == stamp && _asShotPreview != null;
            bool reuseOriginal = requested != null && _sourceStamp == stamp && _beforeBitmap != null && (edits.WhiteBalance != null || cachedAsShot);
            bool fastRaw = reuseOriginal && edits.WhiteBalance != null && _loader.SupportsRawPreview;
            (FloatImageBuffer preview, int originalWidth, int originalHeight, RawMetadata? metadata) loaded;
            PixelBuffer? originalPixels = null;
            if (reuseOriginal)
            {
                // A bounded 600px copy on the UI thread keeps the cache safe from concurrent photo-close disposal.
                loaded = cachedAsShot ? (ImageResize.Preview(_asShotPreview!, 600, cancellation.Token), _fullWidth, _fullHeight, _asShotMetadata)
                    : fastRaw ? await _loader.LoadInteractivePreviewAsync(path, edits.WhiteBalance!, cancellation.Token)
                    : await _loader.LoadPreviewWithMetadataAsync(path, edits.WhiteBalance!, cancellation.Token);
                nextPreview = loaded.preview;
            }
            else
            {
                loaded = await _loader.LoadPreviewWithMetadataAsync(path, cancellation.Token);
                nextPreview = loaded.preview;
                if (PhotoFormats.IsRaw(path) && _loader.SupportsRawPreview)
                {
                    nextAsShotMetadata = loaded.metadata;
                    nextAsShot = await Task.Run(() => ImageResize.Preview(loaded.preview, 600, cancellation.Token), cancellation.Token);
                }
                // Original comparison uses camera-default WB, never the custom-WB preview.
                originalPixels = await Task.Run(() => ImageConvert.ToSrgb8(loaded.preview), cancellation.Token);
                if (edits.WhiteBalance != null)
                {
                    loaded = await _loader.LoadPreviewWithMetadataAsync(path, edits.WhiteBalance, cancellation.Token);
                    nextPreview.Dispose(); nextPreview = loaded.preview;
                }
            }
            // Cache both preview sizes once; sliders never trigger source decoding or resizing.
            var display = await Task.Run(() => (
                original: originalPixels,
                interactive: ImageResize.Preview(loaded.preview, maxEdge: 600, cancellation.Token)), cancellation.Token);
            nextInteractive = display.interactive;
            if (_closed || cancellation.IsCancellationRequested || _loadCts != cancellation) return;
            if (stamp != PhotoSourceStamp.Read(path)) throw new IOException("Photo changed while loading. Reopen it.");
            await _renderer.DrainAsync();
            if (_closed || cancellation.IsCancellationRequested || _loadCts != cancellation) return;
            var beforeBitmap = reuseOriginal ? _beforeBitmap! : PreviewBitmap.Create(display.original!);
            _previewSource?.Dispose(); _interactiveSource?.Dispose();
            if (!reuseOriginal)
            {
                _asShotPreview?.Dispose(); _asShotPreview = nextAsShot; nextAsShot = null;
                _asShotMetadata = nextAsShotMetadata;
            }
            _sourcePath = path;
            SyncPhotoSelection();
            _sourceStamp = stamp;
            _previewSource = loaded.preview;
            _interactiveSource = display.interactive;
            nextPreview = null; nextInteractive = null;
            _fullWidth = loaded.originalWidth; _fullHeight = loaded.originalHeight;
            _settings = edits.Adjustments;
            _whiteBalance = edits.WhiteBalance;
            _pendingRawQuality = fastRaw || cachedAsShot;
            _cameraColor = loaded.metadata?.CameraColor;
            SyncWhiteBalance();
            if (restoreHistory != null) _history.Restore(restoreHistory);
            else if (requested == null) _history.ResetState(edits);
            else if (historyStep != 0) _history.Move(historyStep);
            else _history.ApplyState(edits);
            _fileName.Text = Path.GetFileName(path);
            _dimensions.Text = $"{_fullWidth:N0} × {_fullHeight:N0} px" +
                (PhotoFormats.IsRaw(path) ? " · RAW via LibRaw" : "") +
                (loaded.metadata?.CameraModel != null ? $" · {loaded.metadata.CameraMake} {loaded.metadata.CameraModel}" : "");
            // Only display extracted capture values; missing fields never become guessed defaults.
            var capture = new List<string>();
            if (loaded.metadata?.IsoSpeed is double iso) capture.Add($"ISO {iso.ToString("0.#", CultureInfo.InvariantCulture)}");
            if (loaded.metadata?.ExposureSeconds is double seconds)
                capture.Add(seconds < 1 ? $"1/{(1 / seconds).ToString("0.#", CultureInfo.InvariantCulture)} s"
                    : $"{seconds.ToString("0.###", CultureInfo.InvariantCulture)} s");
            if (loaded.metadata?.Aperture is double aperture) capture.Add($"f/{aperture.ToString("0.#", CultureInfo.InvariantCulture)}");
            _captureInfo.Text = string.Join(" · ", capture);
            if (_whiteBalance != null) _captureInfo.Text += (capture.Count > 0 ? " · " : "") + "Custom WB";
            _captureInfo.IsVisible = capture.Count > 0 || _whiteBalance != null;
            var oldBefore = _beforeBitmap;
            var oldAfter = _afterBitmap;
            _beforeBitmap = beforeBitmap;
            // A same-photo WB refresh must keep its accepted frame until RenderPreviewAsync replaces it.
            if (!reuseOriginal)
            {
                _afterBitmap = null;
                _histogram.SetHistogram(null); _histogramClipping.Text = "";
            }
            _editorCaption.IsVisible = false;
            _before.IsChecked = false;
            // Detach retired bitmaps before the await below lets Avalonia render another frame.
            ShowCurrentBitmap();
            if (!ReferenceEquals(oldBefore, beforeBitmap)) oldBefore?.Dispose();
            if (!reuseOriginal) oldAfter?.Dispose();
            SyncSliders(); UpdateEnabled();
            await RenderPreviewAsync(fullQuality: true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { if (!_closed && _loadCts == cancellation) SetStatus("Open error: " + ex.Message); }
        finally
        {
            if (_loadCts == cancellation)
            {
                _loadCts = null;
                _loadingWb = false;
                if (!_closed && _pendingRawQuality) { _qualityTimer.Stop(); _qualityTimer.Start(); }
            }
            nextPreview?.Dispose(); nextInteractive?.Dispose(); nextAsShot?.Dispose();
            cancellation.Dispose();
            if (!_closed) UpdateEnabled();
        }
    }

    private void ApplyEdit(EditSettings next, bool fromSlider = false)
    {
        if (_closed || _loadCts != null || _previewSource == null || next == _settings) return;
        _previewPreset.IsChecked = false;
        _settings = next;
        if (!fromSlider) _history.EndGroup();
        _history.Apply(next);
        if (!fromSlider) SyncSliders();
        UpdateEnabled();
        RequestPreview();
    }

    private void NavigateHistory(int step)
    {
        if (_closed || _loadCts != null) return;
        _previewPreset.IsChecked = false;
        _history.EndGroup();
        var target = _history.Peek(step);
        if (target == null) return;
        if (target.WhiteBalance != _whiteBalance && _sourcePath != null)
        {
            _ = LoadPhotoAsync(_sourcePath, target, step);
            return;
        }
        if (!_history.Move(step)) return;
        _settings = _history.Current;
        SyncSliders(); UpdateEnabled();
        RequestPreview();
    }

    private void ResetEdits()
    {
        if (_closed || _loadCts != null || _sourcePath == null) return;
        if (_whiteBalance == null) ApplyEdit(new EditSettings());
        else
        {
            _history.EndGroup();
            _ = LoadPhotoAsync(_sourcePath, new PhotoEditState(new EditSettings()));
        }
    }

    private void ApplyWhiteBalance()
    {
        if (_closed || _loadCts != null || _cameraColor == null) return;
        if (!double.TryParse(_rawWarmth.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double warmth) ||
            !double.TryParse(_rawTint.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double tint))
        { SetStatus("Enter warmth and tint between -100 and 100."); return; }
        // Formatting saved ratios must not turn an unchanged Apply click into a new WB/history entry.
        if (_rawWarmth.Text == _displayedWarmth && _rawTint.Text == _displayedTint) return;
        try
        {
            var wb = CameraWhiteBalanceAdjust.Apply(_cameraColor.AsShot, warmth, tint);
            SetWhiteBalance(warmth == 0 && tint == 0 ? null : wb);
        }
        catch (ArgumentException) { SetStatus("Enter finite warmth and tint between -100 and 100."); }
    }

    private void SetWhiteBalance(CameraWhiteBalance? whiteBalance)
    {
        if (_closed || _loadCts != null || _sourcePath == null || !PhotoFormats.IsRaw(_sourcePath)) return;
        if (whiteBalance == _whiteBalance) return;
        _history.EndGroup();
        _ = LoadPhotoAsync(_sourcePath, new PhotoEditState(_settings, whiteBalance));
    }

    private void SyncWhiteBalance()
    {
        // Between fast developments the pump can be idle while the pointer is still held.
        // Keep both input values exact instead of feeding gain-inversion rounding into the next request.
        if (_rawDragging || (_wbPumpRunning && _wbRequestPending)) return;
        _syncingWb = true;
        if (_cameraColor != null && CameraWhiteBalanceAdjust.TryGetOffsets(_cameraColor.AsShot, _whiteBalance, out double warmth, out double tint))
        {
            _rawWarmth.Text = warmth.ToString("0.###", CultureInfo.CurrentCulture);
            _rawTint.Text = tint.ToString("0.###", CultureInfo.CurrentCulture);
            _rawWarmthSlider.Value = warmth; _rawTintSlider.Value = tint;
        }
        else { _rawWarmth.Text = ""; _rawTint.Text = ""; }
        _syncingWb = false;
        _displayedWarmth = _rawWarmth.Text; _displayedTint = _rawTint.Text;
        _whiteBalanceHint.Text = _cameraColor == null ? "Camera white balance is unavailable for this file."
            : _rawWarmth.Text == "" ? "Saved custom WB is outside these controls. Enter offsets or choose As shot."
            : _loader.SupportsRawPreview ? "Relative to as-shot WB. Drag to preview; release for full quality."
            : "Relative to as-shot WB. Apply to redevelop the RAW.";
    }

    private void ConfigureWbSlider(Slider slider)
    {
        slider.AddHandler(PointerPressedEvent, (_, _) =>
        {
            if (_closed || _cameraColor == null) return;
            _rawDragging = true;
            if (!_rawGroupOpen) { _history.BeginGroup(); _rawGroupOpen = true; }
        }, RoutingStrategies.Tunnel);
        slider.AddHandler(PointerReleasedEvent, (_, _) => EndWbDrag(), RoutingStrategies.Tunnel);
        slider.AddHandler(Thumb.DragCompletedEvent, (_, _) => EndWbDrag(), RoutingStrategies.Bubble, handledEventsToo: true);
        slider.ValueChanged += (_, _) =>
        {
            if (_syncingWb || _closed || _cameraColor == null || !_loader.SupportsRawPreview ||
                (_loadCts != null && !_loadingWb)) return;
            _rawWarmth.Text = _rawWarmthSlider.Value.ToString("0.###", CultureInfo.CurrentCulture);
            _rawTint.Text = _rawTintSlider.Value.ToString("0.###", CultureInfo.CurrentCulture);
            _displayedWarmth = _rawWarmth.Text; _displayedTint = _rawTint.Text;
            if (!_rawGroupOpen) { _history.BeginGroup(); _rawGroupOpen = true; }
            var wb = CameraWhiteBalanceAdjust.Apply(_cameraColor.AsShot, _rawWarmthSlider.Value, _rawTintSlider.Value);
            _queuedWb = _rawWarmthSlider.Value == 0 && _rawTintSlider.Value == 0 ? null : wb;
            _wbRequestPending = true;
            _qualityTimer.Stop();
            if (!_wbPumpRunning) _ = DrainWbInputAsync();
        };
    }

    private void EndWbDrag()
    {
        _rawDragging = false;
        if (!_wbPumpRunning && !_wbRequestPending) SyncWhiteBalance();
        if (!_wbPumpRunning && !_wbRequestPending && _rawGroupOpen)
        { _history.EndGroup(); _rawGroupOpen = false; }
        if (!_wbPumpRunning && _pendingRawQuality) { _qualityTimer.Stop(); _qualityTimer.Start(); }
    }

    private async Task DrainWbInputAsync()
    {
        // Keep one active development and one latest value, with a single history group for the drag.
        _wbPumpRunning = true;
        long generation = _wbInputGeneration;
        try
        {
            while (!_closed && generation == _wbInputGeneration && _wbRequestPending && _sourcePath != null)
            {
                var wb = _queuedWb;
                _wbRequestPending = false;
                if (wb != _whiteBalance)
                    await LoadPhotoAsync(_sourcePath, new PhotoEditState(_settings, wb), wbInput: true);
            }
        }
        finally
        {
            _wbPumpRunning = false;
            if (!_closed && generation == _wbInputGeneration)
            {
                SyncWhiteBalance();
                EndWbDragIfFinished();
                if (!_rawDragging && _pendingRawQuality) { _qualityTimer.Stop(); _qualityTimer.Start(); }
            }
            if (!_closed)
            {
                UpdateEnabled();
                if (_wbRequestPending) _ = DrainWbInputAsync();
            }
        }
    }

    private void EndWbDragIfFinished()
    {
        if (!_rawDragging && _rawGroupOpen) { _history.EndGroup(); _rawGroupOpen = false; }
    }

    private void CancelWbInput()
    {
        _wbInputGeneration++;
        _wbRequestPending = false; _rawDragging = false;
        if (_rawGroupOpen) { _history.EndGroup(); _rawGroupOpen = false; }
    }

    private void RequestPreview()
    {
        // Render during input; the idle timer only upgrades the final preview quality.
        _qualityTimer.Stop(); _qualityTimer.Start();
        _ = RenderPreviewAsync();
    }

    private async Task UpgradePreviewAsync()
    {
        if (_rawDragging || _wbPumpRunning || _wbRequestPending) return;
        if (!_pendingRawQuality) { await RenderPreviewAsync(fullQuality: true); return; }
        if (_closed || _loadCts != null || _sourcePath == null || _rawQualityCts != null) return;
        string path = _sourcePath;
        var wb = _whiteBalance;
        var stamp = _sourceStamp;
        long generation = _photoGeneration;
        var cancellation = new CancellationTokenSource();
        _rawQualityCts = cancellation;
        FloatImageBuffer? next = null;
        try
        {
            if (stamp != PhotoSourceStamp.Read(path)) throw new IOException("Photo changed. Reopen it before upgrading the preview.");
            var loaded = wb == null ? await _loader.LoadPreviewWithMetadataAsync(path, cancellation.Token)
                : await _loader.LoadPreviewWithMetadataAsync(path, wb, cancellation.Token);
            next = loaded.preview;
            if (_closed || generation != _photoGeneration || cancellation.IsCancellationRequested) return;
            if (stamp != PhotoSourceStamp.Read(path)) throw new IOException("Photo changed during preview development. Reopen it.");
            await _renderer.DrainAsync();
            if (_closed || generation != _photoGeneration || cancellation.IsCancellationRequested) return;
            var previous = _previewSource;
            _previewSource = next; next = null;
            _pendingRawQuality = false;
            previous?.Dispose(); // interactive source is a separate owned copy, still valid for tone input.
            await RenderPreviewAsync(fullQuality: true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        { if (!_closed && generation == _photoGeneration) SetStatus("Quality preview error: " + error.Message); }
        finally
        {
            next?.Dispose();
            if (_rawQualityCts == cancellation) _rawQualityCts = null;
            cancellation.Dispose();
            // A newer WB's idle tick may have occurred while this obsolete quality job was still running.
            if (!_closed && generation != _photoGeneration && _pendingRawQuality && _loadCts == null)
            { _qualityTimer.Stop(); _qualityTimer.Start(); }
        }
    }

    private async Task RenderPreviewAsync(bool fullQuality = false)
    {
        var frame = fullQuality ? _previewSource : _interactiveSource;
        if (frame == null || _closed) return;
        try
        {
            SetStatus("Rendering preview...");
            var rendered = await _renderer.RenderAsync(frame, _presetPreview ?? _settings);
            if (_closed || rendered == null || !_renderer.IsCurrent(rendered.Version)) return;
            var bitmap = PreviewBitmap.Create(rendered.Pixels);
            var previous = _afterBitmap;
            _afterBitmap = bitmap;
            _histogram.SetHistogram(rendered.Histogram);
            if (rendered.Histogram is { } histogram)
            {
                double total = Math.Max(1, histogram.PixelCount);
                _histogramClipping.Text = $"RGB ≤0: {histogram.ShadowClipped / total:P1}   ≥1: {histogram.HighlightClipped / total:P1}";
            }
            ShowCurrentBitmap();
            previous?.Dispose();
            SetStatus($"{(_presetPreview == null ? "Ready" : "Preset preview — apply to keep")}  •  {_fullWidth:N0} × {_fullHeight:N0}  •  preview {frame.Width:N0} × {frame.Height:N0}  •  " +
                (PhotoFormats.IsRaw(_sourcePath ?? "") ? $"LibRaw → linear float {_renderer.BackendName}" : $"linear float {_renderer.BackendName}"));
        }
        catch (Exception ex) { if (!_closed) SetStatus("Preview error: " + ex.Message); }
    }

    private void ShowCurrentBitmap() => _image.Source = _before.IsChecked == true ? _beforeBitmap : _afterBitmap ?? _beforeBitmap;

    private async Task SaveEditsAsync()
    {
        if (_closed || _loadCts != null || _sourcePath == null || _presetPreview != null) return;
        try
        {
            await _sidecars.SaveStateAsync(_sourcePath, new PhotoEditState(_settings, _whiteBalance));
            SetStatus("Edits saved to " + Path.GetFileName(EditFileStore.GetPath(_sourcePath)));
        }
        catch (Exception ex) { SetStatus("Save error: " + ex.Message); }
    }

    private async Task RefreshPresetsAsync(string? selectedName = null)
    {
        long version = ++_presetVersion;
        try
        {
            var items = await _presets.ListAsync();
            if (_closed || version != _presetVersion) return;
            _presetList.ItemsSource = items;
            _presetList.SelectedItem = items.FirstOrDefault(p => p.Name.Equals(selectedName,StringComparison.OrdinalIgnoreCase));
            UpdateEnabled();
        }
        catch (Exception error) { if (!_closed && version == _presetVersion) SetStatus("Preset error: " + error.Message); }
    }

    private async Task SavePresetAsync()
    {
        if (_presetBusy || _closed || _loadCts != null || _previewSource == null) return;
        _presetBusy = true;UpdateEnabled();
        string name = _presetName.Text ?? "";
        try
        {
            await _presets.SaveAsync(name,_settings);
            await RefreshPresetsAsync(name.Trim());
            if (!_closed) SetStatus("Preset saved: " + name.Trim());
        }
        catch (Exception error) { if (!_closed) SetStatus("Preset error: " + error.Message); }
        finally { _presetBusy = false;if (!_closed) UpdateEnabled(); }
    }

    private async Task DeletePresetAsync()
    {
        if (_presetBusy || _closed || _presetList.SelectedItem is not AdjustmentPreset preset) return;
        _presetBusy = true;UpdateEnabled();
        try
        {
            await _presets.DeleteAsync(preset.Name);
            await RefreshPresetsAsync();
            if (!_closed) SetStatus("Preset deleted: " + preset.Name);
        }
        catch (Exception error) { if (!_closed) SetStatus("Preset error: " + error.Message); }
        finally { _presetBusy = false;if (!_closed) UpdateEnabled(); }
    }

    private async Task ExportAsync()
    {
        if (_closed || _loadCts != null || _sourcePath == null || _presetPreview != null) return;
        var originalPath = _sourcePath;
        var snapshot = _settings;
        var whiteBalance = _whiteBalance;
        bool png16 = _png16Export.IsChecked == true;
        var result = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export edited photograph",
            SuggestedFileName = Path.GetFileNameWithoutExtension(originalPath) + "_edited.jpg",
            DefaultExtension = "jpg",
            FileTypeChoices = [
                new FilePickerFileType("JPEG image") { Patterns = ["*.jpg", "*.jpeg"] },
                new FilePickerFileType("PNG image") { Patterns = ["*.png"] }
            ]
        });
        if (result == null) return;
        var destinationPath = result.TryGetLocalPath();
        if (destinationPath == null) { SetStatus("Please choose a local export file."); return; }
        bool jpeg = !result.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        try
        {
            SetStatus("Exporting full-resolution image...");
            using var original = whiteBalance == null ? await _loader.LoadLinearFullAsync(originalPath)
                : await _loader.LoadLinearFullAsync(originalPath, whiteBalance);
            if (!jpeg && png16)
            {
                using var adjusted = await Task.Run(() => _floatProcessor.Process(original, snapshot));
                await PhotoExportStore.SavePng16Async(originalPath,destinationPath,adjusted);
            }
            else
            {
                byte[] bytes = await Task.Run(() =>
                {
                    using var adjusted = _floatProcessor.Process(original, snapshot);
                    return SkiaImageCodec.Encode(ImageConvert.ToSrgb8(adjusted), jpeg);
                });
                await PhotoExportStore.SaveAsync(originalPath, destinationPath, bytes);
            }
            SetStatus("Export complete: " + result.Name);
        }
        catch (Exception ex) { SetStatus("Export error: " + ex.Message); }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (!ctrl) return;
        if (e.Key == Key.O) { _ = OpenAsync(); e.Handled = true; }
        else if (e.Key == Key.S) { _ = SaveEditsAsync(); e.Handled = true; }
        else if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { NavigateHistory(+1); e.Handled = true; }
        else if (e.Key == Key.Z) { NavigateHistory(-1); e.Handled = true; }
    }

    private void UpdateEnabled()
    {
        _previousPhoto.IsEnabled = _photos.SelectedIndex > 0;
        _nextPhoto.IsEnabled = _photos.SelectedIndex >= 0 && _photos.SelectedIndex + 1 < _photos.ItemCount;
        bool hasFile = _previewSource != null && _loadCts == null && !_wbPumpRunning;
        _before.IsEnabled = hasFile;
        _save.IsEnabled = hasFile && _presetPreview == null;
        _export.IsEnabled = hasFile && _presetPreview == null;
        _savePreset.IsEnabled = hasFile && !_presetBusy && _presetPreview == null;
        _previewPreset.IsEnabled = hasFile && !_presetBusy && _presetList.SelectedItem is AdjustmentPreset;
        _applyPreset.IsEnabled = hasFile && !_presetBusy && _presetList.SelectedItem is AdjustmentPreset;
        _deletePreset.IsEnabled = !_presetBusy && _presetList.SelectedItem is AdjustmentPreset;
        _undo.IsEnabled = hasFile && _history.CanUndo;
        _redo.IsEnabled = hasFile && _history.CanRedo;
        foreach (var slider in _sliders.Values) slider.IsEnabled = hasFile;
        bool isRaw = PhotoFormats.IsRaw(_sourcePath ?? "");
        _rawWhiteBalancePanel.IsVisible = isRaw;
        _rawWarmth.IsEnabled = _rawTint.IsEnabled = _applyWhiteBalance.IsEnabled = hasFile && isRaw && _cameraColor != null;
        _asShotWhiteBalance.IsEnabled = hasFile && isRaw && _whiteBalance != null;
        // An unrepresentable saved WB has no valid thumb position; preserve it until an explicit reset/input.
        bool canSlide = _previewSource != null && (_loadCts == null || _loadingWb) && isRaw && _cameraColor != null && _loader.SupportsRawPreview &&
            CameraWhiteBalanceAdjust.TryGetOffsets(_cameraColor.AsShot, _whiteBalance, out _, out _);
        _rawWarmthSlider.IsVisible = _rawTintSlider.IsVisible = _loader.SupportsRawPreview;
        _rawWarmthSlider.IsEnabled = _rawTintSlider.IsEnabled = canSlide;
    }

    private void SetStatus(string message) => _status.Text = message;
    private static IBrush Solid(string hex) => new SolidColorBrush(Color.Parse(hex));
    private static TextBlock Label(string text, double fontSize, IBrush foreground, bool strong = false) => new()
    {
        Text = text, FontSize = fontSize, Foreground = foreground,
        FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal,
        TextWrapping = TextWrapping.Wrap
    };

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _folderCts?.Cancel();
        CancelWbInput();
        _photoGeneration++;
        _rawQualityCts?.Cancel();
        _qualityTimer.Stop();
        _loadCts?.Cancel();
        _renderer.Dispose();
        _ = ReleaseSourcesAsync();
        _image.Source = null;
        _histogram.SetHistogram(null);
        _beforeBitmap?.Dispose(); _afterBitmap?.Dispose();
        base.OnClosed(e);
    }

    private async Task ReleaseSourcesAsync()
    {
        await _renderer.DrainAsync();
        _previewSource?.Dispose(); _interactiveSource?.Dispose();
        _asShotPreview?.Dispose(); _asShotMetadata = null;
        await _loader.DisposeAsync();
    }
}
