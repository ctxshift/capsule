using Capsule;
using Capsule.Package;
using Capsule.Wasmtime;

static class FeatureRecoveryProof
{
    sealed class Provider : UiFeatureProvider
    {
        public Provider() : base(new UiFeatureDefinition("@example/recovery", "1.0.0", new string('c', 64), Array.Empty<UiFeatureDependency>(), new[] { "read" }))
        { AddState("State", "read", new byte[] { 10, 0, 0, 0 }, UiFeatureDelivery.Ordered); }
        public void Change(byte value) => Publish("State", new byte[] { value, 0, 0, 0 });
    }
    sealed class Host : IUiObservationAdapter, IUiActionAdapter, IUiCommitTarget, IUiTheme
    {
        public UiFeatureSession Session;
        public int Reads;
        public readonly Queue<(ushort topic, byte[] bytes)> Queue = new();
        public bool TryDequeue(out ushort topic, out ushort schema, out byte[] payload)
        { schema = 1; if (Queue.TryDequeue(out var value)) { topic = value.topic; payload = value.bytes; return true; } topic = 0; payload = null; return false; }
        public bool TryRequest(ushort capability, ulong handle, byte[] payload)
        { if (capability != UiFeatureSession.RecoveryCapability || handle != 0) return false; Reads++; return Session.Recover(payload); }
        public bool TryCommit(UiSnapshot snapshot, out string error) { error = null; return true; }
        public bool AllowsAsset(string key) => false;
        public bool AllowsStyle(string key) => false;
        public void Pump(bool dropThird = false) => Session.Pump((topic, bytes) => {
            if (!dropThird || topic != UiFeatureSession.StateTopic || UiFeatureSession.Read32(bytes, 8) != 3) Queue.Enqueue((topic, bytes));
            return true;
        });
    }
    public static void Run(string carrier, string sdk, string bundle)
    {
        using var provider = new Provider();
        var registry = new UiFeatureRegistry();
        using var registration = registry.Register(provider.Definition, provider);
        var requirement = new UiFeatureRequirement(provider.Definition.Name, "1.0.*", "1.0.0", provider.Definition.Fingerprint, false, new[] { "read" });
        var selection = registry.Select(new[] { requirement }, new[] { new UiFeatureGrant(provider.Definition.Name, "read") });
        var addons = new UiAddonRegistry();
        var addon = addons.Register("example.recovery", UiAddonTrust.Community, new UiCapabilityGrant(null, null, null));
        using var session = new UiFeatureSession(selection, 17);
        var host = new Host { Session = session };
        using var guest = new WasmtimeUiGuest(File.ReadAllBytes(carrier), File.ReadAllBytes(sdk), File.ReadAllBytes(bundle), addons.Activate(addon, selection), host, host, host, host);
        void Check(bool result, string message) { if (!result) throw new Exception(message); }
        Check(guest.Initialize(out var error), "Initialize: " + error);
        host.Pump(); Check(guest.Update(out error), "Initial snapshot and explicit recovery: " + error);
        Check(host.Reads == 1, "Explicit read reaches admitted native provider without a UI event");
        host.Pump(); Check(guest.Update(out error), "Recovery reset: " + error);
        provider.Change(20); provider.Change(30); host.Pump(dropThird: true);
        Check(guest.Update(out error), "Gap invalidation and recovery: " + error);
        Check(host.Reads == 2, "Gap requests exactly one recovery");
        host.Pump(); Check(guest.Update(out error), "Recovered snapshot and late subscriber: " + error);
        registration.Dispose(); host.Pump(); Check(guest.Update(out error), "Provider retirement: " + error);
        Console.WriteLine("PASS real-Wasm feature recovery: admission, read-only request, gap invalidation, reset, late subscriber, retirement");
    }
}
