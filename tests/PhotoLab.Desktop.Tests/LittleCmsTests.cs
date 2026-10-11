using PhotoLab.Desktop;
using PhotoLab.Infrastructure;
using SkiaSharp;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;

/// <summary>Actual native ICC conversion, independent matrix/transfer expectations and lifetime failures.</summary>
static class LittleCmsTests
{
    public static async Task RunAsync(string library)
    {
        using var p3 = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);
        byte[] profile = Profile(p3);
        CheckExtraction(profile,p3,library);
        using var transform = new LittleCmsTransform(library, []);
        float[] source = [0,0,0,0, 0.5f,0.5f,0.5f,0.25f, 1,1,1,1];
        using var output = transform.Process(3,1,source);
        Check(Math.Abs(output.Pixels[4]-0.21404114f)<0.0001f && Math.Abs(output.Pixels[8]-1)<0.0001f,
            "LCMS sRGB transfer agrees with analytic reference");
        Check(output.Pixels[3]==0 && output.Pixels[7]==0.25f && output.Pixels[11]==1,
            "LCMS preserves straight alpha including transparent pixels");
        using var wide = new LittleCmsTransform(library, Profile(p3));
        using var red = wide.Process(1,1,new float[] {1,0,0,0.75f});
        Check(Math.Abs(red.Pixels[0]-1.224745f)<0.002f && Math.Abs(red.Pixels[1]+0.042058f)<0.002f && Math.Abs(red.Pixels[2]+0.019642f)<0.002f,
            $"LCMS P3 red retains extended linear RGB ({red.Pixels[0]:G6}, {red.Pixels[1]:G6}, {red.Pixels[2]:G6})");
        using var repeated = wide.Process(1,1,new float[] {1,0,0,0.75f});
        Check(repeated.Pixels.SequenceEqual(red.Pixels), "reused transform is deterministic");
        using var fromOwnedProfile = new LittleCmsTransform(library,profile);
        byte[] original = (byte[])profile.Clone();Array.Clear(profile);
        using var retained = fromOwnedProfile.Process(1,1,new float[] {1,0,0,0.75f});
        Check(retained.Pixels.SequenceEqual(red.Pixels), "native profile owns bytes after caller changes its array");
        profile=original;
        var large = new float[16_385*4];
        for (int i=0;i<large.Length;i+=4) { large[i]=large[i+1]=large[i+2]=0.5f;large[i+3]=0.625f; }
        using var blocked = transform.Process(16_385,1,large);
        Check(blocked.Pixels[..4].SequenceEqual(blocked.Pixels[^4..]), "LCMS block boundary processes the final pixel");
        Reject<OperationCanceledException>(() => transform.Process(3,1,source,new CancellationToken(true)), "pre-canceled transform is rejected");
        Reject<ArgumentException>(() => transform.Process(1,1,new float[] {float.NaN,0,0,1}), "nonfinite RGB rejected");
        Reject<ArgumentException>(() => transform.Process(1,1,new float[] {0,0,0,2}), "invalid alpha rejected");
        Reject<ArgumentException>(() => transform.Process(2,1,source), "invalid dimensions rejected");
        byte[] malformed = (byte[])profile.Clone();malformed[36]=0;
        Reject<InvalidDataException>(() => new LittleCmsTransform(library,malformed), "invalid ICC signature rejected");
        malformed=(byte[])profile.Clone();BinaryPrimitives.WriteUInt32BigEndian(malformed,127);
        Reject<InvalidDataException>(() => new LittleCmsTransform(library,malformed), "invalid ICC declared length rejected");
        malformed=(byte[])profile.Clone();"GRAY"u8.CopyTo(malformed.AsSpan(16));
        Reject<InvalidDataException>(() => new LittleCmsTransform(library,malformed), "non-RGB profile rejected");
        byte[] broken = new byte[128];BinaryPrimitives.WriteUInt32BigEndian(broken,128);"acsp"u8.CopyTo(broken.AsSpan(36));
        Reject<InvalidDataException>(() => new LittleCmsTransform(library,broken), "native parser rejects incomplete ICC tags");
        transform.Dispose();transform.Dispose();
        Reject<ObjectDisposedException>(() => transform.Process(3,1,source), "disposed native transform rejects reuse");
        Check(source[4]==0.5f && source[7]==0.25f && output.Pixels[7]==0.25f,
            "input and owned output survive native disposal");
        string? previous = Environment.GetEnvironmentVariable("PHOTOLAB_LCMS_LIBRARY");
        try
        {
            Environment.SetEnvironmentVariable("PHOTOLAB_LCMS_LIBRARY",library);
            await ColorProfileTests.RunAsync();
        }
        finally { Environment.SetEnvironmentVariable("PHOTOLAB_LCMS_LIBRARY",previous); }
    }

    private static void CheckExtraction(byte[] profile,SKColorSpace space,string library)
    {
        string directory=Path.Combine(Path.GetTempPath(),"photolab-icc-read-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path=Path.Combine(directory,"segments.jpg");
            void WriteJpeg(params (int index,int count,byte[] data)[] parts)
            {
                using var stream=File.Create(path);stream.Write(new byte[] {255,216});
                Span<byte> length=stackalloc byte[2];
                foreach (var part in parts)
                {
                    stream.Write(new byte[] {255,226});
                    BinaryPrimitives.WriteUInt16BigEndian(length,checked((ushort)(part.data.Length+16)));
                    stream.Write(length);stream.Write("ICC_PROFILE\0"u8);
                    stream.WriteByte((byte)part.index);stream.WriteByte((byte)part.count);stream.Write(part.data);
                }
                stream.Write(new byte[] {255,217});
            }
            int split=profile.Length/2;
            WriteJpeg((2,2,profile[split..]),(1,2,profile[..split]));
            Check(EmbeddedIccProfile.Read(path).SequenceEqual(profile), "JPEG ICC segments reassemble in sequence order");
            WriteJpeg((1,2,profile[..split]));
            Reject<InvalidDataException>(()=>EmbeddedIccProfile.Read(path),"missing JPEG ICC segment rejected");
            WriteJpeg((1,2,profile[..split]),(1,2,profile[..split]));
            Reject<InvalidDataException>(()=>EmbeddedIccProfile.Read(path),"duplicate JPEG ICC segment rejected");
            WriteJpeg((1,2,profile[..split]),(2,3,profile[split..]));
            Reject<InvalidDataException>(()=>EmbeddedIccProfile.Read(path),"inconsistent JPEG ICC segment counts rejected");

            using var bitmap=new SKBitmap(new SKImageInfo(1,1,SKColorType.Rgba8888,SKAlphaType.Unpremul,space));
            bitmap.Erase(SKColors.Gray);using var image=SKImage.FromBitmap(bitmap);
            using var encoded=image.Encode(SKEncodedImageFormat.Jpeg,100);
            File.WriteAllBytes(path,encoded.ToArray());
            Check(EmbeddedIccProfile.Read(path).Length>128,"actual JPEG supplies original ICC bytes");
            string? previous=Environment.GetEnvironmentVariable("PHOTOLAB_LCMS_LIBRARY");
            try
            {
                // Use Skia directly: clearing an override now permits automatic bundled LCMS selection.
                using var decodedJpeg=SKBitmap.Decode(path);
                using var jpegImage=SKImage.FromBitmap(decodedJpeg);
                using var linear=SKColorSpace.CreateSrgbLinear();
                var info=new SKImageInfo(1,1,SKColorType.RgbaF32,SKAlphaType.Unpremul,linear);
                var reference=new float[4];var pinned=GCHandle.Alloc(reference,GCHandleType.Pinned);
                try
                {
                    if(!jpegImage.ReadPixels(info,pinned.AddrOfPinnedObject(),info.RowBytes,0,0))
                        throw new Exception("Cannot read independent Skia JPEG reference.");
                }
                finally {pinned.Free();}
                Environment.SetEnvironmentVariable("PHOTOLAB_LCMS_LIBRARY",library);
                using var native=SkiaImageCodec.LoadLinearFull(path);
                Check(Math.Abs(reference[0]-native.Pixels[0])<0.001f && native.Pixels[3]==1,
                    "actual tagged JPEG native load agrees with independent Skia conversion");
            }
            finally { Environment.SetEnvironmentVariable("PHOTOLAB_LCMS_LIBRARY",previous); }

            string png=Path.Combine(directory,"oversize.png");
            using var payload=new MemoryStream();payload.Write("profile\0\0"u8);
            using (var zlib=new ZLibStream(payload,CompressionLevel.Fastest,leaveOpen:true))
            {
                var block=new byte[1024*1024];for(int i=0;i<17;i++) zlib.Write(block);
            }
            using(var stream=File.Create(png))
            {
                stream.Write(new byte[] {137,80,78,71,13,10,26,10});
                Span<byte> length=stackalloc byte[4];BinaryPrimitives.WriteUInt32BigEndian(length,(uint)payload.Length);
                stream.Write(length);stream.Write("iCCP"u8);stream.Write(payload.ToArray());stream.Write(new byte[4]);
            }
            Reject<InvalidDataException>(()=>EmbeddedIccProfile.Read(png),"compressed ICC expansion beyond 16 MiB rejected");
        }
        finally { Directory.Delete(directory,recursive:true); }
    }

    private static byte[] Profile(SKColorSpace space)
    {
        // ToProfile exposes a parsed skcms structure and cannot serialize a constructed RGB space.
        // Encoding a real PNG yields the original serialized iCCP payload for the native engine.
        using var bitmap=new SKBitmap(new SKImageInfo(1,1,SKColorType.Rgba8888,SKAlphaType.Unpremul,space));
        bitmap.Erase(SKColors.Black);using var image=SKImage.FromBitmap(bitmap);
        using var encoded=image.Encode(SKEncodedImageFormat.Png,100);
        string path=Path.Combine(Path.GetTempPath(),"photolab-icc-"+Guid.NewGuid().ToString("N")+".png");
        try { File.WriteAllBytes(path,encoded.ToArray());return EmbeddedIccProfile.Read(path); }
        finally { File.Delete(path); }
    }
    private static void Reject<T>(Action action,string message) where T:Exception
    {
        try { action(); } catch(T) { Check(true,message);return; }
        throw new Exception("FAIL: "+message);
    }
    private static void Check(bool condition,string message)
    {
        if (!condition) throw new Exception("FAIL: "+message);
        Console.WriteLine("PASS: "+message);
    }
}
