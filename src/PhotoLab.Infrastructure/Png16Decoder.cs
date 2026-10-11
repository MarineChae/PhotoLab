using System.Runtime.InteropServices;

namespace PhotoLab.Infrastructure;

/// <summary>Decode encoded PNG16 samples through libpng; profiles and transfer functions remain unapplied.</summary>
public static unsafe class Png16Decoder
{
    public static bool Is16Bit(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[25];
        stream.ReadExactly(header);
        return header[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) &&
            header.Slice(12,4).SequenceEqual("IHDR"u8) && header[24] == 16;
    }

    public static (int Width, int Height, float[] Rgba) Decode(string path, string libraryPath,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        byte[] bytes = File.ReadAllBytes(path);
        nint library = NativeLibrary.Load(libraryPath);
        nint pixels = 0;
        Free? free = null;
        try
        {
            T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
            if (Get<ApiVersion>("ph_png_api_version")() != 1) throw new NotSupportedException("Unsupported PNG bridge ABI.");
            free = Get<Free>("ph_png_free");
            var decode = Get<DecodePixels>("ph_png_decode");
            CancelCheck cancel = () => cancellation.IsCancellationRequested ? 1 : 0;
            int width, height, status;
            fixed (byte* source = bytes)
                status = decode((nint)source,(nuint)bytes.Length,cancel,out pixels,out width,out height);
            GC.KeepAlive(cancel);
            if (status == 3) throw new OperationCanceledException(cancellation);
            if (status == 2) throw new OutOfMemoryException("Cannot allocate PNG16 pixels.");
            if (status != 0 || pixels == 0 || width <= 0 || height <= 0)
                throw new InvalidDataException("Cannot decode PNG16 pixels: " + status);
            cancellation.ThrowIfCancellationRequested();
            int count = checked(width * height * 4);
            var sourcePixels = new ReadOnlySpan<ushort>((void*)pixels,count);
            var encoded = GC.AllocateUninitializedArray<float>(count);
            for (int i = 0; i < count; i++)
            {
                if (i % 65_536 == 0) cancellation.ThrowIfCancellationRequested();
                encoded[i] = sourcePixels[i] / 65535f;
            }
            cancellation.ThrowIfCancellationRequested();
            return (width,height,encoded);
        }
        finally
        {
            if (pixels != 0) free!(pixels);
            NativeLibrary.Free(library);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ApiVersion();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Free(nint pixels);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CancelCheck();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DecodePixels(nint bytes,nuint size,CancelCheck cancel,out nint pixels,out int width,out int height);
}
