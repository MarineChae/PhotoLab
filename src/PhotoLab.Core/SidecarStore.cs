using System.Text.Json;

namespace PhotoLab.Core;

public sealed class SidecarStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static string GetPath(string photoPath) => photoPath + ".photolab.json";

    public async Task SaveAsync(string imagePath, EditSettings edit, CancellationToken ct = default)
    {
        string path = GetPath(imagePath);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, edit, JsonOptions, ct);
    }

    public async Task<EditSettings> LoadAsync(string imagePath, CancellationToken ct = default)
    {
        string path = GetPath(imagePath);
        if (!File.Exists(path)) return new EditSettings();
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<EditSettings>(stream, cancellationToken: ct) ?? new EditSettings();
    }
}
