using PhotoLab.Infrastructure;

static class PhotoFolderTests
{
    public static async Task RunAsync()
    {
        string directory=Path.Combine(Path.GetTempPath(),"photolab-folder-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach(string name in new[]{"z.JPG","A.png","middle.NEF","notes.txt","sidecar.jpg.photolab.json"})
                await File.WriteAllTextAsync(Path.Combine(directory,name),"Discovery fixture; not an image payload.");
            Directory.CreateDirectory(Path.Combine(directory,"nested"));await File.WriteAllTextAsync(Path.Combine(directory,"nested","hidden.png"),"Not scanned recursively.");
            var files=await PhotoFolder.ScanAsync(directory);
            Check(files.Select(p=>p.Name).SequenceEqual(new[]{"A.png","middle.NEF","z.JPG"}),"folder discovery filters photo extensions case-insensitively and sorts deterministically");
            Check(files.All(p=>Path.IsPathFullyQualified(p.Path)),"folder discovery returns full paths without decoding payloads");
            Check(File.ReadAllText(Path.Combine(directory,"A.png")).StartsWith("Discovery"),"folder discovery never changes original files");
            string empty=Path.Combine(directory,"empty");Directory.CreateDirectory(empty);
            Check((await PhotoFolder.ScanAsync(empty)).Count==0,"empty folder discovery succeeds with no candidates");
            try {await PhotoFolder.ScanAsync(directory,new CancellationToken(true));throw new Exception("FAIL: cancellation ignored");}
            catch(OperationCanceledException) {Check(true,"folder discovery honors pre-cancellation");}
            try {await PhotoFolder.ScanAsync(Path.Combine(directory,"missing"));throw new Exception("FAIL: missing folder accepted");}
            catch(DirectoryNotFoundException) {Check(true,"missing directory returns an actionable error");}
            string large=Path.Combine(directory,"large");Directory.CreateDirectory(large);
            for(int i=0;i<2000;i++) File.WriteAllBytes(Path.Combine(large,$"photo-{i:D4}.png"),[]);
            Check((await PhotoFolder.ScanAsync(large)).Count==2000,"large folder lists candidates without allocating decoded image buffers");
        }
        finally {Directory.Delete(directory,recursive:true);}
    }
    private static void Check(bool condition,string message)
    {
        if(!condition) throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);
    }
}
