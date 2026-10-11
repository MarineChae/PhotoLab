using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using PhotoLab.Imaging;

namespace PhotoLab.Infrastructure;

/// <summary>Encode linear working pixels into a 16-bit sRGB PNG, preserving straight alpha.</summary>
public static unsafe class Png16Encoder
{
    /// <summary>Write on the caller's worker; source must remain alive and stream is left open.</summary>
    public static void Write(FloatImageBuffer source, string libraryPath, Stream destination, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!destination.CanWrite) throw new ArgumentException("PNG output stream must be writable.", nameof(destination));
        _ = source.Pixels.Length;
        nint library = NativeLibrary.Load(libraryPath);
        try
        {
            T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
            if (Get<ApiVersion>("ph_png_api_version")() != 1) throw new NotSupportedException("Unsupported PNG bridge ABI.");
            var write = Get<WritePixels>("ph_png_write");
            Exception? failure = null;
            ReadRow read = (row, pixels) =>
            {
                try
                {
                    ImageConvert.WriteSrgb16(source.Pixels.Slice(checked(row * source.Stride),source.Stride),
                        new Span<ushort>((void*)pixels,source.Stride),cancellation);
                    return 1;
                }
                catch (Exception error) { failure = error; return 0; }
            };
            WriteChunk chunk = (bytes, size) =>
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    destination.Write(new ReadOnlySpan<byte>((void*)bytes,checked((int)size)));
                    return 1;
                }
                catch (Exception error) { failure = error; return 0; }
            };
            CancelCheck cancel = () => cancellation.IsCancellationRequested ? 1 : 0;
            int status = write(source.Width,source.Height,read,chunk,cancel);
            GC.KeepAlive(read);GC.KeepAlive(chunk);GC.KeepAlive(cancel);
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
            if (status == 3) throw new OperationCanceledException(cancellation);
            if (status == 2) throw new OutOfMemoryException("Cannot allocate PNG16 row.");
            if (status != 0) throw new InvalidDataException("Cannot write PNG16 stream: " + status);
            cancellation.ThrowIfCancellationRequested();
        }
        finally { NativeLibrary.Free(library); }
    }

    public static byte[] Encode(FloatImageBuffer source, string libraryPath, CancellationToken cancellation = default)
    {
        ushort[] rgba = ImageConvert.ToSrgb16(source,cancellation);
        nint library = NativeLibrary.Load(libraryPath), bytes = 0;
        Free? free = null;
        try
        {
            T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
            if (Get<ApiVersion>("ph_png_api_version")() != 1) throw new NotSupportedException("Unsupported PNG bridge ABI.");
            free = Get<Free>("ph_png_free");
            var encode = Get<EncodePixels>("ph_png_encode");
            CancelCheck cancel = () => cancellation.IsCancellationRequested ? 1 : 0;
            int status;uint size;
            fixed (ushort* pixels = rgba)
                status = encode((nint)pixels,source.Width,source.Height,cancel,out bytes,out size);
            GC.KeepAlive(cancel);
            if (status == 3) throw new OperationCanceledException(cancellation);
            if (status == 2) throw new OutOfMemoryException("Cannot allocate PNG16 output.");
            if (status != 0 || bytes == 0 || size == 0 || size > int.MaxValue)
                throw new InvalidDataException("Cannot encode PNG16 pixels: " + status);
            cancellation.ThrowIfCancellationRequested();
            var result = GC.AllocateUninitializedArray<byte>((int)size);
            Marshal.Copy(bytes,result,0,result.Length);
            cancellation.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            if (bytes != 0) free!(bytes);
            NativeLibrary.Free(library);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ApiVersion();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadRow(int row,nint pixels);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int WriteChunk(nint bytes,nuint size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int WritePixels(int width,int height,ReadRow read,WriteChunk write,CancelCheck cancel);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Free(nint bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CancelCheck();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EncodePixels(nint pixels,int width,int height,CancelCheck cancel,out nint bytes,out uint size);
}
