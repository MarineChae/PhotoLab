using System.Text.Json.Nodes;
using PhotoLab.Core;
using PhotoLab.Infrastructure;

static class PresetTests
{
    public static async Task RunAsync()
    {
        string directory=Path.Combine(Path.GetTempPath(),"photolab-presets-"+Guid.NewGuid().ToString("N"));
        var store=new PresetStore(directory);
        try
        {
            Check((await store.ListAsync()).Count==0 && !Directory.Exists(directory),"listing an empty preset store does not create user files");
            var settings=new EditSettings {Exposure=1.25,Contrast=-20,Highlights=-15,Shadows=23,Temperature=12,Tint=-4,Saturation=7,Vibrance=18};
            await store.SaveAsync("  여행 / ../따뜻함  ",settings);
            var listed=await new PresetStore(directory).ListAsync();
            Check(listed.Count==1 && listed[0].Name=="여행 / ../따뜻함" && listed[0].Settings==settings,
                "Unicode preset names and all eight adjustments survive a new store instance");
            string path=Directory.GetFiles(directory).Single();
            Check(Path.GetDirectoryName(path)==directory && Path.GetFileName(path).Length==85,"display-name separators cannot escape the preset directory");
            byte[] original=await File.ReadAllBytesAsync(path);
            await Reject<IOException>(()=>store.SaveAsync("여행 / ../따뜻함",new EditSettings()),"duplicate preset names never replace existing corrections");
            Check((await File.ReadAllBytesAsync(path)).SequenceEqual(original),"duplicate save preserves preset bytes");
            await Reject<InvalidDataException>(()=>store.SaveAsync("",settings),"blank preset names rejected");
            await Reject<InvalidDataException>(()=>store.SaveAsync("Bad",settings with {Exposure=double.NaN}),"nonfinite preset adjustments rejected");
            await Reject<InvalidDataException>(()=>store.SaveAsync("Bad",settings with {Contrast=101}),"out-of-range preset adjustments rejected");
            await Reject<OperationCanceledException>(()=>store.SaveAsync("Canceled",settings,new CancellationToken(true)),"canceled preset save rejected");
            Check(Directory.GetFiles(directory).Length==1,"canceled or invalid saves create no extra preset files");
            var node=JsonNode.Parse(original)!;
            foreach(var change in new Action<JsonNode>[]
            {
                data=>data["Version"]=2,
                data=>data["Settings"]!["Exposure"]=5,
                data=>data["Settings"]!.AsObject().Remove("Tint"),
                data=>data["Settings"]!["exposure"]=0,
                data=>data["CameraWhiteBalance"]=new JsonObject(),
                data=>data["Name"]="Different name"
            })
            {
                var changed=node.DeepClone();change(changed);await File.WriteAllTextAsync(path,changed.ToJsonString());
                await Reject<InvalidDataException>(async()=>{_ = await store.ListAsync();},"unsupported/corrupt/ambiguous preset schema is rejected");
            }
            await File.WriteAllBytesAsync(path,original);
            await store.SaveAsync("Other",new EditSettings());
            Check((await store.ListAsync()).Count==2,"preset list includes multiple independently saved names");
            await store.DeleteAsync("Other");Check((await store.ListAsync()).Count==1,"deleting a selected preset leaves other presets intact");
            await Reject<OperationCanceledException>(()=>store.DeleteAsync("여행 / ../따뜻함",new CancellationToken(true)),"canceled deletion preserves the selected preset");
            Check((await store.ListAsync()).Count==1,"original named preset remains after rejected deletion");
            await store.SaveAsync("MixedCase",settings);
            await Reject<IOException>(()=>store.SaveAsync("mixedcase",settings),"preset identity is case-insensitive across platform filesystems");
        }
        finally {if(Directory.Exists(directory)) Directory.Delete(directory,recursive:true);}
    }
    private static async Task Reject<T>(Func<Task> action,string message) where T:Exception
    {
        try {await action();} catch(T) {Check(true,message);return;} throw new Exception("FAIL: "+message);
    }
    private static void Check(bool condition,string message)
    {
        if(!condition) throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);
    }
}
