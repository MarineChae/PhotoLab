using System.Text.Json;
using PhotoLab.Core;
using PhotoLab.Imaging;
using PhotoLab.Imaging.Decoding;

namespace PhotoLab.Infrastructure;

/// <summary>Versioned atomic edit files. Legacy flat JSON remains readable without modifying Core.</summary>
public sealed class EditFileStore
{
    private const int LEGACY_VERSION = 1;
    private const int CAMERA_WB_VERSION = 2;
    private static readonly SemaphoreSlim _writeGate = new(1, 1);
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string GetPath(string imagePath) => SidecarStore.GetPath(imagePath);

    public async Task SaveAsync(string imagePath, EditSettings settings, CancellationToken cancellation = default)
    {
        await _writeGate.WaitAsync(cancellation);
        try
        {
            // Check and replace under the same gate: concurrent modern saves must not lose their WB.
            if (File.Exists(GetPath(imagePath))) _ = await LoadAsync(imagePath, cancellation);
            await SaveCoreAsync(imagePath, new PhotoEditState(settings), cancellation);
        }
        finally { _writeGate.Release(); }
    }

    public async Task SaveStateAsync(string imagePath, PhotoEditState state, CancellationToken cancellation = default)
    {
        await _writeGate.WaitAsync(cancellation);
        try { await SaveCoreAsync(imagePath, state, cancellation); }
        finally { _writeGate.Release(); }
    }

    private static async Task SaveCoreAsync(string imagePath, PhotoEditState state, CancellationToken cancellation)
    {
        Validate(state.Adjustments);
        ValidateWhiteBalance(imagePath, state.WhiteBalance);
        string path = GetPath(imagePath);
        PhotoFileGuard.EnsureDistinct(imagePath, path);
        // WB-free version 1 stays compatible with Core; version 2 requires the complete state reader.
        var document = JsonSerializer.SerializeToNode(state.Adjustments, _jsonOptions)!;
        document["version"] = state.WhiteBalance == null ? LEGACY_VERSION : CAMERA_WB_VERSION;
        if (state.WhiteBalance != null)
            document["CameraWhiteBalance"] = JsonSerializer.SerializeToNode(state.WhiteBalance, _jsonOptions);
        await AtomicFileStore.SaveAsync(path, JsonSerializer.SerializeToUtf8Bytes(document, _jsonOptions), cancellation);
    }

    public async Task<EditSettings> LoadAsync(string imagePath, CancellationToken cancellation = default)
    {
        var state = await LoadStateAsync(imagePath, cancellation);
        if (state.WhiteBalance != null) throw new InvalidDataException("Camera WB edit files require the full photo-state API.");
        return state.Adjustments;
    }

    public async Task<PhotoEditState> LoadStateAsync(string imagePath, CancellationToken cancellation = default)
    {
        string path = GetPath(imagePath);
        if (!File.Exists(path)) return new PhotoEditState(new EditSettings());
        await using var file = File.OpenRead(path);
        try
        {
            using var document = await JsonDocument.ParseAsync(file, cancellationToken: cancellation);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Edit settings must be a JSON object.");
            var version = FindProperty(root, "version");
            int number = LEGACY_VERSION;
            if (version.HasValue && (version.Value.ValueKind != JsonValueKind.Number ||
                !version.Value.TryGetInt32(out number) || (number != LEGACY_VERSION && number != CAMERA_WB_VERSION)))
                throw new InvalidDataException("Unsupported edit file version.");
            var settings = root.Deserialize<EditSettings>(_jsonOptions) ?? throw new InvalidDataException("Missing edit settings.");
            Validate(settings);
            var wb = FindProperty(root, "CameraWhiteBalance");
            CameraWhiteBalance? whiteBalance = null;
            if (number == CAMERA_WB_VERSION)
            {
                if (!wb.HasValue || wb.Value.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Version 2 requires a camera white balance object.");
                foreach (string channel in new[] { "Channel0", "Channel1", "Channel2", "Channel3" })
                    if (!FindProperty(wb.Value, channel).HasValue)
                        throw new InvalidDataException("Camera WB must contain all four channels.");
                whiteBalance = wb.Value.Deserialize<CameraWhiteBalance>(_jsonOptions)!;
                ValidateWhiteBalance(imagePath, whiteBalance);
            }
            else if (wb.HasValue) throw new InvalidDataException("Camera WB requires edit file version 2.");
            return new PhotoEditState(settings, whiteBalance);
        }
        catch (JsonException error) { throw new InvalidDataException("Invalid edit settings JSON.", error); }
    }

    // Metadata keys are case-insensitive, but conflicting aliases must never pick a different WB/version.
    private static JsonElement? FindProperty(JsonElement root, string name)
    {
        JsonElement? found = null;
        foreach (var property in root.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                if (found.HasValue) throw new InvalidDataException("Duplicate edit field: " + name);
                found = property.Value;
            }
        return found;
    }

    private static void ValidateWhiteBalance(string imagePath, CameraWhiteBalance? whiteBalance)
    {
        if (whiteBalance == null) return;
        if (!PhotoFormats.IsRaw(imagePath)) throw new InvalidDataException("Camera WB settings require a RAW image.");
        try { _ = whiteBalance.Normalize(); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid camera WB settings.", error); }
    }

    internal static void Validate(EditSettings settings)
    {
        if (!InRange(settings.Exposure, -4, 4) || !InRange(settings.Contrast, -100, 100) ||
            !InRange(settings.Highlights, -100, 100) || !InRange(settings.Shadows, -100, 100) ||
            !InRange(settings.Temperature, -100, 100) || !InRange(settings.Tint, -100, 100) ||
            !InRange(settings.Saturation, -100, 100) || !InRange(settings.Vibrance, -100, 100))
            throw new InvalidDataException("Edit values must be finite and within the supported slider ranges.");
    }

    private static bool InRange(double value, double minimum, double maximum)
        => double.IsFinite(value) && value >= minimum && value <= maximum;
}
