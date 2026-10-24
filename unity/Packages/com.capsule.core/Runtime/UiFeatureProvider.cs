using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Capsule
{
    public enum UiFeatureDelivery { Latest, Ordered }
    // Engine-independent host interface. Arrays are immutable after publication;
    // getters/subscribers borrow the same encoded state, not per-subscriber copies.
    public interface IUiFeatureProvider : IDisposable
    {
        UiFeatureDefinition Definition { get; }
        IEnumerable<string> StateNames { get; }
        IEnumerable<string> RequestNames { get; }
        string StatePermission(string name);
        UiFeatureDelivery StateDelivery(string name);
        byte[] ReadState(string name);
        string RequestPermission(string name);
        IDisposable Subscribe(string name, Action<byte[]> receive);
        Task<byte[]> Request(string name, byte[] payload, CancellationToken cancellationToken);
    }
    public abstract class UiFeatureProvider : IUiFeatureProvider
    {
        sealed class State { public string Permission; public byte[] Bytes; public UiFeatureDelivery Delivery; public readonly HashSet<Action<byte[]>> Subscribers = new HashSet<Action<byte[]>>(); }
        sealed class Operation { public string Permission; public Func<byte[], CancellationToken, Task<byte[]>> Invoke; }
        sealed class Subscription : IDisposable
        {
            Action stop;
            public Subscription(Action stop) { this.stop = stop; }
            public void Dispose() { var action = stop; stop = null; action?.Invoke(); }
        }
        readonly Dictionary<string, State> states = new Dictionary<string, State>(StringComparer.Ordinal);
        readonly Dictionary<string, Operation> requests = new Dictionary<string, Operation>(StringComparer.Ordinal);
        bool disposed;
        public UiFeatureDefinition Definition { get; }
        public IEnumerable<string> StateNames => states.Keys;
        public IEnumerable<string> RequestNames => requests.Keys;
        protected UiFeatureProvider(UiFeatureDefinition definition) { Definition = definition ?? throw new ArgumentNullException(nameof(definition)); }
        protected void AddState(string name, string permission, byte[] initial, UiFeatureDelivery delivery = UiFeatureDelivery.Latest) { Ensure(); if (delivery != UiFeatureDelivery.Latest && delivery != UiFeatureDelivery.Ordered) throw new ArgumentOutOfRangeException(nameof(delivery)); states.Add(name, new State { Permission = permission, Bytes = initial ?? throw new ArgumentNullException(nameof(initial)), Delivery = delivery }); }
        protected void AddRequest(string name, string permission, Func<byte[], CancellationToken, Task<byte[]>> invoke) { Ensure(); requests.Add(name, new Operation { Permission = permission, Invoke = invoke }); }
        protected void Publish(string name, byte[] payload)
        {
            if (disposed) return;
            var state = states[name]; state.Bytes = payload ?? throw new ArgumentNullException(nameof(payload));
            foreach (var subscriber in new List<Action<byte[]>>(state.Subscribers)) subscriber(payload);
        }
        void Ensure() { if (disposed) throw new ObjectDisposedException(nameof(UiFeatureProvider)); }
        public string StatePermission(string name) { Ensure(); return states.TryGetValue(name, out var state) ? state.Permission : throw new ArgumentException("Unknown feature state"); }
        public UiFeatureDelivery StateDelivery(string name) { Ensure(); return states.TryGetValue(name, out var state) ? state.Delivery : throw new ArgumentException("Unknown feature state"); }
        public byte[] ReadState(string name) { Ensure(); return states.TryGetValue(name, out var state) ? state.Bytes : throw new ArgumentException("Unknown feature state"); }
        public string RequestPermission(string name) { Ensure(); return requests.TryGetValue(name, out var request) ? request.Permission : throw new ArgumentException("Unknown feature request"); }
        public IDisposable Subscribe(string name, Action<byte[]> receive)
        {
            Ensure(); if (receive == null) throw new ArgumentNullException(nameof(receive));
            if (!states.TryGetValue(name, out var state)) throw new ArgumentException("Unknown feature state");
            state.Subscribers.Add(receive);
            try { receive(state.Bytes); } catch { state.Subscribers.Remove(receive); throw; }
            return new Subscription(() => state.Subscribers.Remove(receive));
        }
        public Task<byte[]> Request(string name, byte[] payload, CancellationToken cancellationToken)
        {
            Ensure(); cancellationToken.ThrowIfCancellationRequested();
            if (!requests.TryGetValue(name, out var request)) throw new ArgumentException("Unknown feature request");
            return request.Invoke(payload, cancellationToken);
        }
        public virtual void Dispose() { if (disposed) return; disposed = true; foreach (var state in states.Values) state.Subscribers.Clear(); }
    }
}
