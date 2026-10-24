using Capsule;
using Capsule.Package;

static partial class Check
{
    const string Manifest = "{\"schemaVersion\":1,\"apiVersion\":1,\"id\":\"example.features\",\"entry\":\"main.js\",\"assets\":[]}";
    static int passed;
    static byte[] Json(string value) => System.Text.Encoding.UTF8.GetBytes(value);
    static void True(bool value, string message) { if (!value) throw new Exception(message); passed++; }
    static void Main(string[] args) { if (args.Length == 2 && args[0] == "--metrics") { TransportMetrics(args[1]); return; } FeatureActivationCases(); SessionCases(); SegmentedWorkerCases(); Console.WriteLine("PASS native feature activation: " + passed + " checks"); }
    static void FeatureActivationCases()
    {
        const string name = "@example/inventory", item = "@example/items";
        string hash = new string('a', 64);
        string requirement = "{\"name\":\"" + name + "\",\"range\":\"1.2.*\",\"contractVersion\":\"1.2.5\",\"fingerprint\":\"" + hash + "\",\"optional\":false,\"permissions\":[\"inventory.read\"]}";
        string source = Manifest.Substring(0, Manifest.Length - 1) + ",\"features\":[" + requirement + "]}";
        True(UiPackageManifest.TryParse(Json(source), out var manifest, out var error), "manifest feature requirements: " + error);
        True(manifest.Features.Count == 1 && !manifest.Features[0].Optional, "required metadata retained");
        True(UiPackageManifest.TryParse(Json(source.Replace("false", "true")), out var optional, out _), "optional boolean accepted");
        foreach (var invalid in new[] { source.Replace("false", "null"), source.Replace("1.2.*", "^1.2"), source.Replace("inventory.read", "bad permission"), source.Replace(requirement, requirement + "," + requirement), source.Replace("\"permissions\":[", "\"extra\":1,\"permissions\":[") })
            True(!UiPackageManifest.TryParse(Json(invalid), out _, out _), "invalid feature metadata rejected");
        var registry = new UiFeatureRegistry();
        var grants = new[] { new UiFeatureGrant(name, "inventory.read") };
        bool Fails(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }
        True(Fails(() => registry.Select(manifest.Features, grants)), "missing required provider blocks");
        True(registry.Select(optional.Features, grants).Providers.Count == 0, "optional missing provider omitted");
        var inventory = registry.Register(new UiFeatureDefinition(name, "1.2.5", hash, new[] { new UiFeatureDependency(item, "1.0.*") }, new[] { "inventory.read" }), new object());
        True(Fails(() => registry.Select(manifest.Features, grants)), "transitive dependency required");
        var items = registry.Register(new UiFeatureDefinition(item, "1.0.1", hash, Array.Empty<UiFeatureDependency>(), new[] { "items.read" }), new object());
        True(Fails(() => registry.Select(manifest.Features, Array.Empty<UiFeatureGrant>())), "requirements do not grant permission");
        var selected = registry.Select(manifest.Features, grants);
        True(selected.IsCurrent && selected.Allows(name, "inventory.read") && !selected.Allows(item, "items.read"), "dependency grants are not transitive");
        var deniedHash = new UiFeatureRequirement(name, "1.2.*", "1.2.5", new string('b', 64), false, new[] { "inventory.read" });
        True(Fails(() => registry.Select(new[] { deniedHash }, grants)), "same version fingerprint mismatch");
        var addonRegistry = new UiAddonRegistry();
        var registration = addonRegistry.Register("example.features", UiAddonTrust.Community, new UiCapabilityGrant(null, null, null));
        var activation = addonRegistry.Activate(registration, selected);
        items.Dispose();
        True(!selected.IsCurrent && !selected.Allows(name, "inventory.read"), "dependency retirement revokes selection");
        True(Fails(() => activation.ConsumeOwner()), "provider change before guest construction rejected");
        items = registry.Register(new UiFeatureDefinition(item, "1.0.2", hash, Array.Empty<UiFeatureDependency>(), new[] { "items.read" }), new object());
        True(registry.Select(manifest.Features, grants).IsCurrent && !selected.IsCurrent, "fresh selection after compatible replacement");
        inventory.Dispose();
        True(Fails(() => registry.Register(new UiFeatureDefinition(name, "1.2.5", new string('b', 64), Array.Empty<UiFeatureDependency>(), Array.Empty<string>()), new object())), "identity collision persists across retirement");
        registry.Register(new UiFeatureDefinition(name, "2.0.0", hash, Array.Empty<UiFeatureDependency>(), Array.Empty<string>()), new object());
        True(Fails(() => registry.Select(manifest.Features, grants)), "incompatible major rejected");
        var cycle = new UiFeatureRegistry();
        cycle.Register(new UiFeatureDefinition(name, "1.2.5", hash, new[] { new UiFeatureDependency(item, "1.0.*") }, new[] { "inventory.read" }), new object());
        cycle.Register(new UiFeatureDefinition(item, "1.0.0", hash, new[] { new UiFeatureDependency(name, "1.2.*") }, Array.Empty<string>()), new object());
        True(Fails(() => cycle.Select(manifest.Features, grants)), "dependency cycle rejected");
    }
}
