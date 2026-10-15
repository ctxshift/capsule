using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace PortableUi.Package
{
    // One activation owns one session. Transport identifiers carry no game semantics.
    public sealed class UiFeatureSession : IDisposable
    {
        public const ushort MetadataTopic = 65000, StateTopic = 65001, ResultTopic = 65002, Capability = 65000;
        readonly UiFeatureSelection selection;
        readonly uint epoch;
        readonly List<IDisposable> subscriptions = new List<IDisposable>();
        readonly Dictionary<uint, byte[]> dirty = new Dictionary<uint, byte[]>();
        readonly Dictionary<uint, UiObservationPayload> stateFrames = new Dictionary<uint, UiObservationPayload>();
        readonly Dictionary<uint, UiObservationPayload> resultFrames = new Dictionary<uint, UiObservationPayload>();
        readonly Dictionary<uint, Task<byte[]>> pending = new Dictionary<uint, Task<byte[]>>();
        readonly Dictionary<uint, Func<byte[], Task<byte[]>>> requests = new Dictionary<uint, Func<byte[], Task<byte[]>>>();
        readonly Dictionary<string, uint> requestKeys = new Dictionary<string, uint>();
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        byte[] metadata;
        uint lastCall;
        bool disposed, retired;
        public int PendingStateCount => dirty.Count;
        public long PublishedStates { get; private set; }
        public event Action StatePublished;
        public UiFeatureSession(UiFeatureSelection selection, uint epoch)
        {
            this.selection = selection ?? throw new ArgumentNullException(nameof(selection));
            if (epoch == 0) throw new ArgumentOutOfRangeException(nameof(epoch));
            this.epoch = epoch;
            var descriptors = new List<object>();
            try
            {
                ushort id = 0;
                foreach (var entry in selection.Providers.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    var provider = entry.Value.Provider as IUiFeatureProvider ?? throw new InvalidOperationException("Feature requires a generated provider binding");
                    var definition = entry.Value.Definition;
                    if (provider.Definition.Name != definition.Name || provider.Definition.Version != definition.Version || provider.Definition.Fingerprint != definition.Fingerprint)
                        throw new InvalidOperationException("Provider binding identity mismatch");
                    id = checked((ushort)(id + 1));
                    ushort member = 0;
                    var states = new Dictionary<string, ushort>(); var operations = new Dictionary<string, ushort>();
                    foreach (string name in provider.StateNames.OrderBy(n => n, StringComparer.Ordinal))
                    {
                        if (!selection.Allows(entry.Key, provider.StatePermission(name))) continue;
                        member = checked((ushort)(member + 1)); states.Add(name, member);
                        uint key = (uint)id << 16 | member;
                        subscriptions.Add(provider.Subscribe(name, bytes => { if (!disposed && !retired) { dirty[key] = bytes; stateFrames.Remove(key); StatePublished?.Invoke(); } }));
                    }
                    member = 0;
                    foreach (string name in provider.RequestNames.OrderBy(n => n, StringComparer.Ordinal))
                    {
                        string permission = provider.RequestPermission(name);
                        if (!selection.Allows(entry.Key, permission)) continue;
                        member = checked((ushort)(member + 1)); operations.Add(name, member);
                        uint key = (uint)id << 16 | member;
                        requests.Add(key, bytes => selection.Allows(entry.Key, permission)
                            ? provider.Request(name, bytes, cancellation.Token) : Task.FromException<byte[]>(new InvalidOperationException("Feature permission retired")));
                        requestKeys.Add(entry.Key + "/" + name, key);
                    }
                    descriptors.Add(new { id, name = definition.Name, version = definition.Version, fingerprint = definition.Fingerprint,
                        states, requests = operations });
                }
                metadata = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { epoch, features = descriptors }));
            }
            catch { Dispose(); throw; }
        }
        public static uint Read32(byte[] bytes, int at) => (uint)(bytes[at] | bytes[at + 1] << 8 | bytes[at + 2] << 16 | bytes[at + 3] << 24);
        static void Write32(byte[] bytes, int at, uint value) { for (int n = 0; n < 4; n++) bytes[at + n] = (byte)(value >> (n * 8)); }
        public byte[] FrameRequest(string feature, string operation, uint call, byte[] payload)
        {
            uint key = requestKeys[feature + "/" + operation];
            var frame = new byte[12 + payload.Length]; Write32(frame, 0, epoch); Write32(frame, 4, call); Write32(frame, 8, key);
            Buffer.BlockCopy(payload, 0, frame, 12, payload.Length); return frame;
        }
        public void Dispatch(byte[] frame)
        {
            if (disposed || retired || !selection.IsCurrent || frame == null || frame.Length < 12 || frame.Length > 256 || Read32(frame, 0) != epoch) return;
            uint call = Read32(frame, 4), key = Read32(frame, 8);
            if (call == 0 || call <= lastCall) return;
            if (pending.Count >= 64) throw new InvalidOperationException("Feature request backpressure exceeded");
            lastCall = call;
            if (!requests.TryGetValue(key, out var request)) { pending.Add(call, Task.FromException<byte[]>(new UnauthorizedAccessException())); return; }
            var payload = new byte[frame.Length - 12]; Buffer.BlockCopy(frame, 12, payload, 0, payload.Length);
            try { pending.Add(call, request(payload)); }
            catch (Exception error) { pending.Add(call, Task.FromException<byte[]>(error)); }
        }
        // Pump and provider callbacks run on the host's owning thread. Dirty state is
        // coalesced by member here; shared transport topics must never be coalesced.
        public void Pump(Func<ushort, byte[], bool> publish) => PumpSegments((topic, payload) => publish(topic, payload.AsContiguous()));
        public void PumpSegments(Func<ushort, UiObservationPayload, bool> publish)
        {
            if (disposed) return;
            if (!retired && !selection.IsCurrent)
            {
                retired = true; cancellation.Cancel();
                foreach (var subscription in subscriptions) subscription.Dispose(); subscriptions.Clear();
                dirty.Clear(); pending.Clear(); stateFrames.Clear(); resultFrames.Clear();
                metadata = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { epoch, features = new object[0] }));
            }
            if (metadata != null) { if (!publish(MetadataTopic, UiObservationPayload.Owned(metadata))) return; metadata = null; }
            foreach (var pair in dirty.ToArray())
            {
                // The callback borrows an immutable frame. Retain it on rejection;
                // acceptance transfers the same array without another envelope copy.
                if (!stateFrames.TryGetValue(pair.Key, out var frame))
                {
                    var header = new byte[8]; Write32(header, 0, epoch); Write32(header, 4, pair.Key);
                    frame = new UiObservationPayload(header, pair.Value);
                    stateFrames.Add(pair.Key, frame);
                }
                if (!publish(StateTopic, frame)) return;
                dirty.Remove(pair.Key); stateFrames.Remove(pair.Key); PublishedStates++;
            }
            foreach (var pair in pending.ToArray())
            {
                if (!pair.Value.IsCompleted) continue;
                var task = pair.Value;
                byte status = task.IsCanceled || task.IsFaulted ? (byte)1 : (byte)0;
                if (task.Exception?.GetBaseException() is UnauthorizedAccessException) status = 2;
                if (!resultFrames.TryGetValue(pair.Key, out var frame))
                {
                    byte[] payload = status == 0 ? task.Result : Array.Empty<byte>();
                    var header = new byte[9]; Write32(header, 0, epoch); Write32(header, 4, pair.Key); header[8] = status;
                    frame = new UiObservationPayload(header, payload);
                    resultFrames.Add(pair.Key, frame);
                }
                if (!publish(ResultTopic, frame)) return;
                pending.Remove(pair.Key); resultFrames.Remove(pair.Key);
            }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; cancellation.Cancel();
            foreach (var subscription in subscriptions) subscription.Dispose(); subscriptions.Clear();
            dirty.Clear(); pending.Clear(); stateFrames.Clear(); resultFrames.Clear(); cancellation.Dispose();
        }
    }
}
