using Capsule;
using Capsule.Host;
using Capsule.Package;
using Newtonsoft.Json.Linq;

int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
void Refuses(Action action, string label) { try { action(); } catch (Exception) { checks++; return; } throw new Exception(label); }
var definition = JObject.Parse("""
{"name":"@capsule-example/energy","version":"1.0.0","dependencies":{},"states":{"State":{"permission":"energy.read","delivery":"latest","initial":"AAAAAA=="}},"requests":{"Add":{"permission":"energy.add"}},"grants":["energy.read","energy.add"]}
""");
definition["fingerprint"] = new string('a', 64);
UiFeatureRequirement Requirement(string fingerprint) => new("@capsule-example/energy", "1.0.*", "1.0.0", fingerprint, false, new[] { "energy.read", "energy.add" });
using var providers = new FeatureProviders(new JArray(definition));
var invalid = (JObject)definition.DeepClone();
invalid["states"]!["State"]!["delivery"] = "unknown";
Refuses(() => new FeatureProviders(new JArray(invalid)), "invalid delivery refused");
var dependent = (JObject)definition.DeepClone();
dependent["dependencies"] = new JObject { ["@capsule-example/missing"] = "1.0.*" };
using (var missing = new FeatureProviders(new JArray(dependent)))
    Refuses(() => missing.Registry.Select(new[] { Requirement(new string('a', 64)) }, missing.Grants), "missing dependency refused");
