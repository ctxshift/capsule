using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Capsule;
using Capsule.Wasmtime;
using Capsule.Yoga;

static class Program
{
    static int checks;
    static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); checks++; }
    sealed class Theme : IUiTheme
    {
        public bool AllowsStyle(string key) => false;
        public bool AllowsAsset(string key) => false;
    }
    sealed class Target : IUiCommitTarget
    {
        public bool TryCommit(UiSnapshot snapshot, out string error) { error = null; return true; }
    }
    sealed class Measurer : IYogaMeasurer
    {
        public int Calls;
        public void Measure(int node, float width, int widthMode, float height, int heightMode,
            out float measuredWidth, out float measuredHeight)
        { Calls++; measuredWidth = 42; measuredHeight = 18; }
    }
    static void Dependencies(Assembly assembly)
    {
        Check(assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName
            == ".NETStandard,Version=v2.1", "Runtime target framework.");
        foreach (var reference in assembly.GetReferencedAssemblies())
            Check(reference.Name is "netstandard" or "Capsule.Core", "Unexpected dependency: " + reference);
    }
    static void Main(string[] args)
    {
        bool imports = args.Length > 0 && args[0] == "--imports";
        bool layoutCapacity = args.Length > 0 && args[0] == "--layout-capacity";
        bool recovery = args.Length > 0 && args[0] == "--feature-recovery";
        bool textInput = args.Length > 0 && args[0] == "--text-input";
        bool worldPresentation = args.Length > 0 && args[0] == "--world-presentation";
        bool observations = args.Length > 0 && args[0] == "--whole-observations";
        bool carrierCache = args.Length > 0 && args[0] == "--carrier-cache";
        if (imports || textInput || worldPresentation || observations || recovery || carrierCache || layoutCapacity) args = args.Skip(1).ToArray();
        if (textInput || worldPresentation || layoutCapacity ? args.Length != 5 : args.Length != 4 && args.Length != 6) throw new ArgumentException("Provide absolute paths: wasmtime.dll bridge.dll yoga.dll carrier.bytes [sdk.js bundle.js]; --text-input and --layout-capacity need sdk.js only");
        foreach (string path in args)
            Check(Path.IsPathFullyQualified(path) && File.Exists(path), "Explicit existing resource path required: " + path);
        Dependencies(typeof(WasmtimeUiGuest).Assembly);
        Dependencies(typeof(NativeYoga).Assembly);
        NativeLibrary.Load(args[0]);
        if (carrierCache) { CarrierCacheProof.Run(args[1]); return; }
        NativeLibrary.SetDllImportResolver(typeof(WasmtimeUiGuest).Assembly,
            (name, _, _) => name == "portable_ui_wasmtime" ? NativeLibrary.Load(args[1]) : IntPtr.Zero);
        NativeLibrary.SetDllImportResolver(typeof(NativeYoga).Assembly,
            (name, _, _) => name == "portable_ui_yoga" ? NativeLibrary.Load(args[2]) : IntPtr.Zero);
        if (layoutCapacity) { LayoutCapacityProof.Run(args[3], args[4]); return; }
        if (imports) { ImportBoundaryProof.Run(args[3], args[1]); return; }
        if (recovery) { if (args.Length != 6) throw new ArgumentException("Recovery requires SDK and fixture bundle"); FeatureRecoveryProof.Run(args[3], args[4], args[5]); return; }
        if (observations) { WholeObservationProof.Run(args[3]); return; }
        if (worldPresentation) { WorldPresentationProof.Run(args[3], args[4]); return; }
        if (textInput) { TextInputProof.Run(args[3], args[4]); return; }
        if (args.Length == 6)
        {
            LifecycleProof.Run(args[3], args[4], args[5]);
            foreach (string path in args)
                Console.WriteLine($"SHA256 {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))} {path}");
            return;
        }
        var registry = new UiAddonRegistry();
        var registration = registry.Register("example.native-smoke", UiAddonTrust.Community,
            new UiCapabilityGrant(Array.Empty<UiObservationCapability>(), Array.Empty<ushort>(), null));
        ulong fuel;
        using (var guest = new WasmtimeUiGuest(File.ReadAllBytes(args[3]), Array.Empty<byte>(),
            Encoding.UTF8.GetBytes("globalThis.portableUiUpdate = () => {};"), registry.Activate(registration),
            new Theme(), new Target(), null, null))
        {
            Check(guest.Initialize(out var error), "Actual guest initializes: " + error);
            Check(guest.Update(out error), "Actual guest updates: " + error);
            fuel = guest.TotalCallFuel;
            Check(guest.HasCallFuel && fuel > 0, "Guest consumed measured call fuel.");
            Check(WasmtimeUiGuest.AddonExecutionFuel == 200000000, "Execution budget unchanged.");
        }
        Check(NativeYoga.LiveRoots == 0, "No initial Yoga roots.");
        using (var yoga = new NativeYoga())
        {
            var styles = new[] { YogaStyle.Box(-1, 200, 100), YogaStyle.Box(0, 50, 20), YogaStyle.Box(0, 60, 20) };
            styles[0].Row = 1; styles[0].Gap = 10;
            Check(yoga.TryLayout(styles, out var rects, out _, out _, out var error), "Native layout: " + error);
            Check(rects[1].X == 0 && rects[2].X == 60 && rects[2].Width == 60, "Native row geometry.");
            var measure = new Measurer();
            var measured = new[] { YogaStyle.Box(-1, 200, 100), YogaStyle.Box(0) };
            measured[0].Align = 1; measured[1].IntrinsicWidth = 42; measured[1].IntrinsicHeight = 18;
            Check(yoga.TryLayout(measured, out rects, out _, out _, out error, measure), "Native measurement: " + error);
            Check(measure.Calls > 0 && rects[1].Height == 18, "Managed callback invoked by native Yoga.");
            Check(!yoga.TryLayout(new YogaStyle[129], out _, out _, out _, out _), "Existing node quota retained.");
        }
        Check(NativeYoga.LiveRoots == 0, "Native root disposal.");
        foreach (string path in args)
            Console.WriteLine($"SHA256 {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))} {path}");
        Console.WriteLine($"Capsule neutral native checks passed: {checks}; guest total call fuel: {fuel}");
    }
}
