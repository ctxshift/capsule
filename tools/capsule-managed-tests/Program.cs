using System.Reflection;
using System.Text;
using Capsule;
using Capsule.Package;

static partial class Program
{
    static int checks;
    static void Check(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException(reason); checks++; }

    sealed class Theme : IUiTheme
    {
        public bool AllowsStyle(string key) => key == "panel";
        public bool AllowsAsset(string key) => false;
    }
    sealed class Target : IUiCommitTarget
    {
        public int Commits;
        public bool TryCommit(UiSnapshot snapshot, out string error)
        { Commits++; error = null!; return true; }
    }
    static void Dependencies(Assembly assembly, params string[] allowed)
    {
        Check(assembly != typeof(Program).Assembly, "Consumer must use a separate library assembly.");
        foreach (var reference in assembly.GetReferencedAssemblies())
            Check(allowed.Contains(reference.Name), "Unexpected assembly dependency: " + reference.FullName);
        Check(assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName == ".NETStandard,Version=v2.1",
            "Reusable library must target .NET Standard 2.1.");
    }
    static void Main()
    {
        ThemeLimitCases();
        string modulesRoot = Path.Combine(Path.GetTempPath(), "capsule-modules-" + Guid.NewGuid().ToString("N"));
        try { ScriptModuleCases(modulesRoot); EncodedImageCases(modulesRoot); }
        finally { if (Directory.Exists(modulesRoot)) Directory.Delete(modulesRoot, true); }
        Dependencies(typeof(UiSession).Assembly, "netstandard");
        Dependencies(typeof(UiPackageRevision).Assembly, "netstandard", "Newtonsoft.Json", "Capsule.Core");
        var session = new UiSession(7, new Theme());
        Check(session.ReserveHandle(out ulong handle).Success && handle >> 32 == 7, "Neutral host reserves an owned handle.");
        var target = new Target();
        // Existing wire v1, empty atomic transaction. No engine controls or game policy.
        byte[] packet = { 0x50, 0x55, 0x49, 0x31, 1, 0, 0, 0, 0, 0, 0, 0, 16, 0, 0, 0 };
        Check(session.Submit(packet, target).Success && target.Commits == 1, "Portable core commits via injected host interface.");
        packet[0] = 0;
        Check(!session.Submit(packet, target).Success && target.Commits == 1, "Invalid wire does not commit.");

        string root = Path.Combine(Path.GetTempPath(), "capsule-managed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string manifest = """{"schemaVersion":1,"apiVersion":1,"id":"example.neutral","entry":"main.js","assets":[{"kind":"data","key":"settings","path":"settings.json"}]}""";
            File.WriteAllText(Path.Combine(root, "addon.json"), manifest, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(root, "main.js"), "globalThis.portableUiUpdate = () => {};", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(root, "settings.json"), "{\"value\":7}", new UTF8Encoding(false));
            Check(UiDirectoryPackageLoader.TryLoad(root, out var revision, out var error), "Neutral package loads: " + error);
            Check(revision.Manifest.Id == "example.neutral" && revision.Identity == "example.neutral:" + revision.ContentHash && revision.ContentHash.Length == 64, "Package identity and content hash.");
            Check(revision.TryCopyData("settings", out byte[] data) && data.Length > 0, "Declared data is available.");
            data[0] = 0;
            Check(revision.TryCopyData("settings", out var fresh) && fresh[0] == (byte)'{', "Package bytes are independent copies.");
            File.WriteAllText(Path.Combine(root, "undeclared.txt"), "unexpected");
            Check(!UiDirectoryPackageLoader.TryLoad(root, out _, out _), "Undeclared files are rejected.");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine($"Capsule neutral managed checks passed: {checks}");
    }
}
