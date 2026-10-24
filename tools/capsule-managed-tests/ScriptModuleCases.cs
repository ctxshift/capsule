using System.Text.Json;
using Capsule.Package;

static partial class Program
{
    static void ScriptModuleCases(string root)
    {
        Directory.CreateDirectory(root);
        void Package(int count, int size)
        {
            foreach (var path in Directory.GetFiles(root)) File.Delete(path);
            File.WriteAllText(Path.Combine(root,"main.js"),"void 0;");
            var assets = Enumerable.Range(0,count).Select(i=>new {key="part"+i,kind="script",path="part"+i+".js"}).ToArray();
            File.WriteAllText(Path.Combine(root,"addon.json"),JsonSerializer.Serialize(new {schemaVersion=1,apiVersion=1,id="modules.fixture",entry="main.js",assets}));
            foreach(var asset in assets) File.WriteAllBytes(Path.Combine(root,asset.path),new byte[size]);
        }
        Package(2,40000);
        Check(UiDirectoryPackageLoader.TryLoad(root,out var first,out var error),"multiple bounded modules: "+error);
        var bytes=first.CopyScriptModules(); bytes[0][0]=1;
        Check(first.CopyScriptModules()[0][0]==0,"module copies immutable");
        Check(!first.TryCopyData("part0",out _),"scripts are not data grants");
        File.WriteAllText(Path.Combine(root,"part1.js"),"changed");
        Check(UiDirectoryPackageLoader.TryLoad(root,out var changed,out _) && changed.ContentHash!=first.ContentHash,"module changes revision identity");
        File.Delete(Path.Combine(root,"part1.js"));
        Check(!UiDirectoryPackageLoader.TryLoad(root,out _,out _),"missing module rejected");
        Package(1,65537); Check(!UiDirectoryPackageLoader.TryLoad(root,out _,out _),"per-script bound");
        Package(5,60000); Check(!UiDirectoryPackageLoader.TryLoad(root,out _,out _),"aggregate source bound");
        Package(9,1); Check(!UiDirectoryPackageLoader.TryLoad(root,out _,out _),"module count bound");
    }
}
