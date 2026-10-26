using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Capsule;
using Newtonsoft.Json.Linq;

namespace Capsule.Host;

// Only trusted host configuration creates providers and grants. UI packages cannot register them.
public sealed class FeatureProviders : IDisposable
{
    public readonly UiFeatureRegistry Registry = new();
    public readonly List<UiFeatureGrant> Grants = new();
    readonly Dictionary<string, Provider> providers = new(StringComparer.Ordinal);
    readonly Dictionary<long, Pending> pending = new();
    readonly Queue<long> outgoing = new();
    readonly Queue<long> cancelled = new();
    long sequence;
    sealed class Pending
    {
        public string Feature, Operation;
        public byte[] Payload;
        public bool Delivered;
        public CancellationTokenRegistration Cancellation;
        public readonly TaskCompletionSource<byte[]> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    sealed class Provider : UiFeatureProvider
    {
        public Provider(UiFeatureDefinition definition, JObject value, FeatureProviders owner) : base(definition)
        {
            foreach (var member in (JObject)value["states"] ?? new JObject())
            {
                var item = (JObject)member.Value;
                string delivery = (string)item["delivery"];
                if (delivery != "ordered" && delivery != "latest") throw new ArgumentException("Invalid feature delivery.");
                AddState(member.Key, (string)item["permission"], Bytes((string)item["initial"]),
                    delivery == "ordered" ? UiFeatureDelivery.Ordered : UiFeatureDelivery.Latest);
            }
            foreach (var member in (JObject)value["requests"] ?? new JObject())
            {
                string operation = member.Key;
                AddRequest(operation, (string)member.Value["permission"], (bytes, token) => owner.Request(definition.Name, operation, bytes, token));
            }
        }
        public void PublishState(string member, byte[] bytes) => Publish(member, bytes);
    }
    public FeatureProviders(JArray definitions)
    {
        if (definitions == null) return;
        if (definitions.Count > 64) throw new ArgumentException("Feature provider quota.");
        foreach (JObject item in definitions)
        {
            var permissions = new HashSet<string>(StringComparer.Ordinal);
            foreach (string kind in new[] { "states", "requests" })
            {
                if (((JObject)item[kind])?.Count > 128) throw new ArgumentException("Feature member quota.");
                foreach (var member in (JObject)item[kind] ?? new JObject())
                {
                    if (!UiFeatureIdentity.Permission(member.Key) || member.Key.Length > 64) throw new ArgumentException("Invalid feature member.");
                    permissions.Add((string)member.Value["permission"]);
                }
            }
            var dependencies = new List<UiFeatureDependency>();
            foreach (var entry in (JObject)item["dependencies"] ?? new JObject()) dependencies.Add(new(entry.Key, (string)entry.Value));
            var definition = new UiFeatureDefinition((string)item["name"], (string)item["version"], (string)item["fingerprint"], dependencies, permissions);
            var provider = new Provider(definition, item, this);
            Registry.Register(definition, provider);
            providers.Add(definition.Name, provider);
            Grants.Add(new UiFeatureGrant(definition.Name, ((JArray)item["grants"] ?? new JArray()).Values<string>().ToArray()));
        }
    }
    static byte[] Bytes(string value)
    {
        if (value == null || value.Length > 5592408) throw new ArgumentException("Feature payload quota.");
        byte[] bytes = Convert.FromBase64String(value);
        if (bytes.Length > UiHostBridgeContract.MaxPayloadBytes) throw new ArgumentException("Feature payload quota.");
        return bytes;
    }
    Task<byte[]> Request(string feature, string operation, byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (pending.Count >= 64 || bytes.Length > UiHostBridgeContract.MaxPayloadBytes) throw new InvalidOperationException("Provider request backpressure.");
        long id = checked(++sequence);
        var entry = new Pending { Feature = feature, Operation = operation, Payload = (byte[])bytes.Clone() };
        pending.Add(id, entry); outgoing.Enqueue(id);
        entry.Cancellation = token.Register(() =>
        {
            if (pending.Remove(id))
            {
                entry.Completion.TrySetCanceled();
                if (entry.Delivered) cancelled.Enqueue(id);
            }
        });
        return entry.Completion.Task;
    }
    public void Publish(JObject value)
    {
        if (!providers.TryGetValue((string)value["feature"], out var provider)) throw new ArgumentException("Unknown host provider.");
        provider.PublishState((string)value["member"], Bytes((string)value["payload"]));
    }
    public bool Resolve(JObject value)
    {
        long id = long.Parse((string)value["token"]);
        if (!pending.TryGetValue(id, out var entry)) return false;
        byte[] bytes = (bool?)value["rejected"] == true ? null : Bytes((string)value["payload"]);
        pending.Remove(id); entry.Cancellation.Dispose();
        if (bytes == null) entry.Completion.TrySetException(new InvalidOperationException("Host provider rejected the request."));
        else entry.Completion.TrySetResult(bytes);
        return true;
    }
    public JArray DrainRequests()
    {
        var result = new JArray();
        while (outgoing.Count != 0)
        {
            long id = outgoing.Dequeue();
            if (!pending.TryGetValue(id, out var entry)) continue;
            entry.Delivered = true;
            result.Add(new JObject { ["token"] = id.ToString(), ["feature"] = entry.Feature, ["operation"] = entry.Operation, ["payload"] = Convert.ToBase64String(entry.Payload) });
        }
        return result;
    }
    public JArray DrainCancelled()
    {
        var result = new JArray();
        while (cancelled.Count != 0) result.Add(cancelled.Dequeue().ToString());
        return result;
    }
    public void Dispose()
    {
        foreach (var provider in providers.Values) provider.Dispose();
        foreach (var entry in pending.Values) { entry.Cancellation.Dispose(); entry.Completion.TrySetCanceled(); }
        pending.Clear(); outgoing.Clear(); cancelled.Clear();
    }
}
