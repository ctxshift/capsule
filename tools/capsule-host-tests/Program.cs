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
Console.WriteLine($"PASS native provider boundary: {checks} checks");
