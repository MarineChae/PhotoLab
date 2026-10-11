using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using PhotoLab.Imaging;
using PhotoLab.Infrastructure;

/// <summary>One isolated 20MP encode/file-write observation; source creation and hash are outside encode timing.</summary>
static class PngExportBenchmark
{
    public static void Run(string library,string mode)
    {
        using var source=Create();
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        string path=Path.Combine(Path.GetTempPath(),"photolab-export-bench-"+Guid.NewGuid().ToString("N")+".png");
        try
        {
            long allocated=GC.GetTotalAllocatedBytes(true);var timer=Stopwatch.StartNew();
            if(mode=="buffered") File.WriteAllBytes(path,Png16Encoder.Encode(source,library));
            else if(mode=="streamed")
            {
                using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536);
                Png16Encoder.Write(source,library,file);
            }
            else throw new ArgumentException("Expected buffered or streamed mode.");
            timer.Stop();long bytesAllocated=GC.GetTotalAllocatedBytes(true)-allocated;
            using var encoded=File.OpenRead(path);
            Console.WriteLine(JsonSerializer.Serialize(new {mode,width=source.Width,height=source.Height,
                milliseconds=timer.Elapsed.TotalMilliseconds,managedAllocatedBytes=bytesAllocated,
                fileBytes=encoded.Length,sha256=Convert.ToHexString(SHA256.HashData(encoded))}));
        }
        finally {File.Delete(path);}
    }

    private static FloatImageBuffer Create()
    {
        var pixels=new float[5000*4000*4];uint state=0x50484F54;
        for(int i=0;i<pixels.Length;i+=4)
        {
            for(int c=0;c<3;c++) {state=unchecked(state*1664525+1013904223);pixels[i+c]=(state>>8)/16777215f;}
            pixels[i+3]=1;
        }
        return FloatImageBuffer.FromLinearRgba32(5000,4000,pixels);
    }
}
