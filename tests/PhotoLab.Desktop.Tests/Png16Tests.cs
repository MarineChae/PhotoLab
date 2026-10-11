using System.Buffers.Binary;
using System.IO.Compression;
using PhotoLab.Desktop;
using PhotoLab.Core;
using PhotoLab.Imaging;
using PhotoLab.Infrastructure;
using SkiaSharp;

/// <summary>Real PNG16 fixtures covering all code values, five filters, Adam7 and original ICC input.</summary>
static class Png16Tests
{
    // LCMS-generated D50 gray profile with gamma 2.2; profile declares "No copyright, use freely".
    private const string GRAY_ICC_BASE64 = "AAABXGxjbXMEQAAAbW50ckdSQVlYWVogB+oACgALAAMAIgAXYWNzcEFQUEwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAPbWAAEAAAAA0y1sY21zAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEZGVzYwAAALQAAAA2Y3BydAAAAOwAAABMd3RwdAAAATgAAAAUa1RSQwAAAUwAAAAQbWx1YwAAAAAAAAABAAAADGVuVVMAAAAaAAAAHABnAHIAYQB5ACAAYgB1AGkAbAB0AC0AaQBuAABtbHVjAAAAAAAAAAEAAAAMZW5VUwAAADAAAAAcAE4AbwAgAGMAbwBwAHkAcgBpAGcAaAB0ACwAIAB1AHMAZQAgAGYAcgBlAGUAbAB5WFlaIAAAAAAAAPbcAAEAAAAA0zpwYXJhAAAAAAAAAAAAAjMz";
    public static async Task RunBundledAsync()
    {
        string? previousBridge=Environment.GetEnvironmentVariable("PHOTOLAB_PNG16_BRIDGE");
        string? previousLcms=Environment.GetEnvironmentVariable("PHOTOLAB_LCMS_LIBRARY");
        string directory=Path.Combine(Path.GetTempPath(),"photolab-bundled-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Environment.SetEnvironmentVariable("PHOTOLAB_PNG16_BRIDGE",null);
            Environment.SetEnvironmentVariable("PHOTOLAB_LCMS_LIBRARY",null);
            Check(ColorLibraries.Png16==Path.Combine(AppContext.BaseDirectory,"libPhotoLab.NativePng.dylib"),
                "PNG16 library resolves beside the published app without an override");
            Check(ColorLibraries.LittleCms==Path.Combine(AppContext.BaseDirectory,"liblcms2.dylib"),
                "LCMS library resolves beside the published app without an override");
            string path=Path.Combine(directory,"precision.png");
            Write(path,3,1,6,(x,y,c)=>c==3?(ushort)32769:(ushort)(32768+x));
            await using var loader=new PhotoImageLoader();
            using var loaded=await loader.LoadLinearFullAsync(path);
            using var native=new LittleCmsTransform(ColorLibraries.LittleCms!,[]);
            float[] encoded=Enumerable.Range(0,12).Select(i=>(i%4==3?32769:32768+i/4)/65535f).ToArray();
            using var expected=native.Process(3,1,encoded);
            Check(loaded.Pixels.SequenceEqual(expected.Pixels),"published app automatically uses bundled PNG16 and LCMS engines");
            Check(loaded.Pixels[0]<loaded.Pixels[4] && loaded.Pixels[4]<loaded.Pixels[8] && loaded.Pixels[3]==32769/65535f,
                "automatic bundled input preserves adjacent 16-bit RGB and alpha");
            string gray=Path.Combine(directory,"gray-icc.png");
            Write(gray,3,1,0,(x,y,c)=>(ushort)(32768+x),icc:Convert.FromBase64String(GRAY_ICC_BASE64));
            using var grayImage=await loader.LoadLinearFullAsync(gray);
            Check(Math.Abs(grayImage.Pixels[0]-Math.Pow(32768/65535.0,2.2))<0.001 && grayImage.Pixels[3]==1,
                "bundled RGB engine preserves grayscale ICC through existing Skia conversion");
            Check(grayImage.Pixels[0]<grayImage.Pixels[4],"bundled grayscale ICC input retains adjacent 16-bit values");
            byte[] invalidGray=Convert.FromBase64String(GRAY_ICC_BASE64);invalidGray[36]=0;
            Write(gray,3,1,0,(x,y,c)=>(ushort)(32768+x),icc:invalidGray);
            Reject<InvalidDataException>(()=>SkiaImageCodec.LoadLinearFull(gray),"invalid grayscale ICC fails visibly in the bundled path");
            Environment.SetEnvironmentVariable("PHOTOLAB_PNG16_BRIDGE",Path.Combine(directory,"missing.dylib"));
            Check(ColorLibraries.Png16==Path.Combine(directory,"missing.dylib"),"explicit override takes priority over a bundled library");
            Reject<DllNotFoundException>(()=>SkiaImageCodec.LoadLinearFull(path),"invalid explicit bridge reports an error instead of silently changing precision");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHOTOLAB_PNG16_BRIDGE",previousBridge);
            Environment.SetEnvironmentVariable("PHOTOLAB_LCMS_LIBRARY",previousLcms);
            Directory.Delete(directory,recursive:true);
        }
    }

