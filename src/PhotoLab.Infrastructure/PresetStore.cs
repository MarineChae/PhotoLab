using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhotoLab.Core;

namespace PhotoLab.Infrastructure;

/// <summary>Portable basic adjustments; camera-specific WB coefficients are never copied between photos.</summary>
public sealed record AdjustmentPreset(string Name, EditSettings Settings)
{
    public override string ToString() => Name;
}

/// <summary>Named, versioned preset files in a dedicated folder; atomic writes and explicit duplicate rejection.</summary>
public sealed class PresetStore
{
    private const int VERSION = 1;
    private const int MAX_FILE_BYTES = 65_536;
    private static readonly SemaphoreSlim _writeGate = new(1,1);
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _directory;

    public PresetStore(string directory) => _directory = Path.GetFullPath(directory);

    public static PresetStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PhotoLab","Presets"));

    public async Task SaveAsync(string name, EditSettings settings, CancellationToken cancellation = default)
    {
        name = ValidateName(name);EditFileStore.Validate(settings);
        await _writeGate.WaitAsync(cancellation);
        try
        {
            Directory.CreateDirectory(_directory);
            string path = GetPath(name);
            if (File.Exists(path)) throw new IOException("A preset with this name already exists. Choose a new name.");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new Document(VERSION,name,settings),_options);
            await AtomicFileStore.SaveAsync(path,bytes,cancellation);
        }
        finally { _writeGate.Release(); }
    }

    public async Task<IReadOnlyList<AdjustmentPreset>> ListAsync(CancellationToken cancellation = default)
    {
        var presets = new List<AdjustmentPreset>();
        if (!Directory.Exists(_directory)) return presets;
        foreach (string path in Directory.EnumerateFiles(_directory,"*.photolab-preset.json"))
        {
            cancellation.ThrowIfCancellationRequested();
            await using var file = File.OpenRead(path);
            if (file.Length > MAX_FILE_BYTES) throw new InvalidDataException("Preset file is too large.");
            try
            {
                using var json = await JsonDocument.ParseAsync(file,cancellationToken:cancellation);
                var root = json.RootElement;
                ValidateFields(root,new[] {"Version","Name","Settings"});
                var document = root.Deserialize<Document>(_options) ?? throw new InvalidDataException("Missing preset.");
                if (document.Version != VERSION) throw new InvalidDataException("Unsupported preset version.");
                string name = ValidateName(document.Name);
                if (document.Settings == null) throw new InvalidDataException("Missing preset adjustments.");
                var settings = root.EnumerateObject().Single(p => p.Name.Equals("Settings",StringComparison.OrdinalIgnoreCase)).Value;
                ValidateFields(settings,new[] {"Exposure","Contrast","Highlights","Shadows","Temperature","Tint","Saturation","Vibrance"});
                EditFileStore.Validate(document.Settings);
                if (!Path.GetFileName(path).Equals(Path.GetFileName(GetPath(name)),StringComparison.Ordinal))
                    throw new InvalidDataException("Preset filename does not match its name.");
                presets.Add(new AdjustmentPreset(name,document.Settings));
            }
            catch (JsonException error) { throw new InvalidDataException("Invalid preset JSON.",error); }
        }
        return presets.OrderBy(p=>p.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task DeleteAsync(string name, CancellationToken cancellation = default)
    {
        await _writeGate.WaitAsync(cancellation);
        try { cancellation.ThrowIfCancellationRequested();File.Delete(GetPath(ValidateName(name))); }
        finally { _writeGate.Release(); }
    }

    private string GetPath(string name)
    {
        // A display name never becomes a filesystem path, including separators or Unicode names.
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name.ToUpperInvariant())));
        return Path.Combine(_directory,id+".photolab-preset.json");
    }

    private static string ValidateName(string? name)
    {
        string normalized = name?.Trim() ?? "";
        if (normalized.Length == 0 || normalized.Length > 80 || normalized.Any(char.IsControl))
            throw new InvalidDataException("Preset name must contain 1–80 characters without control characters.");
        return normalized;
    }

    private static void ValidateFields(JsonElement value, string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Preset data must be an object.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
            if (!expected.Contains(property.Name,StringComparer.OrdinalIgnoreCase) || !names.Add(property.Name))
                throw new InvalidDataException("Unknown or duplicate preset field: "+property.Name);
        if (names.Count != expected.Length) throw new InvalidDataException("Preset data is incomplete.");
    }

    private sealed record Document(int Version, string Name, EditSettings Settings);
}
