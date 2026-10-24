using Capsule.Package;
static partial class Program
{
    static void EncodedImageCases(string root)
    {
        foreach (var file in Directory.GetFiles(root)) File.Delete(file);
        File.WriteAllText(Path.Combine(root,"addon.json"), """{"schemaVersion":1,"apiVersion":1,"id":"image.fixture","entry":"main.js","assets":[{"kind":"image","key":"image","path":"image.png"}]}""");
        File.WriteAllText(Path.Combine(root,"main.js"), "void 0;");
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");
        // Trailing padding exercises encoded byte admission independently of pixel dimensions.
        Array.Resize(ref png, 2 * 1024 * 1024 + 1);
        File.WriteAllBytes(Path.Combine(root,"image.png"), png);
        Check(!UiDirectoryPackageLoader.TryLoad(root,out _,out _), "Community image cap remains two MiB.");
        var limits = new UiPackageLimits(8*1024*1024,32*1024*1024,2048,64,4*1024*1024);
        Check(UiDirectoryPackageLoader.TryLoad(root,limits,out _,out var error), "Host encoded image admission: " + error);
        Array.Resize(ref png,4*1024*1024+1);File.WriteAllBytes(Path.Combine(root,"image.png"),png);
        Check(!UiDirectoryPackageLoader.TryLoad(root,limits,out _,out _), "Host encoded image cap remains bounded.");
    }
}