var selection = providers.Registry.Select(new[] { Requirement(new string('a', 64)) }, providers.Grants);
Refuses(() => providers.Registry.Select(new[] { Requirement(new string('b', 64)) }, providers.Grants), "fingerprint must match");
Refuses(() => providers.Registry.Select(new[] { Requirement(new string('a', 64)) }, Array.Empty<UiFeatureGrant>()), "explicit grants required");
using var session = new UiFeatureSession(selection, 7);
var frames = new List<(ushort Topic, byte[] Bytes)>();
void Pump() => session.Pump((topic, bytes) => { frames.Add((topic, bytes)); return true; });
Pump();
var metadata = JObject.Parse(System.Text.Encoding.UTF8.GetString(frames.Single(x => x.Topic == UiFeatureSession.MetadataTopic).Bytes));
Check((uint)metadata["epoch"]! == 7, "metadata epoch");
Check((string)metadata["features"]![0]!["name"]! == "@capsule-example/energy", "explicit token metadata identity");
Check((int)metadata["features"]![0]!["states"]!["State"]! == 1, "state member ID");
frames.Clear();
session.Dispatch(session.FrameRequest("@capsule-example/energy", "Add", 1, new byte[] { 1, 0, 0, 0 }));
var request = (JObject)providers.DrainRequests().Single();
Check((string)request["operation"]! == "Add", "real operation forwarded");
Check(Convert.FromBase64String((string)request["payload"]!).SequenceEqual(new byte[] { 1, 0, 0, 0 }), "request copied exactly");
Check(providers.DrainRequests().Count == 0, "request delivered once");
var token = (string)request["token"]!;
Refuses(() => providers.Resolve(new JObject { ["token"] = token, ["payload"] = "invalid-base64" }), "invalid reply rejected before consuming token");
providers.Publish(new JObject { ["feature"] = "@capsule-example/energy", ["member"] = "State", ["payload"] = "DAAAAA==" });
Check(providers.Resolve(new JObject { ["token"] = token, ["payload"] = "AQ==" }), "provider result accepted");
Check(!providers.Resolve(new JObject { ["token"] = token, ["payload"] = "AQ==" }), "duplicate reply rejected");
Pump();
Check(frames.Any(x => x.Topic == UiFeatureSession.StateTopic && x.Bytes[8] == 12), "published state reaches transport");
Check(frames.Any(x => x.Topic == UiFeatureSession.ResultTopic && x.Bytes[8] == 0 && x.Bytes[9] == 1), "resolved result reaches transport");
frames.Clear();
session.Dispatch(session.FrameRequest("@capsule-example/energy", "Add", 3, new byte[] { 1, 0, 0, 0 }));
var rejected = (string)providers.DrainRequests().Single()["token"]!;
Check(providers.Resolve(new JObject { ["token"] = rejected, ["rejected"] = true }), "host can reject request");
Pump();
Check(frames.Any(x => x.Topic == UiFeatureSession.ResultTopic && x.Bytes[8] != 0), "rejection reaches transport");
session.Dispatch(session.FrameRequest("@capsule-example/energy", "Add", 4, new byte[] { 1, 0, 0, 0 }));
var cancelled = (string)providers.DrainRequests().Single()["token"]!;
session.Dispose();
Check(providers.DrainCancelled().Values<string>().SequenceEqual(new[] { cancelled }), "delivered request cancellation forwarded");
Check(!providers.Resolve(new JObject { ["token"] = cancelled, ["payload"] = "AQ==" }), "retired reply cannot revive a generation");
using var queued = new UiFeatureSession(selection, 8);
queued.Dispatch(queued.FrameRequest("@capsule-example/energy", "Add", 1, new byte[] { 1, 0, 0, 0 }));
queued.Dispose();
Check(providers.DrainRequests().Count == 0, "cancelled queued request never reaches game logic");
string temporary = Path.Combine(Path.GetTempPath(), "capsule-runtime-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
string artifactPath = Path.Combine(temporary, "CapsuleQuickJs.cwasm");
string descriptorPath = Path.Combine(temporary, "CapsuleQuickJs.precompiled.json");
try
{
    byte[] source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "CapsuleQuickJs.bytes"));
    Check(CompiledCarrier.Read(temporary, source) == null, "legacy source carrier remains supported");
    byte[] artifact = new byte[] { 1, 2, 3, 4 };
    File.WriteAllBytes(artifactPath, artifact);
    Refuses(() => CompiledCarrier.Read(temporary, source), "compiled carrier needs its descriptor");
    var descriptor = new JObject { ["schemaVersion"] = 1, ["target"] = "x86_64-pc-windows-msvc", ["runtimeVersion"] = "48.0.2",
        ["runtimeSha256"] = "19b3880fbb92f1826c58ac9f5e2a472692b5fc3d012a1a7a41983ccf77544607",
        ["sourceSha256"] = Capsule.Wasmtime.WasmtimeUiGuest.QuickJsSha256,
        ["compiledSha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(artifact)) };
    void Save() => File.WriteAllText(descriptorPath, descriptor.ToString());
    Save();
    Check(CompiledCarrier.Read(temporary, source).SequenceEqual(artifact), "verified engine artifact selected");
    File.WriteAllBytes(artifactPath, new byte[] { 1, 2, 3, 5 });
    Refuses(() => CompiledCarrier.Read(temporary, source), "corrupted compiled artifact refused");
    File.WriteAllBytes(artifactPath, artifact);
    descriptor["target"] = "different-platform"; Save();
    Refuses(() => CompiledCarrier.Read(temporary, source), "wrong compiled target refused");
    descriptor["target"] = "x86_64-pc-windows-msvc"; descriptor["runtimeVersion"] = "different-runtime"; Save();
    Refuses(() => CompiledCarrier.Read(temporary, source), "wrong compiled runtime refused");
    descriptor["runtimeVersion"] = "48.0.2"; Save();
    Refuses(() => CompiledCarrier.Read(temporary, new byte[] { 0 }), "wrong source identity refused");
    using (var oversized = File.OpenWrite(artifactPath)) oversized.SetLength(16777217);
    Refuses(() => CompiledCarrier.Read(temporary, source), "compiled artifact size bounded before reading");
}
finally { File.Delete(artifactPath); File.Delete(descriptorPath); Directory.Delete(temporary); }
Console.WriteLine($"PASS native provider/runtime boundary: {checks} checks");
