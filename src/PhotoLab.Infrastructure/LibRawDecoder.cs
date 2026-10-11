using System.ComponentModel;
using System.Globalization;
using PhotoLab.Core;
using PhotoLab.Imaging;
using PhotoLab.Imaging.Decoding;

namespace PhotoLab.Infrastructure;

/// <summary>
/// Prototype adapter to LibRaw's dcraw_emu command-line sample. Works on Windows and
/// macOS as long as dcraw_emu is installed. Optional camera color metadata uses C API getters.
/// Pixel decoding still uses a temporary PPM file.
/// </summary>
public sealed class LibRawDecoder : IRawDeveloper
{
    private static readonly TimeSpan _decodeTimeout = TimeSpan.FromMinutes(4);

    public Task<PixelBuffer> DecodeLegacyAsync(string sourcePath, CancellationToken cancellation = default)
        => DecodeAsync(sourcePath, linear: false, PpmImageCodec.Read, cancellation);

    /// <summary>Linear 16-bit sRGB output with automatic brightening disabled, preserving precision for editing.</summary>
    public Task<FloatImageBuffer> DecodeLinearAsync(string sourcePath, CancellationToken cancellation = default)
        => DecodeAsync(sourcePath, linear: true, path => LinearPpmDecoder.Read(path, cancellation), cancellation);

    public Task<RawImage> DecodeAsync(string sourcePath, CancellationToken cancellation = default)
        => DevelopCoreAsync(sourcePath, null, cancellation);

    public Task<RawImage> DevelopAsync(string sourcePath, CameraWhiteBalance whiteBalance,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(whiteBalance);
        _ = whiteBalance.Normalize();
        return DevelopCoreAsync(sourcePath, whiteBalance, cancellation);
    }

    private async Task<RawImage> DevelopCoreAsync(string sourcePath, CameraWhiteBalance? whiteBalance,
        CancellationToken cancellation)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        var pixels = await DecodeAsync(sourcePath, linear: true, path => LinearPpmDecoder.Read(path, cancellation),
            cancellation, whiteBalance);
        try
        {
            string executable = FindExecutable();
            string helper = Path.Combine(Path.GetDirectoryName(executable) ?? "",
                OperatingSystem.IsWindows() ? "raw-identify.exe" : "raw-identify");
            var metadata = new RawMetadata(pixels.Width, pixels.Height, Path.GetExtension(sourcePath))
                { AppliedWhiteBalance = whiteBalance };
            metadata = await RawMetadataReader.ReadAsync(helper, sourcePath, metadata, cancellation);
            string? library = NativeRawColorReader.FindLibrary(executable);
            if (library != null)
            {
                try
                {
                    var color = await Task.Run(() => NativeRawColorReader.Read(library, sourcePath, cancellation), cancellation);
                    metadata = metadata with { CameraColor = color };
                }
                catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or
                    BadImageFormatException or InvalidDataException or ArgumentException)
                {
                    metadata = metadata with { MetadataWarning = string.Join(" ", metadata.MetadataWarning,
                        "Camera color metadata unavailable: " + error.Message).Trim() };
                }
            }
            else metadata = metadata with { MetadataWarning = string.Join(" ", metadata.MetadataWarning,
                "Camera color metadata unavailable: no matching LibRaw shared library found.").Trim() };
            cancellation.ThrowIfCancellationRequested();
            return new RawImage(pixels, metadata);
        }
        catch { pixels.Dispose(); throw; }
    }

    private async Task<T> DecodeAsync<T>(string sourcePath, bool linear, Func<string, T> read,
        CancellationToken cancellation, CameraWhiteBalance? whiteBalance = null)
    {
        // Absolute source paths cannot be mistaken for native command-line flags.
        sourcePath = Path.GetFullPath(sourcePath);
        if (!PhotoFormats.IsRaw(sourcePath))
            throw new ArgumentException("Not a recognized camera RAW extension.", nameof(sourcePath));
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("RAW source not found.", sourcePath);

        // Separate temporary directory per decode to avoid races across preview/export.
        string directory = Path.Combine(Path.GetTempPath(), "photolab-raw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, "developed.ppm");
        try
        {
            string executable = FindExecutable();
            // -w   camera white balance when available
            // -o 1 sRGB output profile
            // -6   16-bit binary PPM intermediate (RGB8 editor conversion happens later)
            // -Z   write a predictable temporary output, never into the photo directory
            LibRawProcessResult result;
            try
            {
                // -4 is -6 -W -g 1 1: linear transfer and no automatic brightening.
                var arguments = new List<string>();
                if (whiteBalance == null) arguments.Add("-w");
                else
                {
                    var normalized = whiteBalance.Normalize();
                    // -w overrides user_mul in LibRaw scale_colors: do not combine it with -r.
                    // +M retains embedded camera color data independently of the WB selection.
                    arguments.AddRange(["-r", normalized.Channel0.ToString("R", CultureInfo.InvariantCulture),
                        normalized.Channel1.ToString("R", CultureInfo.InvariantCulture),
                        normalized.Channel2.ToString("R", CultureInfo.InvariantCulture),
                        normalized.Channel3.ToString("R", CultureInfo.InvariantCulture), "+M"]);
                }
                arguments.AddRange(["-o", "1", linear ? "-4" : "-6", "-Z", destination, sourcePath]);
                result = await LibRawProcess.RunAsync(executable, arguments, _decodeTimeout, cancellation);
            }
            catch (Win32Exception ex)
            {
                throw new InvalidOperationException(
                    "RAW support needs LibRaw dcraw_emu. macOS: brew install libraw. " +
                    "Windows: install a compatible LibRaw build and set PHOTOLAB_DCRAW_EMU to its full executable path.", ex);
            }
            if (result.ExitCode != 0 || !File.Exists(destination))
            {
                string message = string.IsNullOrWhiteSpace(result.Error) ? "Unsupported RAW/camera or decoding failed." : result.Error.Trim();
                if (message.Length > 500) message = message[..500];
                throw new InvalidDataException("LibRaw failed: " + message);
            }
            return await Task.Run(() => read(destination), cancellation);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { /* best-effort temp cleanup; never modify the source */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string FindExecutable()
    {
        string? configured = Environment.GetEnvironmentVariable("PHOTOLAB_DCRAW_EMU");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var appLocal = Path.Combine(AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "dcraw_emu.exe" : "dcraw_emu");
        if (File.Exists(appLocal)) return appLocal;
        if (OperatingSystem.IsMacOS())
        {
            if (File.Exists("/opt/homebrew/bin/dcraw_emu")) return "/opt/homebrew/bin/dcraw_emu";
            if (File.Exists("/usr/local/bin/dcraw_emu")) return "/usr/local/bin/dcraw_emu";
        }
        return OperatingSystem.IsWindows() ? "dcraw_emu.exe" : "dcraw_emu";
    }
}
