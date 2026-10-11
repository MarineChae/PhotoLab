using System.Buffers.Binary;
using System.Security.Cryptography;
using PhotoLab.Core;
using PhotoLab.Desktop;
using PhotoLab.Imaging;
using PhotoLab.Infrastructure;
using SkiaSharp;

/// <summary>Native export, independent decode/code references and atomic original/link protection.</summary>
static class Png16ExportTests
{
    public static async Task RunAsync(string bridge, string[] rawPaths)
    {
        string directory=Path.Combine(Path.GetTempPath(),"photolab-png16-export-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string? previous=Environment.GetEnvironmentVariable("PHOTOLAB_PNG16_BRIDGE");
        try
        {
            Environment.SetEnvironmentVariable("PHOTOLAB_PNG16_BRIDGE",bridge);
            var encoded=new float[65536*4];
            for(int i=0;i<65536;i++)
            {
                encoded[i*4]=i/65535f;encoded[i*4+1]=(65535-i)/65535f;
                encoded[i*4+2]=(i^0x5555)/65535f;encoded[i*4+3]=i/65535f;
            }
            using var working=ImageConvert.FromSrgb32(256,256,encoded);
            ushort[] codes=ImageConvert.ToSrgb16(working);bool exact=true;
            for(int i=0;i<65536;i++)
                exact &= codes[i*4]==i && codes[i*4+1]==65535-i && codes[i*4+2]==(i^0x5555) && codes[i*4+3]==i;
            Check(exact,"all 65,536 sRGB/alpha codes survive float-to-ushort export quantization");
            string source=Path.Combine(directory,"source.png");
            byte[] bytes=Png16Encoder.Encode(working,bridge);await File.WriteAllBytesAsync(source,bytes);
            using(var stream=new MemoryStream())
            {
                Png16Encoder.Write(working,bridge,stream);
                Check(stream.CanWrite && stream.ToArray().SequenceEqual(bytes),"streamed PNG16 is byte-identical to the existing native encoder and leaves its stream open");
            }
            Check(bytes.AsSpan(12,4).SequenceEqual("IHDR"u8) && bytes[24]==16 && bytes[25]==6 &&
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16))==256,"encoded PNG declares 16-bit RGBA and source dimensions");
            using(var codec=SKCodec.Create(source)) Check(codec.Info.ColorSpace?.IsSrgb==true,"PNG16 export declares actual sRGB encoding");
            var decoded=Png16Decoder.Decode(source,bridge);
            Check(decoded.Rgba.SequenceEqual(encoded),"libpng independently decodes every exported 16-bit channel exactly");
            byte[] sourceHash=SHA256.HashData(bytes);
            var processor=new FloatImageProcessor();using var adjusted=processor.Process(working,new EditSettings {Exposure=-1});
            string output=Path.Combine(directory,"edited.png");
            await PhotoExportStore.SavePng16Async(source,output,adjusted);
            var edited=Png16Decoder.Decode(output,bridge);double error=0;
            for(int i=0;i<edited.Rgba.Length;i++)
            {
                double expected=i%4==3?encoded[i]:Encode(adjusted.Pixels[i]);
                error=Math.Max(error,Math.Abs(edited.Rgba[i]-expected));
            }
            Check(error<1.1/65535,$"edited PNG16 agrees with double analytic export within one code (max error {error:G6})");
            Check(edited.Rgba[32768*4]<edited.Rgba[32769*4],"PNG16 edited midtones retain precision beyond 8-bit output");
            Check(SHA256.HashData(await File.ReadAllBytesAsync(source)).SequenceEqual(sourceHash),"PNG16 export preserves original source hash");
            using var extremes=FloatImageBuffer.FromLinearRgba32(2,1,new float[]{-1,2,0,0, 0.0031308f,0.25f,1,32769/65535f});
            string clipped=Path.Combine(directory,"clipped.png");await File.WriteAllBytesAsync(clipped,Png16Encoder.Encode(extremes,bridge));
            var boundary=Png16Decoder.Decode(clipped,bridge);
            Check(boundary.Rgba[0]==0 && boundary.Rgba[1]==1 && boundary.Rgba[3]==0 && boundary.Rgba[7]==32769/65535f,
                "export clips out-of-gamut RGB only at the output boundary and preserves straight alpha");
            Check(extremes.Pixels[0]==-1 && extremes.Pixels[1]==2,"PNG16 export never changes working HDR pixels");
            await RejectAsync<IOException>(()=>PhotoExportStore.SavePng16Async(source,source,adjusted),"PNG16 direct original overwrite rejected");
            string link=Path.Combine(directory,"source-link.png");File.CreateSymbolicLink(link,source);
            await RejectAsync<IOException>(()=>PhotoExportStore.SavePng16Async(source,link,adjusted),"PNG16 symlink original overwrite rejected");
            byte[] previousBytes=await File.ReadAllBytesAsync(output);
            await RejectAsync<OperationCanceledException>(()=>PhotoExportStore.SavePng16Async(source,output,adjusted,new CancellationToken(true)),
                "pre-canceled PNG16 export rejected");
            Check((await File.ReadAllBytesAsync(output)).SequenceEqual(previousBytes),"canceled PNG16 export preserves existing destination");
            Environment.SetEnvironmentVariable("PHOTOLAB_PNG16_BRIDGE",Path.Combine(directory,"missing.dylib"));
            await RejectAsync<DllNotFoundException>(()=>PhotoExportStore.SavePng16Async(source,output,adjusted),"encoder failure propagates without replacing the output");
            Check((await File.ReadAllBytesAsync(output)).SequenceEqual(previousBytes),"failed encoder preserves existing destination");
            Check(!Directory.GetFiles(directory,".photolab-write-*").Any(),"failed/canceled PNG16 export leaves no temporary output");
            Environment.SetEnvironmentVariable("PHOTOLAB_PNG16_BRIDGE",bridge);
            await RejectAsync<IOException>(()=>AtomicFileStore.SaveAsync(output,(file,token)=>Task.Run(()=>
            {
                using var failing=new ObservedStream(file,()=>throw new IOException("Injected destination write failure."));
                Png16Encoder.Write(working,bridge,failing,token);
            },token)),"native stream callback failure returns to managed code safely");
            Check((await File.ReadAllBytesAsync(output)).SequenceEqual(previousBytes) && !Directory.GetFiles(directory,".photolab-write-*").Any(),
                "partial streamed write failure preserves destination and removes temporary file");
            using(var cancellation=new CancellationTokenSource())
            {
                await RejectAsync<OperationCanceledException>(()=>AtomicFileStore.SaveAsync(output,(file,token)=>Task.Run(()=>
                {
                    using var canceling=new ObservedStream(file,()=> {if(file.Position>128) cancellation.Cancel();});
                    Png16Encoder.Write(working,bridge,canceling,token);
                },token),cancellation.Token),"streamed PNG16 cancellation after output starts is propagated");
            }
            Check((await File.ReadAllBytesAsync(output)).SequenceEqual(previousBytes) && !Directory.GetFiles(directory,".photolab-write-*").Any(),
                "mid-stream cancellation preserves destination and cleans temporary data");
            foreach(string rawPath in rawPaths)
            {
                byte[] rawHash=SHA256.HashData(await File.ReadAllBytesAsync(rawPath));
                await using var loader=new PhotoImageLoader();using var raw=await loader.LoadLinearFullAsync(rawPath);
                using var result=processor.Process(raw,new EditSettings {Exposure=-1});
                string destination=Path.Combine(directory,Path.GetFileName(rawPath)+".png");
                await PhotoExportStore.SavePng16Async(rawPath,destination,result);
                var native=Png16Decoder.Decode(destination,bridge);double maximum=0;
                for(int i=0;i<native.Rgba.Length;i++)
                    maximum=Math.Max(maximum,Math.Abs(native.Rgba[i]-(i%4==3?result.Pixels[i]:Encode(result.Pixels[i]))));
                Check(native.Width==raw.Width && native.Height==raw.Height && maximum<1.1/65535,
                    "actual RAW full-resolution PNG16 export matches analytic pixel reference: "+Path.GetFileName(rawPath));
                Check(SHA256.HashData(await File.ReadAllBytesAsync(rawPath)).SequenceEqual(rawHash),"actual RAW PNG16 export preserves source: "+Path.GetFileName(rawPath));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHOTOLAB_PNG16_BRIDGE",previous);
            Directory.Delete(directory,recursive:true);
        }
    }
    private static double Encode(float value)
    {
        double bounded=Math.Clamp((double)value,0,1);
        return bounded<=0.0031308?bounded*12.92:1.055*Math.Pow(bounded,1/2.4)-0.055;
    }
    private sealed class ObservedStream(Stream inner,Action afterWrite) : Stream
    {
        private readonly Stream _inner=inner;
        private readonly Action _afterWrite=afterWrite;
        public override bool CanRead=>false;
        public override bool CanSeek=>false;
        public override bool CanWrite=>true;
        public override long Length=>_inner.Length;
        public override long Position {get=>_inner.Position;set=>throw new NotSupportedException();}
        public override void Write(ReadOnlySpan<byte> buffer) {_inner.Write(buffer);_afterWrite();}
        public override void Write(byte[] buffer,int offset,int count)=>Write(buffer.AsSpan(offset,count));
        public override void Flush()=>_inner.Flush();
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();
        // Keep the atomic store's file alive until its own flush/disposal completes.
    }
    private static async Task RejectAsync<T>(Func<Task> action,string message) where T:Exception
    {
        try {await action();} catch(T) {Check(true,message);return;} throw new Exception("FAIL: "+message);
    }
    private static void Check(bool condition,string message)
    {
        if(!condition) throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);
    }
}
