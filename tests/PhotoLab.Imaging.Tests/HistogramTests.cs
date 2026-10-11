using PhotoLab.Imaging;

static class HistogramTests
{
    public static void Run()
    {
        using var source = FloatImageBuffer.FromLinearRgba32(5,1,new float[]
        {0,0,0,1, 1,1,1,1, 0.5f,0.25f,0.125f,0.5f, -0.1f,1.2f,0.5f,1, 100,-100,0,0});
        var data=ImageHistogram.Compute(source);
        Check(data.PixelCount==4 && data.Red.ToArray().Sum()==4 && data.Green.ToArray().Sum()==4 &&
            data.Blue.ToArray().Sum()==4 && data.Luminance.ToArray().Sum()==4,"histogram excludes transparent pixels and conserves every channel count");
        Check(data.Red[0]==2 && data.Red[128]==1 && data.Red[255]==1 && data.Green[64]==1 && data.Blue[32]==1,
            "linear bins retain boundary, HDR and fractional channel positions");
        Check(data.ShadowClipped==2 && data.HighlightClipped==2,"clipping counts each affected visible pixel once, including exact endpoints");
        int expected=(int)((0.2126*0.5+0.7152*0.25+0.0722*0.125)*256);
        Check(data.Luminance[expected]==1,"luminance bins match independent linear Rec.709 weights");
        source.Dispose();Check(data.Red[128]==1,"histogram owns counts after the source buffer is released");
        using var transparent=FloatImageBuffer.FromLinearRgba32(1,1,new float[]{1,1,1,0});
        var empty=ImageHistogram.Compute(transparent);Check(empty.PixelCount==0 && empty.HighlightClipped==0,"fully transparent image has no histogram or clipping counts");
        using var ramp=FloatImageBuffer.FromLinearRgba32(256,1,Enumerable.Range(0,256).SelectMany(i=>new float[]{i/256f,i/256f,i/256f,1}).ToArray());
        var bins=ImageHistogram.Compute(ramp);
        Check(bins.Red.ToArray().All(count=>count==1) && bins.Green.SequenceEqual(bins.Red) && bins.Blue.SequenceEqual(bins.Red),
            "all 256 bin boundaries follow the declared floor mapping");
        using var processorResult=new FloatImageProcessor().Process(ramp,new PhotoLab.Core.EditSettings {Exposure=1});
        Check(ImageHistogram.Compute(processorResult).HighlightClipped==128,"histogram measures adjusted float values before display clipping");
        try {ImageHistogram.Compute(ramp,new CancellationToken(true));throw new Exception("FAIL: cancellation ignored");}
        catch(OperationCanceledException) {Check(true,"histogram honors cancellation before scanning pixels");}
    }
    private static void Check(bool condition,string message)
    {
        if(!condition) throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);
    }
}