    public static async Task RunAsync(string bridge,string? lcms)
    {
        string directory=Path.Combine(Path.GetTempPath(),"photolab-png16-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string? previousBridge=Environment.GetEnvironmentVariable("PHOTOLAB_PNG16_BRIDGE");
        string? previousLcms=Environment.GetEnvironmentVariable("PHOTOLAB_LCMS_LIBRARY");
        try
        {
            Environment.SetEnvironmentVariable("PHOTOLAB_PNG16_BRIDGE",bridge);
            Environment.SetEnvironmentVariable("PHOTOLAB_LCMS_LIBRARY",null);
            ushort Sample(int x,int y,int channel) => channel switch
            { 0=>(ushort)(y*256+x),1=>(ushort)(65535-y*256-x),2=>(ushort)((y*256+x)^0x5555),_=>(ushort)(y*256+x) };
            string ramp=Path.Combine(directory,"ramp.png");
            Write(ramp,256,256,6,Sample);
            byte[] sourceHash=System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(ramp));
            Check(Png16Decoder.Is16Bit(ramp),"PNG16 header is recognized");
            var decoded=Png16Decoder.Decode(ramp,bridge);
            bool exact=true;
            for(int y=0;y<256;y++) for(int x=0;x<256;x++) for(int c=0;c<4;c++)
                exact &= decoded.Rgba[(y*256+x)*4+c] == Sample(x,y,c)/65535f;
            Check(exact,"all 65,536 RGB/alpha codes survive five PNG filters and ushort normalization");
            using var working=SkiaImageCodec.LoadLinearFull(ramp);
            double error=0;
            for(int i=0;i<decoded.Rgba.Length;i++)
                error=Math.Max(error,Math.Abs(working.Pixels[i]-(i%4==3?decoded.Rgba[i]:Linear(decoded.Rgba[i]))));
            Check(error<0.00003,$"default PNG16 working conversion matches analytic transfer (max error {error:G6})");
            int middle=32768*4;
            Check(working.Pixels[middle]<working.Pixels[middle+4] && working.Pixels[middle+4]<working.Pixels[middle+8],
                "adjacent 16-bit midtones remain distinct in working float");
            bool increasing=true;
            for(int i=4;i<working.Pixels.Length;i+=4) increasing &= working.Pixels[i]>working.Pixels[i-4];
            Check(increasing,"all 65,536 sRGB codes remain ordered and distinct in working float");
            await using var loader=new PhotoImageLoader();
            using var loaded=await loader.LoadLinearFullAsync(ramp);
            Check(loaded.Pixels.SequenceEqual(working.Pixels),"app float loader retains PNG16 precision");
            Check(working.Pixels[3]==0 && working.Pixels[^1]==1 && working.Pixels[middle+3]==32768/65535f,
                "straight alpha retains zero, full and fractional 16-bit coverage");
            var processor=new FloatImageProcessor();
            using var edited=processor.Process(loaded,new EditSettings {Exposure=-1});
            Check(edited.Pixels[middle]==working.Pixels[middle]*0.5f && edited.Pixels[middle+3]==working.Pixels[middle+3],
                "PNG16 editing applies exposure in working float and preserves alpha");
            string export=Path.Combine(directory,"edited.png");
            await PhotoExportStore.SaveAsync(ramp,export,SkiaImageCodec.Encode(ImageConvert.ToSrgb8(edited),jpeg:false));
            using var exported=SkiaImageCodec.LoadLinearFull(export);
            Check(exported.Width==256 && exported.Height==256 && Math.Abs(exported.Pixels[middle]-edited.Pixels[middle])<0.003f,
                "PNG16 source flows through existing sRGB8 export with expected output quantization");

            foreach(int color in new[]{0,2,4,6})
            {
                string path=Path.Combine(directory,$"adam7-{color}.png");
                ushort Value(int x,int y,int c)=>(ushort)(1025+x*139+y*357+c*811);
                Write(path,13,9,color,Value,interlace:true);
                var image=Png16Decoder.Decode(path,bridge);bool match=true;
                for(int y=0;y<9;y++) for(int x=0;x<13;x++) for(int c=0;c<4;c++)
                {
                    ushort expected=c==3?(color==4?Value(x,y,1):color==6?Value(x,y,3):ushort.MaxValue):
                        color is 0 or 4?Value(x,y,0):Value(x,y,c);
                    match &= image.Rgba[(y*13+x)*4+c]==expected/65535f;
                }
                Check(match,"Adam7 odd-sized PNG16 decodes grayscale/RGB/alpha: "+color);
            }
            foreach(int color in new[]{0,2})
            {
                string path=Path.Combine(directory,$"transparent-{color}.png");
                Write(path,2,1,color,(x,y,c)=>(ushort)(32768+x),transparent:32768);
                var image=Png16Decoder.Decode(path,bridge);
                Check(image.Rgba[3]==0 && image.Rgba[7]==1 && image.Rgba[0]==32768/65535f,
                    "16-bit tRNS matching preserves hidden RGB and binary alpha: "+color);
            }
            string gammaPath=Path.Combine(directory,"linear-gamma.png");
            Write(gammaPath,3,1,6,(x,y,c)=>c==3?ushort.MaxValue:(ushort)(32768+x),gamma:100000);
            using var gammaImage=SkiaImageCodec.LoadLinearFull(gammaPath);
            Check(Math.Abs(gammaImage.Pixels[0]-32768/65535f)<0.00003 && gammaImage.Pixels[0]<gammaImage.Pixels[4],
                "PNG16 gAMA metadata avoids incorrect sRGB decoding");

            Reject<OperationCanceledException>(()=>Png16Decoder.Decode(ramp,bridge,new CancellationToken(true)),"pre-canceled PNG16 decode rejected");
            Reject<OperationCanceledException>(()=>SkiaImageCodec.LoadLinearFull(ramp,new CancellationToken(true)),"pre-canceled app PNG16 load rejected");
            Reject<ArgumentException>(()=>ImageConvert.FromSrgb32(1,1,new float[]{float.MaxValue,0,0,1}),
                "normalized sRGB conversion rejects input that could overflow working float");
            string broken=Path.Combine(directory,"broken.png");
            byte[] valid=File.ReadAllBytes(ramp);File.WriteAllBytes(broken,valid[..(valid.Length/2)]);
            Reject<InvalidDataException>(()=>Png16Decoder.Decode(broken,bridge),"truncated PNG16 rejected without partial output");
            valid[29]^=1;File.WriteAllBytes(broken,valid);
            Reject<InvalidDataException>(()=>Png16Decoder.Decode(broken,bridge),"bad PNG16 chunk CRC rejected");
            string p3=Path.Combine(directory,"p3.png");
            using var space=SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb,SKColorSpaceXyz.DisplayP3);
            using var bitmap=new SKBitmap(new SKImageInfo(1,1,SKColorType.Rgba8888,SKAlphaType.Unpremul,space));
            bitmap.Erase(SKColors.Black);using var skImage=SKImage.FromBitmap(bitmap);
            using var png=skImage.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(p3,png.ToArray());
            Check(!Png16Decoder.Is16Bit(p3),"ordinary PNG8 does not select PNG16 decoding");
            Reject<InvalidDataException>(()=>Png16Decoder.Decode(p3,bridge),"PNG16 bridge rejects an 8-bit source");
            byte[] icc=EmbeddedIccProfile.Read(p3);
            Write(p3,3,1,6,(x,y,c)=>c==3?(ushort)32769:c==0?(ushort)(65535-x):(ushort)0,icc:icc);
            using var profileImage=SkiaImageCodec.LoadLinearFull(p3);
            Check(profileImage.Pixels[0]>1.22f && profileImage.Pixels[1]<-0.04f && profileImage.Pixels[3]==32769/65535f,
                "PNG16 default profile conversion retains extended gamut and exact alpha");
            Check(profileImage.Pixels[0]>profileImage.Pixels[4],"PNG16 wide-gamut adjacent source values remain distinct");
            if(lcms!=null)
            {
                Environment.SetEnvironmentVariable("PHOTOLAB_LCMS_LIBRARY",lcms);
                using var native=SkiaImageCodec.LoadLinearFull(ramp);double nativeError=0;
                for(int i=0;i<decoded.Rgba.Length;i++)
                    nativeError=Math.Max(nativeError,Math.Abs(native.Pixels[i]-(i%4==3?decoded.Rgba[i]:Linear(decoded.Rgba[i]))));
                Check(nativeError<0.00003,$"PNG16 LCMS agrees with analytic transfer across all codes (max error {nativeError:G6})");
                Check(native.Pixels[middle]<native.Pixels[middle+4],"LCMS input retains adjacent 16-bit samples");
                bool nativeIncreasing=true;
                for(int i=4;i<native.Pixels.Length;i+=4) nativeIncreasing &= native.Pixels[i]>native.Pixels[i-4];
                Check(nativeIncreasing,"LCMS preserves all 65,536 ordered sRGB codes");
                using var nativeGamma=SkiaImageCodec.LoadLinearFull(gammaPath);
                Check(nativeGamma.Pixels.SequenceEqual(gammaImage.Pixels),"LCMS selection retains non-ICC PNG gamma metadata");
                using var nativeP3=SkiaImageCodec.LoadLinearFull(p3);
                Check(Math.Abs(nativeP3.Pixels[0]-1.224745f)<0.002 && Math.Abs(nativeP3.Pixels[1]+0.042058f)<0.002 &&
                    nativeP3.Pixels[3]==32769/65535f && nativeP3.Pixels[0]>nativeP3.Pixels[4],
                    "PNG16 original ICC reaches LCMS without RGB/alpha quantization");
            }
            Check(decoded.Rgba[middle]==32768/65535f,"returned encoded pixels remain owned after native release and later decodes");
            Check(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(ramp)).SequenceEqual(sourceHash),
                "PNG16 decode/edit/export leaves original bytes unchanged");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHOTOLAB_PNG16_BRIDGE",previousBridge);
            Environment.SetEnvironmentVariable("PHOTOLAB_LCMS_LIBRARY",previousLcms);
            Directory.Delete(directory,recursive:true);
        }
    }

    private static double Linear(float value)=>value<=0.04045?value/12.92:Math.Pow((value+0.055)/1.055,2.4);

    // Generate source code values independently of libpng; include every PNG filter and optional Adam7 packing.
    private static void Write(string path,int width,int height,int color,Func<int,int,int,ushort> sample,
        bool interlace=false,ushort? transparent=null,byte[]? icc=null,uint? gamma=null)
    {
        int channels=color switch {0=>1,2=>3,4=>2,6=>4,_=>throw new ArgumentException()};
        using var stream=File.Create(path);stream.Write(new byte[]{137,80,78,71,13,10,26,10});
        var header=new byte[13];BinaryPrimitives.WriteUInt32BigEndian(header,(uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4),(uint)height);header[8]=16;header[9]=(byte)color;header[12]=(byte)(interlace?1:0);
        Chunk(stream,"IHDR",header);
        if(gamma.HasValue)
        {
            var gammaBytes=new byte[4];BinaryPrimitives.WriteUInt32BigEndian(gammaBytes,gamma.Value);Chunk(stream,"gAMA",gammaBytes);
        }
        if(icc!=null)
        {
            using var payload=new MemoryStream();payload.Write("profile\0\0"u8);
            using(var zlib=new ZLibStream(payload,CompressionLevel.SmallestSize,leaveOpen:true)) zlib.Write(icc);
            Chunk(stream,"iCCP",payload.ToArray());
        }
        if(transparent.HasValue)
        {
            var trns=new byte[channels*2];for(int c=0;c<channels;c++) BinaryPrimitives.WriteUInt16BigEndian(trns.AsSpan(c*2),transparent.Value);
            Chunk(stream,"tRNS",trns);
        }
        using var compressed=new MemoryStream();
        using(var zlib=new ZLibStream(compressed,CompressionLevel.SmallestSize,leaveOpen:true))
        {
            var passes=interlace?new[]{(0,0,8,8),(4,0,8,8),(0,4,4,8),(2,0,4,4),(0,2,2,4),(1,0,2,2),(0,1,1,2)}:new[]{(0,0,1,1)};
            foreach(var (startX,startY,stepX,stepY) in passes)
            {
                if(startX>=width || startY>=height) continue;
                int columns=(width-startX+stepX-1)/stepX;var prior=new byte[columns*channels*2];int rowIndex=0;
                for(int y=startY;y<height;y+=stepY)
                {
                    var row=new byte[prior.Length];int offset=0;
                    for(int x=startX;x<width;x+=stepX) for(int c=0;c<channels;c++)
                    { BinaryPrimitives.WriteUInt16BigEndian(row.AsSpan(offset),sample(x,y,c));offset+=2; }
                    int filter=rowIndex++%5;var filtered=new byte[row.Length+1];filtered[0]=(byte)filter;
                    for(int i=0;i<row.Length;i++)
                    {
                        int left=i>=channels*2?row[i-channels*2]:0,up=prior[i],corner=i>=channels*2?prior[i-channels*2]:0;
                        int predictor=filter switch {0=>0,1=>left,2=>up,3=>(left+up)/2,_=>Paeth(left,up,corner)};
                        filtered[i+1]=unchecked((byte)(row[i]-predictor));
                    }
                    zlib.Write(filtered);prior=row;
                }
            }
        }
        Chunk(stream,"IDAT",compressed.ToArray());Chunk(stream,"IEND",[]);
    }

    private static int Paeth(int a,int b,int c)
    {
        int p=a+b-c,pa=Math.Abs(p-a),pb=Math.Abs(p-b),pc=Math.Abs(p-c);
        return pa<=pb && pa<=pc?a:pb<=pc?b:c;
    }
    private static void Chunk(Stream stream,string type,byte[] bytes)
    {
        Span<byte> size=stackalloc byte[4];BinaryPrimitives.WriteUInt32BigEndian(size,(uint)bytes.Length);stream.Write(size);
        byte[] name=System.Text.Encoding.ASCII.GetBytes(type);stream.Write(name);stream.Write(bytes);
        uint crc=0xffffffff;
        foreach(byte value in name.Concat(bytes))
        { crc^=value;for(int i=0;i<8;i++) crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0u); }
        BinaryPrimitives.WriteUInt32BigEndian(size,~crc);stream.Write(size);
    }
    private static void Reject<T>(Action action,string message) where T:Exception
    {
        try {action();} catch(T) {Check(true,message);return;} throw new Exception("FAIL: "+message);
    }
    private static void Check(bool condition,string message)
    {
        if(!condition) throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);
    }
}
