using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Capsule;
using Capsule.Package;
using Capsule.Wasmtime;
using Capsule.Yoga;
using Newtonsoft.Json.Linq;

namespace Capsule.Host;

// Process-lifetime NativeAOT library; all exported session calls belong to one host thread.
public static unsafe class Exports
{
    static string createError;
    [UnmanagedCallersOnly(EntryPoint = "capsule_abi_version", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int AbiVersion() => 1;
    [UnmanagedCallersOnly(EntryPoint = "capsule_create", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static nint Create(byte* config, int length)
    {
        try
        {
            var value = JObject.Parse(Read(config, length, 8 * 1024 * 1024));
            var session = new DevelopmentSession((string)value["resources"], (bool?)value["demo"] == true, value);
            return GCHandle.ToIntPtr(GCHandle.Alloc(session));
        }
        catch (Exception error) { createError = error.Message; return 0; }
    }
    [UnmanagedCallersOnly(EntryPoint = "capsule_exchange", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static nint Exchange(nint handle, int command, byte* request, int length)
    {
        try
        {
            if (handle == 0) throw new InvalidOperationException(createError ?? "No Capsule host.");
            var host = (DevelopmentSession)GCHandle.FromIntPtr(handle).Target;
            return Marshal.StringToCoTaskMemUTF8(host.Exchange(command, Read(request, length, 8 * 1024 * 1024)).ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception error)
        {
            return Marshal.StringToCoTaskMemUTF8(new JObject { ["error"] = error.Message }.ToString(Newtonsoft.Json.Formatting.None));
        }
    }
    [UnmanagedCallersOnly(EntryPoint = "capsule_free", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void Free(nint bytes) { if (bytes != 0) Marshal.FreeCoTaskMem(bytes); }
    [UnmanagedCallersOnly(EntryPoint = "capsule_destroy", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void Destroy(nint handle)
    {
        if (handle == 0) return;
        var owner = GCHandle.FromIntPtr(handle);
        try { ((DevelopmentSession)owner.Target).Dispose(); }
        catch { /* No managed exception may cross the native boundary. */ }
        finally { owner.Free(); }
    }
    static string Read(byte* bytes, int length, int max)
    {
        if (length < 0 || length > max || (length != 0 && bytes == null)) throw new ArgumentException("Native request quota.");
        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(bytes, length));
    }
}

public sealed class DevelopmentSession : IDisposable
{
    readonly UiAsyncHostSession session;
    readonly byte[] carrier, sdk;
    readonly bool demo;
    readonly FeatureProviders providers;
    readonly UiThemeLimits themeLimits;
    readonly int nodeLimit, hostCalls;
    readonly bool firstParty;
    readonly string expectedAddon;
    UiFeatureSession candidateFeatures, activeFeatures;
    UiAsyncCompletion pending;
    UiThemeSnapshot candidatePolicy, activePolicy;
    JObject candidateTheme, activeTheme;
    UiSnapshot lastSnapshot;
    uint candidateOwner, activeOwner;
    string candidateHash, failedHash, activeHash, error;
    int requests;
    public DevelopmentSession(string resources, bool demo, JObject config = null)
    {
        this.demo = demo;
        carrier = File.ReadAllBytes(Path.Combine(resources, "CapsuleQuickJs.bytes"));
        sdk = File.ReadAllBytes(Path.Combine(resources, "CapsuleReactSdk.txt"));
        config ??= new JObject();
        providers = new FeatureProviders(config["providers"] as JArray);
        firstParty = (bool?)config["firstParty"] == true;
        expectedAddon = (string)config["addonId"];
        nodeLimit = firstParty ? UiSession.ExtendedLayoutNodes : UiSession.DefaultLayoutNodes;
        hostCalls = firstParty ? UiHostBridgeContract.ExtendedCallsPerInvocation : UiHostBridgeContract.MaxCallsPerInvocation;
        themeLimits = firstParty ? new UiThemeLimits(4096, 256, 4096) : UiThemeLimits.Default;
        session = new UiAsyncHostSession();
    }
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    static bool Hex(string value) => value != null && value.Length == 64 && Array.TrueForAll(value.ToCharArray(), Uri.IsHexDigit);
    JObject Status() => new() { ["error"] = error, ["revision"] = activeHash, ["generation"] = session.ActiveGeneration.ToString(), ["requests"] = requests,
        ["featureRequests"] = providers.DrainRequests(), ["cancelledRequests"] = providers.DrainCancelled() };
    public JObject Exchange(int command, string request)
    {
        switch (command)
        {
            case 0: return Status();
            case 1: Load(JObject.Parse(request)); return Status();
            case 2: return Poll();
            case 3: Acknowledge(JObject.Parse(request)); return Status();
            case 4:
                var input = JObject.Parse(request);
                bool accepted = session.EnqueueEvent(long.Parse((string)input["generation"]), ulong.Parse((string)input["handle"]),
                    UiEventMask.Activate, Array.Empty<byte>());
                return new JObject { ["accepted"] = accepted };
            case 5:
                var viewport = JObject.Parse(request);
                return new JObject { ["accepted"] = session.EnqueueViewport(session.ActiveGeneration, (int)viewport["width"], (int)viewport["height"]) };
            case 6: providers.Publish(JObject.Parse(request)); return new JObject { ["accepted"] = true };
            case 7: return new JObject { ["accepted"] = providers.Resolve(JObject.Parse(request)) };
            default: throw new ArgumentOutOfRangeException(nameof(command));
        }
    }
    void Load(JObject value)
    {
        if ((int?)value["schemaVersion"] != 1 || (int?)value["apiVersion"] != 1 ||
            value["requires"] is not JObject requires || requires.Count != 0 ||
            value["features"] is not JArray features)
            throw new ArgumentException("Unsupported development package contract or required features.");
        if ((bool?)value["compiler"]?["success"] == false)
        { error = (string)value["compiler"]?["error"] ?? "Compilation failed."; return; }
        string hash = (string)value["hash"], sdkHash = (string)value["sdkSha256"];
        string source = (string)value["entryBundle"] ?? (string)value["bundle"], suppliedSdk = (string)value["sdk"], addon = (string)value["addonId"];
        if (expectedAddon != null && addon != expectedAddon) throw new ArgumentException("Addon identity does not match host configuration.");
        var modules = new List<byte[]>();
        foreach (string module in value["scriptModules"] as JArray ?? new JArray()) modules.Add(Encoding.UTF8.GetBytes(module));
        if (!Hex(hash) || !Hex(sdkHash) || string.IsNullOrEmpty(addon) || addon.Length > 96 || source == null || suppliedSdk == null ||
            Encoding.UTF8.GetByteCount(source) > 65536 || Encoding.UTF8.GetByteCount(suppliedSdk) > 524288 ||
            !string.Equals(Hash(Encoding.UTF8.GetBytes(suppliedSdk)), sdkHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Hash(sdk), sdkHash, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Development identity, source quota or SDK compatibility failed.");
        if (hash == activeHash || hash == candidateHash || hash == failedHash) return;
        if (pending != null || candidateHash != null) return;
        var themeJson = value["theme"] as JObject ?? throw new ArgumentException("Theme missing.");
        var theme = new UiControlTheme(themeJson.ToString(), themeLimits);
        var styles = new List<string>(theme.StyleKeys);
        var assets = new List<string>();
        foreach (var asset in theme.CopyAssets()) assets.Add(asset.Key);
        var policy = new UiThemeSnapshot(styles, assets);
        var raw = value["metrics"] as JObject ?? throw new ArgumentException("Metrics missing.");
        var advances = new Dictionary<int, float>();
        foreach (var glyph in raw["advances"] as JArray ?? throw new ArgumentException("Glyphs missing.")) advances.Add((int)glyph["code"], (float)glyph["value"]);
        var pairs = new Dictionary<ulong, float>();
        foreach (var pair in raw["pairs"] as JArray ?? new JArray()) pairs.Add(YogaFontMetrics.PairKey((int)pair["first"], (int)pair["second"]), (float)pair["value"]);
        var fonts = new Dictionary<string, YogaFontMetrics>(StringComparer.Ordinal);
        foreach (var key in theme.TextKeys) fonts.Add(key, new YogaFontMetrics(advances, pairs, theme.TextNumber(key, "lineHeight")));
        var metrics = new YogaTextCatalog(fonts);
        var registry = new UiAddonRegistry();
        var grant = demo ? new UiCapabilityGrant(new[] { new UiObservationCapability(7, 1) }, new ushort[] { 7 }, null) : new UiCapabilityGrant(null, null, null);
        var requirements = new List<UiFeatureRequirement>();
        foreach (JObject requirement in features) requirements.Add(new((string)requirement["name"], (string)requirement["range"],
            (string)requirement["contractVersion"], (string)requirement["fingerprint"], (bool?)requirement["optional"] == true,
            ((JArray)requirement["permissions"] ?? new JArray()).Values<string>()));
        var selection = providers.Registry.Select(requirements, providers.Grants);
        var registration = registry.Register(addon, firstParty ? UiAddonTrust.FirstParty : UiAddonTrust.Community, grant);
        var activation = registry.Activate(registration, selection);
        var featureSession = new UiFeatureSession(selection, activation.OwnerNonce);
        var initial = new Queue<(ushort, UiObservationPayload)>();
        featureSession.PumpSegments((topic, payload) => { initial.Enqueue((topic, payload)); return true; });
        try
        {
            session.RequestLoad((output, observations, actions) => new YogaUiGuest(activation.OwnerNonce, policy, metrics, output,
                layout => new WasmtimeUiGuest(carrier, sdk, Encoding.UTF8.GetBytes(source), activation, policy, layout, new InitialObservations(initial, observations), actions,
                    scheduling: true, hostCallsPerInvocation: hostCalls, layoutNodeLimit: nodeLimit, scriptModules: modules.ToArray()), layoutNodeLimit: nodeLimit));
        }
        catch { featureSession.Dispose(); throw; }
        candidateFeatures?.Dispose(); candidateFeatures = featureSession;
        candidateHash = hash; candidatePolicy = policy; candidateTheme = (JObject)themeJson.DeepClone(); candidateOwner = activation.OwnerNonce; error = null;
    }
    JObject Poll()
    {
        activeFeatures?.PumpSegments((topic, bytes) => session.EnqueueSegmentedObservation(session.ActiveGeneration, topic, 1, bytes, replaceable: false));
        if (pending == null && session.TryGetCompletion(out var completion))
        {
            pending = completion;
            if (completion.Error != null) { Acknowledge(new JObject { ["sequence"] = completion.Sequence.ToString(), ["accepted"] = false }); return Status(); }
        }
        var result = Status();
        if (pending == null) return result;
        var snapshot = pending.Snapshot;
        if (snapshot == null && !pending.Candidate)
        {
            // Input/action completions need acknowledgment even when React does not render.
            Acknowledge(new JObject { ["sequence"] = pending.Sequence.ToString(), ["accepted"] = true });
            return Status();
        }
        uint owner = pending.Candidate ? candidateOwner : activeOwner;
        var policy = pending.Candidate ? candidatePolicy : activePolicy;
        if (snapshot == null || !UiSnapshotValidator.Validate(snapshot, owner, policy, out var validationError))
        {
            error = "Snapshot rejected."; Acknowledge(new JObject { ["sequence"] = pending.Sequence.ToString(), ["accepted"] = false }); return Status();
        }
        var nodes = new JArray();
        foreach (var node in snapshot.Nodes)
        {
            if (node.WorldAnchor != null || node.Effect != null ||
                node.Kind != UiNodeKind.Box && node.Kind != UiNodeKind.ClipBox && node.Kind != UiNodeKind.Text && node.Kind != UiNodeKind.Button && node.Kind != UiNodeKind.Image)
            { error = "Unsupported Godot node: " + node.Kind; Acknowledge(new JObject { ["sequence"] = pending.Sequence.ToString(), ["accepted"] = false }); return Status(); }
            nodes.Add(new JObject { ["handle"] = node.Handle.ToString(), ["parent"] = node.Parent.ToString(), ["kind"] = node.Kind.ToString(),
                ["x"] = node.Rect.X / 64f, ["y"] = node.Rect.Y / 64f, ["width"] = node.Rect.Width / 64f, ["height"] = node.Rect.Height / 64f,
                ["style"] = node.StyleKey, ["asset"] = node.AssetKey, ["text"] = node.Text, ["visible"] = node.Visible, ["events"] = (int)node.Events, ["order"] = node.Order });
        }
        result["sequence"] = pending.Sequence.ToString(); result["generation"] = pending.Generation.ToString();
        result["candidate"] = pending.Candidate; result["revision"] = pending.Candidate ? candidateHash : activeHash;
        result["theme"] = pending.Candidate ? candidateTheme : activeTheme; result["nodes"] = nodes;
        return result;
    }
    void Acknowledge(JObject value)
    {
        if (pending == null || (string)value["sequence"] != pending.Sequence.ToString()) throw new ArgumentException("Stale native presentation acknowledgment.");
        var completing = pending;
        bool accepted = (bool?)value["accepted"] == true;
        var sourceSnapshot = lastSnapshot;
        var outcome = session.TryApply(completing, (UiAsyncCompletion item, out string reason) =>
        { reason = accepted ? null : error ?? "Godot presentation rejected."; return accepted; }, item =>
        {
            if (item.Candidate)
            {
                activeHash = candidateHash; activePolicy = candidatePolicy; activeTheme = candidateTheme; activeOwner = candidateOwner;
                activeFeatures?.Dispose(); activeFeatures = candidateFeatures; candidateFeatures = null;
                candidateHash = null;
            }
            if (item.Snapshot != null) lastSnapshot = item.Snapshot;
        }, out var failure);
        pending = null;
        if (outcome != UiAsyncApplyResult.Accepted)
        { error = completing.Error ?? failure; if (completing.Candidate) { failedHash = candidateHash; candidateHash = null; candidateFeatures?.Dispose(); candidateFeatures = null; } return; }
        error = null;
        if (demo && completing.Candidate) ObserveDemo();
        foreach (var action in completing.Actions)
        {
            if (action.Capability == UiFeatureSession.Capability && activeFeatures != null) { activeFeatures.Dispatch(action.CopyPayload()); continue; }
            if (action.Capability == UiFeatureSession.RecoveryCapability && activeFeatures != null) { activeFeatures.Recover(action.CopyPayload()); continue; }
            bool valid = false;
            foreach (var node in (sourceSnapshot ?? lastSnapshot).Nodes)
                if (node.Handle == action.Handle && node.Kind == UiNodeKind.Button && node.Visible && (node.Events & UiEventMask.Activate) != 0) valid = true;
            if (!demo || !valid || (uint)(action.Handle >> 32) != activeOwner || action.Capability != 7 || action.CopyPayload().Length != 2)
            { error = "Unregistered action or invalid source."; continue; }
            requests = Math.Min(ushort.MaxValue, requests + 1); ObserveDemo();
        }
    }
    void ObserveDemo() => session.EnqueueObservation(session.ActiveGeneration, 7, 1, new[] { (byte)requests, (byte)(requests >> 8) });
    public void Dispose() { candidateFeatures?.Dispose(); activeFeatures?.Dispose(); providers.Dispose(); session.Dispose(); }
    sealed class InitialObservations : IUiSegmentedObservationAdapter
    {
        readonly Queue<(ushort Topic, UiObservationPayload Payload)> initial;
        readonly IUiObservationAdapter inner;
        public InitialObservations(Queue<(ushort, UiObservationPayload)> initial, IUiObservationAdapter inner) { this.initial = initial; this.inner = inner; }
        public bool TryDequeueSegments(out ushort topic, out ushort schema, out UiObservationPayload payload)
        {
            if (initial.Count != 0) { var item = initial.Dequeue(); topic = item.Topic; schema = 1; payload = item.Payload; return true; }
            if (inner is IUiSegmentedObservationAdapter segmented) return segmented.TryDequeueSegments(out topic, out schema, out payload);
            bool result = inner.TryDequeue(out topic, out schema, out var bytes); payload = result ? UiObservationPayload.Owned(bytes) : null; return result;
        }
        public bool TryDequeue(out ushort topic, out ushort schema, out byte[] bytes) { bool result = TryDequeueSegments(out topic, out schema, out var payload); bytes = result ? payload.AsContiguous() : null; return result; }
    }
}
