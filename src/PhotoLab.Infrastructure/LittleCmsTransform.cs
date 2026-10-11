using System.Buffers.Binary;
using System.Runtime.InteropServices;
using PhotoLab.Imaging;

namespace PhotoLab.Infrastructure;

/// <summary>Owned RGB ICC to linear sRGB transform. An empty profile assumes sRGB; input is straight RGBA32F.</summary>
public sealed unsafe class LittleCmsTransform : IDisposable
{
    // lcms2.h TYPE_RGBA_FLT, relative colorimetric, COPY_ALPHA and NOOPTIMIZE.
    private const uint RGBA_FLOAT = (1u << 22) | (4u << 16) | (1u << 7) | (3u << 3) | 4u;
    private const uint RGB_SIGNATURE = 0x52474220;
    private const int BLOCK_PIXELS = 16_384;
    private const int MAX_PROFILE_BYTES = 16 * 1024 * 1024;
    private readonly object _gate = new();
    private nint _library, _context, _input, _output, _transform;
    private HandleAction? _deleteContext, _deleteTransform;
    private CloseProfile? _closeProfile;
    private readonly DoTransform _process = null!;

    public LittleCmsTransform(string libraryPath, ReadOnlySpan<byte> inputProfile)
    {
        // Reject malformed envelopes before handing untrusted bytes to the native parser.
        if (!inputProfile.IsEmpty && (inputProfile.Length < 128 || inputProfile.Length > MAX_PROFILE_BYTES ||
            BinaryPrimitives.ReadUInt32BigEndian(inputProfile) != inputProfile.Length ||
            !inputProfile.Slice(36, 4).SequenceEqual("acsp"u8)))
            throw new InvalidDataException("Invalid ICC profile header or size.");
        try
        {
            _library = NativeLibrary.Load(libraryPath);
            _deleteContext = Get<HandleAction>("cmsDeleteContext");
            _deleteTransform = Get<HandleAction>("cmsDeleteTransform");
            _closeProfile = Get<CloseProfile>("cmsCloseProfile");
            _process = Get<DoTransform>("cmsDoTransform");
            _context = Get<CreateContext>("cmsCreateContext")(0, 0);
            if (_context == 0) throw new InvalidOperationException("Cannot create LittleCMS context.");
            if (inputProfile.IsEmpty) _input = Get<CreateSrgb>("cmsCreate_sRGBProfileTHR")(_context);
            else
                fixed (byte* bytes = inputProfile)
                    _input = Get<OpenProfile>("cmsOpenProfileFromMemTHR")(_context, (nint)bytes, (uint)inputProfile.Length);
            if (_input == 0) throw new InvalidDataException("LittleCMS rejected the ICC profile.");
            if (Get<ColorSpace>("cmsGetColorSpace")(_input) != RGB_SIGNATURE)
                throw new InvalidDataException("An RGB ICC profile is required for RGBA pixels.");

            // D65/sRGB primaries with gamma 1. LCMS copies the curves into the profile tags.
            var freeCurve = Get<HandleAction>("cmsFreeToneCurve");
            nint curve = Get<BuildGamma>("cmsBuildGamma")(_context, 1.0);
            if (curve == 0) throw new InvalidOperationException("Cannot create linear transfer curve.");
            try
            {
                double* white = stackalloc double[] { 0.3127, 0.3290, 1.0 };
                double* primaries = stackalloc double[] { 0.64, 0.33, 1, 0.30, 0.60, 1, 0.15, 0.06, 1 };
                nint* curves = stackalloc nint[] { curve, curve, curve };
                _output = Get<CreateRgb>("cmsCreateRGBProfileTHR")(_context, white, primaries, curves);
            }
            finally { freeCurve(curve); }
            if (_output == 0) throw new InvalidOperationException("Cannot create linear sRGB profile.");
            _transform = Get<CreateTransform>("cmsCreateTransformTHR")(_context, _input, RGBA_FLOAT,
                _output, RGBA_FLOAT, 1, 0x04000000 | 0x0100);
            if (_transform == 0) throw new InvalidDataException("Cannot transform ICC profile into linear sRGB.");
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Transform without clipping RGB; return independent owned working pixels.</summary>
    public FloatImageBuffer Process(int width, int height, ReadOnlySpan<float> encodedRgba,
        CancellationToken cancellation = default)
    {
        if (width <= 0 || height <= 0 || checked(width * height * 4) != encodedRgba.Length)
            throw new ArgumentException("RGBA dimensions do not match the input.");
        for (int i = 0; i < encodedRgba.Length; i++)
            if (!float.IsFinite(encodedRgba[i]) || (i % 4 == 3 && (encodedRgba[i] < 0 || encodedRgba[i] > 1)))
                throw new ArgumentException("RGBA must be finite, with alpha between zero and one.");
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_library == 0, this);
            cancellation.ThrowIfCancellationRequested();
            var result = GC.AllocateUninitializedArray<float>(encodedRgba.Length);
            fixed (float* source = encodedRgba)
            fixed (float* destination = result)
                for (int offset = 0; offset < encodedRgba.Length; offset += BLOCK_PIXELS * 4)
                {
                    cancellation.ThrowIfCancellationRequested();
                    uint count = (uint)Math.Min(BLOCK_PIXELS, (encodedRgba.Length - offset) / 4);
                    _process(_transform, (nint)(source + offset), (nint)(destination + offset), count);
                }
            cancellation.ThrowIfCancellationRequested();
            return FloatImageBuffer.FromLinearRgba32(width, height, result);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            // Readers finish before releasing transform, profiles, context, then the native module.
            if (_transform != 0) { _deleteTransform!(_transform); _transform = 0; }
            if (_output != 0) { _closeProfile!(_output); _output = 0; }
            if (_input != 0) { _closeProfile!(_input); _input = 0; }
            if (_context != 0) { _deleteContext!(_context); _context = 0; }
            if (_library != 0) { NativeLibrary.Free(_library); _library = 0; }
        }
    }

    private T Get<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    // Winapi matches the LCMS DLL ABI on Windows and the C ABI on Unix.
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint CreateContext(nint plugin, nint userData);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint CreateSrgb(nint context);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void HandleAction(nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CloseProfile(nint profile);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint OpenProfile(nint context, nint bytes, uint size);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint ColorSpace(nint profile);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint BuildGamma(nint context, double gamma);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint CreateRgb(nint context, double* white, double* primaries, nint* curves);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint CreateTransform(nint context, nint input, uint inputFormat, nint output, uint outputFormat, uint intent, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void DoTransform(nint transform, nint input, nint output, uint count);
}
