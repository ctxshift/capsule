using System.Text;
using Capsule.Package;

static partial class Check
{
    const string Manifest =
        """{"schemaVersion":1,"apiVersion":1,"id":"example.fixture","entry":"main.js","assets":[{"key":"settings","kind":"data","path":"data/settings.json"},{"key":"portrait","kind":"image","path":"images/portrait.png"}]}""";

    static void Write(string root, string manifest = Manifest)
    {
        Directory.CreateDirectory(Path.Combine(root, "data"));
        Directory.CreateDirectory(Path.Combine(root, "images"));
        File.WriteAllBytes(Path.Combine(root, "addon.json"), Json(manifest));
        File.WriteAllText(Path.Combine(root, "main.js"), "globalThis.portableUiUpdate = () => {};", Encoding.UTF8);
        File.WriteAllBytes(Path.Combine(root, "data/settings.json"), Json("""{"name":"before"}"""));
        File.WriteAllBytes(Path.Combine(root, "images/portrait.png"), Png());
    }

    static void ManifestCases()
    {
        True(UiPackageManifest.TryParse(Json(Manifest), out var parsed, out _)
            && parsed!.Declarations.Count == 2, "valid JSON manifest");
        string[] invalid =
        {
            Manifest.Replace("\"schemaVersion\":1", "\"schemaVersion\":999999999999999999999999"),
            Manifest.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"),
            Manifest.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"SchemaVersion\":1"),
            Manifest.Replace("\"id\":\"example.fixture\"", "\"id\":\"../escape\""),
            Manifest.Replace("images/portrait.png", "images/CON.png"),
            Manifest.Replace("images/portrait.png", "../portrait.png"),
            Manifest.Replace("images/portrait.png", "/portrait.png"),
            Manifest.Replace("images/portrait.png", "data/SETTINGS.json"),
            Manifest.Replace("images/portrait.png", "images/portrait.png:stream"),
            Manifest.Replace("images/portrait.png", "C:/portrait.png"),
            Manifest.Replace("images/portrait.png", "images/portrait.gif"),
            Manifest.Replace("\"apiVersion\":1", "\"apiVersion\":2"),
            Manifest.Replace("\"key\":\"portrait\"", "\"key\":\"settings\""),
            Manifest.Replace("\"assets\":[", "\"assets\":[/*bad*/"),
            Manifest.Replace("\"id\":", "'id':"),
            Manifest.Replace("\"id\":", "id:"),
            Manifest.Replace("\"assets\":[", "\"assets\":[,"),
            Manifest.Replace("portrait.png\"}", "portrait.png\",}"),
            Manifest + "true"
        };
        foreach (string source in invalid)
            True(!UiPackageManifest.TryParse(Json(source), out _, out _), "manifest rejects " + source);
        byte[] malformed = { 0x7B, 0xFF, 0x7D };
        True(!UiPackageManifest.TryParse(malformed, out _, out _), "strict UTF-8");
    }

    static void LoaderCases(string root)
    {
        Write(root);
        True(UiDirectoryPackageLoader.TryLoad(root, out var first, out var error),
            "load package: " + error);
        var script = first!.CopyScript(); script[0] = 99;
        True(first.CopyScript()[0] != 99, "script immutable");
        True(first.TryCopyData("settings", out var data), "declared data");
        data![0] = 99;
        True(first.TryCopyData("settings", out var next) && next![0] == (byte)'{',
            "data immutable");
        True(!first.TryCopyData("undeclared", out _), "undeclared data unavailable");
        True(first.TryCopyImage("portrait", out var encoded, out var descriptor) &&
            descriptor!.Width == 8 && descriptor.DecodedBytes == 256, "image descriptor");
        encoded![0] = 0;
        True(first.TryCopyImage("portrait", out var again, out _) && again![0] == 137,
            "image immutable");

        File.WriteAllBytes(Path.Combine(root, "data/settings.json"), Json("""{"name":"after"}"""));
        True(UiDirectoryPackageLoader.TryLoad(root, out var second, out _)
            && second!.ContentHash != first.ContentHash &&
            first.TryCopyData("settings", out var oldData) &&
            Encoding.UTF8.GetString(oldData!).Contains("before"), "revision atomicity");

        var decoder = new FakeDecoder();
        using var cache = new UiPackageImageCache(decoder);
        using var oldLease = cache.Open(first);
        using var otherLease = cache.Open(first);
        using var changedLease = cache.Open(second);
        bool acquiredOld = oldLease.TryAcquireImage("portrait", out var oldHandle, out _);
        bool acquiredOther = otherLease.TryAcquireImage("portrait", out var otherHandle, out _);
        True(acquiredOld && acquiredOther &&
            decoder.Calls == 1 && cache.CachedImageCount == 1, "shared decoded cache");
        True(!oldLease.TryResolve(otherHandle, out _), "foreign generation rejected");
        True(changedLease.TryAcquireImage("portrait", out _, out _) &&
            decoder.Calls == 1 && cache.CachedImageCount == 1,
            "unchanged image reused across data-only revision");
        True(oldLease.TryAcquireImageKey("portrait", out var token, out _) &&
            oldLease.TryResolveImageKey(token, out _) &&
            !otherLease.TryResolveImageKey(token, out _), "opaque image key ownership");
        oldLease.Dispose();
        True(!oldLease.TryResolve(oldHandle, out _) &&
            !oldLease.TryResolveImageKey(token, out _) && cache.CachedImageCount == 1,
            "one unload retains other owner");
        otherLease.Dispose();
        True(cache.CachedImageCount == 1 && !decoder.Last!.Disposed,
            "changed revision still owns shared image");
        changedLease.Dispose();
        True(cache.CachedImageCount == 0 && decoder.Last.Disposed,
            "last unload releases image");

        File.WriteAllBytes(Path.Combine(root, "images/portrait.png"), Png(0, 8));
        True(!UiDirectoryPackageLoader.TryLoad(root, out _, out _), "invalid image dimensions");
        File.WriteAllBytes(Path.Combine(root, "images/portrait.png"), Png());
        File.WriteAllBytes(Path.Combine(root, "extra.txt"), Json("surprise"));
        True(!UiDirectoryPackageLoader.TryLoad(root, out _, out _), "undeclared file");
        File.Delete(Path.Combine(root, "extra.txt"));
        File.Delete(Path.Combine(root, "data/settings.json"));
        True(!UiDirectoryPackageLoader.TryLoad(root, out _, out _), "missing declared file");
        File.WriteAllBytes(Path.Combine(root, "data/settings.json"), new byte[UiDirectoryPackageLoader.MaxDataBytes + 1]);
        True(!UiDirectoryPackageLoader.TryLoad(root, out _, out _), "data byte quota");
    }

    static void AttackCases(string root)
    {
        Write(root);
        File.WriteAllBytes(Path.Combine(root, "main.js"), new byte[UiDirectoryPackageLoader.MaxScriptBytes + 1]);
        True(!UiDirectoryPackageLoader.TryLoad(root, out _, out _), "script quota");
        Write(root);
        File.WriteAllBytes(Path.Combine(root, "images/portrait.png"), new byte[UiDirectoryPackageLoader.MaxImageBytes + 1]);
        True(!UiDirectoryPackageLoader.TryLoad(root, out _, out _), "encoded image quota");
        Write(root);
        File.WriteAllBytes(Path.Combine(root, "images/portrait.png"), Png(2049, 1));
        True(!UiDirectoryPackageLoader.TryLoad(root, out _, out _), "image dimension quota");
        Write(root);
        File.WriteAllBytes(Path.Combine(root, "addon.json"), new byte[UiPackageManifest.MaxManifestBytes + 1]);
        True(!UiDirectoryPackageLoader.TryLoad(root, out _, out _), "manifest quota");
        Write(root);
        string archive = root + ".zip";
        File.WriteAllBytes(archive, new byte[] { 80, 75, 3, 4 });
        try { True(!UiDirectoryPackageLoader.TryLoad(archive, out _, out _), "archives unsupported"); }
        finally { File.Delete(archive); }
        string linked = Path.Combine(root, "linked");
        Directory.CreateSymbolicLink(linked, Path.GetTempPath());
        try { True(!UiDirectoryPackageLoader.TryLoad(root, out _, out _), "symlink rejected"); }
        finally { Directory.Delete(linked); }
        var declarations = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            string path = "images/large" + i + ".png";
            declarations.Add("{\"key\":\"large" + i + "\",\"kind\":\"image\",\"path\":\"" + path + "\"}");
            File.WriteAllBytes(Path.Combine(root, path), Png(2048, 2048));
        }
        string largeManifest = Manifest.Replace("{\"key\":\"settings\",\"kind\":\"data\",\"path\":\"data/settings.json\"},{\"key\":\"portrait\",\"kind\":\"image\",\"path\":\"images/portrait.png\"}", string.Join(",", declarations));
        File.Delete(Path.Combine(root, "data/settings.json")); File.Delete(Path.Combine(root, "images/portrait.png"));
        File.WriteAllText(Path.Combine(root, "addon.json"), largeManifest);
        True(!UiDirectoryPackageLoader.TryLoad(root, out _, out var failure) && failure.Contains("Decoded image quota"), "aggregate decoded quota");
        File.Delete(Path.Combine(root, "images/large2.png"));
        File.WriteAllText(Path.Combine(root, "addon.json"), largeManifest.Replace("," + declarations[2], ""));
        True(UiDirectoryPackageLoader.TryLoad(root, out var large, out _), "32MiB revision allowed");
        var decoder = new FakeDecoder();
        using var cache = new UiPackageImageCache(decoder);
        var leases = new List<UiPackageLease>();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                File.WriteAllText(Path.Combine(root, "addon.json"), largeManifest.Replace("," + declarations[2], "").Replace("example.fixture", "example." + i));
                True(UiDirectoryPackageLoader.TryLoad(root, out var revision, out _), "cache fixture revision");
                var lease = cache.Open(revision); leases.Add(lease);
                bool prepared = lease.TryPrepareImages(out _);
                True(prepared == (i < 2), "cache aggregate preflight");
            }
            True(decoder.Calls == 4, "over-budget decode never called");
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
        True(cache.CachedImageCount == 0, "aggregate unload cleanup");
        var padded = new byte[UiDirectoryPackageLoader.MaxImageBytes];
        Png().CopyTo(padded, 0);
        File.WriteAllBytes(Path.Combine(root, "images/large0.png"), padded);
        File.WriteAllBytes(Path.Combine(root, "images/large1.png"), padded);
        True(!UiDirectoryPackageLoader.TryLoad(root, out _, out failure) && failure.Contains("Encoded package quota"), "aggregate encoded quota");
        for (int i = 0; i < 70; i++) Directory.CreateDirectory(Path.Combine(root, "empty" + i));
        True(!UiDirectoryPackageLoader.TryLoad(root, out _, out failure) && failure.Contains("count quota"), "directory enumeration quota");
    }

    public static void Main(string[] args)
    {
        ManifestCases();
        ChangeStampCases();

        string root = Path.Combine(Path.GetTempPath(), "capsule-package-tests-" + Guid.NewGuid().ToString("N"));
        try { LoaderCases(root); AttackCases(root); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        Console.WriteLine("Capsule package checks passed: " + passed);
    }
}
