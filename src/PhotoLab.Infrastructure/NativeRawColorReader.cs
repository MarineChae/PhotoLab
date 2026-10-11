using System.Runtime.InteropServices;
using PhotoLab.Imaging.Decoding;

namespace PhotoLab.Infrastructure;

/// <summary>
/// Reads stable C getters without mapping LibRaw's version-dependent native structs.
/// Cancellation is checked around open_file; an in-progress native metadata read cannot be interrupted.
/// </summary>
public static class NativeRawColorReader
{
    private static readonly object _gate = new();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr Init(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int OpenUtf8(IntPtr context, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int OpenWide(IntPtr context, [MarshalAs(UnmanagedType.LPWStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Close(IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float Multiplier(IntPtr context, int channel);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float MatrixValue(IntPtr context, int row, int column);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr Version();

    public static RawColorInfo Read(string libraryPath, string sourcePath, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        sourcePath = Path.GetFullPath(sourcePath);
        // Serialize metadata calls even if the installed DLL is not the reentrant LibRaw variant.
        lock (_gate)
        {
            cancellation.ThrowIfCancellationRequested();
            IntPtr library = NativeLibrary.Load(libraryPath);
            try
            {
                var init = Get<Init>(library, "libraw_init");
                var close = Get<Close>(library, "libraw_close");
                var multiplier = Get<Multiplier>(library, "libraw_get_cam_mul");
                var matrixValue = Get<MatrixValue>(library, "libraw_get_rgb_cam");
                var version = Get<Version>(library, "libraw_version");
                var openWide = OperatingSystem.IsWindows() ? Get<OpenWide>(library, "libraw_open_wfile") : null;
                var openUtf8 = OperatingSystem.IsWindows() ? null : Get<OpenUtf8>(library, "libraw_open_file");
                IntPtr context = init(0);
                if (context == IntPtr.Zero) throw new InvalidOperationException("LibRaw metadata context allocation failed.");
                try
                {
                    int status = openWide != null ? openWide(context, sourcePath) : openUtf8!(context, sourcePath);
                    cancellation.ThrowIfCancellationRequested();
                    if (status != 0) throw new InvalidDataException("LibRaw cannot read camera color information: " + status);
                    var asShot = new CameraWhiteBalance(multiplier(context, 0), multiplier(context, 1),
                        multiplier(context, 2), multiplier(context, 3));
                    var matrix = new float[12];
                    for (int row = 0; row < 3; row++)
                        for (int column = 0; column < 4; column++)
                            matrix[row * 4 + column] = matrixValue(context, row, column);
                    string nativeVersion = Marshal.PtrToStringUTF8(version()) ?? "Unknown";
                    return new RawColorInfo(asShot, matrix, nativeVersion);
                }
                finally { close(context); }
            }
            finally { NativeLibrary.Free(library); }
        }
    }

    public static string? FindLibrary(string executable)
    {
        if (!File.Exists(executable)) return null;
        var file = new FileInfo(executable);
        string binary = file.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? file.FullName;
        string directory = Path.GetDirectoryName(binary)!;
        string[] names = OperatingSystem.IsWindows() ? ["libraw_r.dll", "libraw.dll", "raw.dll"]
            : OperatingSystem.IsMacOS() ? ["libraw_r.dylib", "libraw.dylib"] : ["libraw_r.so", "libraw.so"];
        foreach (string folder in new[] { directory, Path.GetFullPath(Path.Combine(directory, "..", "lib")) })
            foreach (string name in names)
            {
                string path = Path.Combine(folder, name);
                if (File.Exists(path)) return path;
            }
        return null;
    }

    private static T Get<T>(IntPtr library, string symbol) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, symbol));
}
