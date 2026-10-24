using Capsule;
using Capsule.Package;

static partial class Check
{
    sealed class Provider : UiFeatureProvider
    {
        static readonly UiFeatureDefinition Contract = new("@example/counter", "1.0.0", new string('c', 64), Array.Empty<UiFeatureDependency>(), new[] { "read", "write", "secret" });
        public int Calls;
        public byte[] Received;
        public TaskCompletionSource<byte[]> Completion = new();
        public Provider(bool ordered = false) : base(Contract)
        {
            AddState("First", "read", new byte[] { 1 }, ordered ? UiFeatureDelivery.Ordered : UiFeatureDelivery.Latest); AddState("Second", "read", new byte[] { 2 });
            AddState("Secret", "secret", new byte[] { 99 });
            AddRequest("Update", "write", (bytes, token) => { Calls++; Received = bytes; return Completion.Task; });
        }
        public void Change(byte value) => Publish("First", new[] { value });
    }
    static void SessionCases()
    {
        OrderedSessionCases();
        using var provider = new Provider();
        var registry = new UiFeatureRegistry();
        using var registration = registry.Register(provider.Definition, provider);
        var requirement = new UiFeatureRequirement(provider.Definition.Name, "1.0.*", "1.0.0", provider.Definition.Fingerprint, false, new[] { "read", "write" });
        var selection = registry.Select(new[] { requirement }, new[] { new UiFeatureGrant(provider.Definition.Name, "read", "write") });
        using var session = new UiFeatureSession(selection, 17);
        var messages = new List<(ushort topic, byte[] bytes)>();
        bool Publish(ushort topic, byte[] bytes) { messages.Add((topic, bytes)); return true; }
        session.Pump((_, _) => false);
        provider.Change(3); provider.Change(4);
        session.Pump(Publish);
        True(messages.Count == 3 && messages[0].topic == UiFeatureSession.MetadataTopic, "metadata precedes both distinct state members after backpressure");
        True(messages.Skip(1).Select(m => m.bytes[8]).Order().SequenceEqual(new byte[] { 2, 4 }), "latest state per member retained; secret omitted");
        string metadata = System.Text.Encoding.UTF8.GetString(messages[0].bytes);
        True(!metadata.Contains("Secret") && metadata.Contains("Update"), "metadata exposes only granted operations");
        provider.Change(10);
        byte[] rejected = null;
        session.Pump((_, bytes) => { rejected = bytes; return false; });
        session.Pump((_, bytes) => { True(ReferenceEquals(rejected, bytes), "state retry reuses immutable frame"); return false; });
        provider.Change(11);
        byte[] accepted = null;
        session.Pump((_, bytes) => { accepted = bytes; return true; });
        True(!ReferenceEquals(rejected, accepted) && rejected[8] == 10 && accepted[8] == 11, "new coalesced state replaces rejected frame without mutating it");
        provider.Change(12); session.Pump(Publish);
        True(accepted[8] == 11, "accepted frame remains immutable after next publication");
        var addons = new UiAddonRegistry();
        var addon = addons.Register("example", UiAddonTrust.Community, new UiCapabilityGrant(null, null, null));
        var activation = addons.Activate(addon, selection);
        True(activation.Grant.AllowsAction(UiFeatureSession.Capability) && activation.Grant.AllowsObservation(UiFeatureSession.StateTopic, 1), "transport grant derived after admission");
        var request = session.FrameRequest(provider.Definition.Name, "Update", 1, Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray());
        var stale = (byte[])request.Clone(); stale[0] = 16; session.Dispatch(stale);
        True(provider.Calls == 0, "stale activation cannot dispatch");
        session.Dispatch(request); session.Dispatch(request);
        True(provider.Calls == 1, "duplicate calls never repeat side effects");
        True(provider.Received.SequenceEqual(request.Skip(12)), "Variable-length request reaches provider intact");
        provider.Completion.SetResult(new byte[] { 8 }); messages.Clear();
        byte[] resultRejected = null;
        session.Pump((_, bytes) => { resultRejected = bytes; return false; });
        session.Pump((topic, bytes) => { True(ReferenceEquals(resultRejected, bytes), "result retry reuses immutable frame"); return Publish(topic, bytes); });
        True(messages.Count == 1 && messages[0].topic == UiFeatureSession.ResultTopic && messages[0].bytes[9] == 8, "asynchronous typed result framed once");
        var denied = session.FrameRequest(provider.Definition.Name, "Update", 2, Array.Empty<byte>()); denied[8] = 99;
        session.Dispatch(denied); messages.Clear(); session.Pump(Publish);
        True(provider.Calls == 1 && messages.Single().bytes[8] == 2, "unknown member denied before provider");
        session.Dispatch(session.FrameRequest(provider.Definition.Name, "Update", 3, Array.Empty<byte>()));
        session.Pump((_, _) => false); // Retain a completed result while the queue is full.
        provider.Change(13); session.Pump((_, _) => false); // Retain state too.
        registration.Dispose(); provider.Change(5); messages.Clear(); session.Pump(Publish);
        True(messages.Count == 1 && System.Text.Encoding.UTF8.GetString(messages[0].bytes).Contains("\"features\":[]"), "retirement clears pending state and publishes unavailable");
        session.Dispatch(session.FrameRequest(provider.Definition.Name, "Update", 4, Array.Empty<byte>()));
        True(provider.Calls == 2, "retirement fences dispatch and discards retained frames");
    }
    static void OrderedSessionCases()
    {
        using var provider = new Provider(true);
        var registry = new UiFeatureRegistry();
        using var registration = registry.Register(provider.Definition, provider);
        var requirement = new UiFeatureRequirement(provider.Definition.Name, "1.0.*", "1.0.0", provider.Definition.Fingerprint, false, new[] { "read", "write" });
        var selection = registry.Select(new[] { requirement }, new[] { new UiFeatureGrant(provider.Definition.Name, "read", "write") });
        using var session = new UiFeatureSession(selection, 21, orderedCapacity: 3);
        provider.Change(3); provider.Change(4);
        UiObservationPayload rejected = null;
        string metadata = null;
        session.PumpSegments((topic, bytes) => {
            if (topic == UiFeatureSession.MetadataTopic) { metadata = System.Text.Encoding.UTF8.GetString(bytes.AsContiguous()); return true; }
            rejected = bytes; return false;
        });
        True(metadata.Contains("\"First\":\"ordered\"") && metadata.Contains("\"Second\":\"latest\""), "delivery metadata distinguishes members");
        var values = new List<byte>();
        session.PumpSegments((_, bytes) => { if (values.Count == 0) True(ReferenceEquals(rejected, bytes), "ordered retry retains immutable frame"); values.Add(bytes.AsContiguous()[^1]); return true; });
        True(values.SequenceEqual(new byte[] { 1, 3, 4, 2 }), "ordered updates survive backpressure without coalescing");
        byte[] recovery = { 21, 0, 0, 0, 1, 0, 1, 0 };
        True(session.Recover(recovery), "granted state recovers without a gameplay request");
        session.Pump((topic, bytes) => { True(topic == UiFeatureSession.StateTopic && UiFeatureSession.Read32(bytes, 8) == 4 && UiFeatureSession.Read32(bytes, 12) == 1 && bytes[16] == 4, "recovery emits a revisioned snapshot reset"); return true; });
        True(provider.Calls == 0, "read recovery cannot invoke gameplay operation");
        var staleRead = (byte[])recovery.Clone(); staleRead[0] = 20;
        True(!session.Recover(staleRead) && !session.Recover(new byte[12]), "stale and malformed reads rejected");
        var deniedRead = (byte[])recovery.Clone(); deniedRead[4] = 3;
        True(!session.Recover(deniedRead), "ungranted state cannot be recovered");
        provider.Change(5); provider.Change(6); provider.Change(7); provider.Change(8);
        True(session.OrderedOverflow, "capacity exhaustion is explicit");
        session.Dispatch(session.FrameRequest(provider.Definition.Name, "Update", 1, Array.Empty<byte>()));
        True(provider.Calls == 0, "overflow fences gameplay requests immediately");
        var messages = new List<(ushort topic, byte[] bytes)>();
        session.Pump((topic, bytes) => { messages.Add((topic, bytes)); return true; });
        True(messages.Count == 1 && messages[0].topic == UiFeatureSession.MetadataTopic && System.Text.Encoding.UTF8.GetString(messages[0].bytes).Contains("ordered-overflow"), "overflow retires the activation instead of publishing a partial sequence");
        provider.Change(9); session.Pump((_, _) => throw new Exception("retired publication"));
        using var fresh = new UiFeatureSession(selection, 22, orderedCapacity: 1);
        values.Clear(); fresh.Pump((topic, bytes) => { if (topic == UiFeatureSession.StateTopic) values.Add(bytes[^1]); return true; });
        True(values.SequenceEqual(new byte[] { 9, 2 }), "fresh activation starts from current snapshots, not lost history");
        registration.Dispose(); provider.Change(10);
        recovery[0] = 22; True(!fresh.Recover(recovery), "retired provider cannot be read");
        messages.Clear(); fresh.Pump((topic, bytes) => { messages.Add((topic, bytes)); return true; });
        True(messages.Count == 1 && messages[0].topic == UiFeatureSession.MetadataTopic, "provider replacement discards queued ordered values");
    }
}
