using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace PortableUi
{
    public readonly struct UiCallFuelMetrics
    {
        public readonly ulong Activation, Updates, Disposal;
        public readonly double DisposalMilliseconds;
        public readonly bool Available;
        public ulong Total => checked(Activation + Updates + Disposal);
        internal UiCallFuelMetrics(ulong activation, ulong updates, ulong disposal, double disposalMs, bool available)
        { Activation = activation; Updates = updates; Disposal = disposal; DisposalMilliseconds = disposalMs; Available = available; }
    }
    // The factory must capture only immutable managed data. It runs on the owner thread,
    // as do every guest call and disposal. Unity adapters must never enter this boundary.
    public delegate IUiGuest UiAsyncGuestFactory(IUiCommitTarget target,
        IUiObservationAdapter observations, IUiActionAdapter actions);

    public sealed class UiAsyncAction
    {
        readonly byte[] payload;
        public ushort Capability { get; }
        public ulong Handle { get; }
        public byte[] CopyPayload() => (byte[])payload.Clone();
        internal UiAsyncAction(ushort capability, ulong handle, byte[] data)
        { Capability = capability; Handle = handle; payload = (byte[])data.Clone(); }
    }

    public sealed class UiAsyncCompletion
    {
        public long Sequence { get; }
        public long Generation { get; }
        public bool Candidate { get; }
        public UiSnapshot Snapshot { get; }
        public IReadOnlyList<UiAsyncAction> Actions { get; }
        public string Error { get; }
        public double WorkerMilliseconds { get; }
        public double QueueMilliseconds { get; }
        public int WorkerThreadId { get; }
        public long EnqueuedTimestamp { get; }
        public ulong Fuel { get; }
        public double LayoutMilliseconds { get; }
        public double CoordinatorMilliseconds { get; }
        public double ForwardMilliseconds { get; }
        public int LayoutCalculations { get; }
        public int LayoutMeasurements { get; }
        internal UiAsyncCompletion(long sequence, long generation, bool candidate,
            UiSnapshot snapshot, UiAsyncAction[] actions, string error, double work, double age, long enqueued, ulong fuel, IUiLayoutDiagnostics layout = null)
        {
            Sequence = sequence; Generation = generation; Candidate = candidate;
            Snapshot = snapshot; Actions = Array.AsReadOnly(actions); Error = error;
            WorkerMilliseconds = work; QueueMilliseconds = age;
            WorkerThreadId = Thread.CurrentThread.ManagedThreadId;
            EnqueuedTimestamp = enqueued; Fuel = fuel;
            LayoutMilliseconds=layout?.LastLayoutMilliseconds??0; CoordinatorMilliseconds=layout?.LastCoordinatorMilliseconds??0;
            ForwardMilliseconds=layout?.LastForwardMilliseconds??0; LayoutCalculations=layout?.LastCalculations??0;LayoutMeasurements=layout?.LastMeasurements??0;
        }
    }

    // One serialized owner, one outstanding completion. Main applies outside this lock
    // then acknowledges; no guest can advance beyond an unaccepted native snapshot.
    public sealed class UiAsyncWorker : IDisposable
    {
        public const int QueueCapacity = 128;
        public const int TimerMilliseconds = 50;
        static long generationSequence;
        readonly object gate;
        readonly LinkedList<Input> inputs = new LinkedList<Input>();
        readonly UiAsyncDispatcher dispatcher;
        readonly bool ownsDispatcher;
        bool stopping, stopped, acknowledged, accepted, busy;
        long requestedGeneration, activeGeneration, sequence, timerWakes;
        string terminalError;
        long jobs;
        double totalWorkMilliseconds;
        ulong totalFuel;
        ulong activationCallFuel, updateCallFuel, disposalCallFuel;
        double disposalMilliseconds;
        bool callFuelAvailable = true;
        long scheduledNotBefore;
        long updateCalls;
        public long UpdateCalls => Interlocked.Read(ref updateCalls);
        UiAsyncCompletion completion;
        Load pendingLoad;
        IUiGuest active;
        Exchange activeExchange;
        IUiGuest candidateGuest;
        Exchange candidateExchange;

        enum Kind { Observation, Event, Viewport }
        sealed class Input
        {
            public Kind Kind;
            public long Generation, Enqueued;
            public ushort Topic, Schema;
            public byte[] Payload;
            public ulong Handle;
            public UiEventMask Event;
            public int Width, Height;
        }
        sealed class Load
        {
            public long Generation, Enqueued;
            public UiAsyncGuestFactory Factory;
        }
        sealed class Exchange : IUiCommitTarget, IUiObservationAdapter, IUiActionAdapter
        {
            public UiSnapshot Snapshot;
            public readonly List<UiAsyncAction> Actions = new List<UiAsyncAction>();
            public Input Observation;
            public bool Candidate, Disposing;
            public void Reset() { Snapshot = null; Actions.Clear(); Observation = null; }
            public bool TryCommit(UiSnapshot proposed, out string error)
            {
                error = null;
                if (Disposing) return true;
                if (Snapshot != null) { error = "Async prototype permits one snapshot per job."; return false; }
                Snapshot = proposed;
                return true;
            }
            public bool TryDequeue(out ushort topic, out ushort schemaVersion, out byte[] payload)
            {
                var value = Observation; Observation = null;
                topic = value?.Topic ?? 0; schemaVersion = value?.Schema ?? 0;
                payload = value == null ? null : (byte[])value.Payload.Clone();
                return value != null;
            }
            public bool TryRequest(ushort capability, ulong handle, byte[] payload)
            {
                if (Candidate || Disposing || payload == null || payload.Length > UiSession.MaxEventPayloadBytes || Actions.Count >= 16) return false;
                Actions.Add(new UiAsyncAction(capability, handle, payload));
                return true;
            }
        }

        public UiAsyncWorker() : this(new UiAsyncDispatcher(1), true) { }
        public UiAsyncWorker(UiAsyncDispatcher owner) : this(owner, false) { }
        UiAsyncWorker(UiAsyncDispatcher owner, bool owns)
        {
            dispatcher = owner ?? throw new ArgumentNullException(nameof(owner));
            ownsDispatcher = owns;
            gate = owner.Gate;
            owner.Register(this);
        }
        public bool IsStopped { get { lock (gate) return stopped; } }
        public long TimerWakes => Interlocked.Read(ref timerWakes);
        public long ActiveGeneration { get { lock (gate) return activeGeneration; } }
        public int PendingInputs { get { lock (gate) return inputs.Count; } }
        public string TerminalError { get { lock (gate) return terminalError; } }
        public string DiagnosticState
        {
            get
            {
                lock (gate) return "active=" + activeGeneration + ";requested=" + requestedGeneration +
                    ";stopping=" + stopping + ";stopped=" + stopped + ";busy=" + busy + ";inputs=" + inputs.Count +
                    ";ack=" + acknowledged + ";accepted=" + accepted + ";completion=" + (completion?.Sequence ?? 0) + ";error=" + terminalError;
            }
        }
        public bool IsIdle { get { lock (gate) return !busy && inputs.Count == 0 && pendingLoad == null && (completion == null || acknowledged); } }
        public void ReadWork(out long count, out double milliseconds, out ulong fuel)
        { lock (gate) { count = jobs; milliseconds = totalWorkMilliseconds; fuel = totalFuel; } }
        public UiCallFuelMetrics ReadCallFuel()
        { lock (gate) return new UiCallFuelMetrics(activationCallFuel, updateCallFuel, disposalCallFuel, disposalMilliseconds, callFuelAvailable && jobs != 0); }
        static ulong CallFuel(IUiGuest guest) => (guest as IUiCallFuelDiagnostics)?.TotalCallFuel ?? 0;
        void RecordWork(long start, IUiGuest guest, bool activation, ulong before = 0)
        {
            lock (gate)
            {
                ++jobs; totalWorkMilliseconds += Milliseconds(Stopwatch.GetTimestamp() - start);
                totalFuel += (guest as IUiFuelDiagnostics)?.LastFuel ?? 0;
                callFuelAvailable &= (guest as IUiCallFuelDiagnostics)?.HasCallFuel ?? false;
                ulong used = checked(CallFuel(guest) - before);
                if (activation) activationCallFuel = checked(activationCallFuel + used);
                else updateCallFuel = checked(updateCallFuel + used);
            }
        }

        public long RequestLoad(UiAsyncGuestFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            lock (gate)
            {
                if (stopping) throw new ObjectDisposedException(nameof(UiAsyncWorker));
                // A stopped worker may be replaced while an old native callback
                // still holds its generation. Never reuse that identity.
                var generation = Interlocked.Increment(ref generationSequence);
                if (generation <= 0) throw new OverflowException("UI generation exhausted.");
                requestedGeneration = generation;
                pendingLoad = new Load { Generation = generation, Factory = factory, Enqueued = Stopwatch.GetTimestamp() };
                Monitor.PulseAll(gate);
                return generation;
            }
        }
        public bool EnqueueObservation(long generation, ushort topic, ushort schema, byte[] payload) =>
            Enqueue(new Input { Kind = Kind.Observation, Generation = generation, Topic = topic, Schema = schema, Payload = Copy(payload) });
        public bool EnqueueEvent(long generation, ulong handle, UiEventMask kind, byte[] payload) =>
            UiInputPayload.Valid(kind, payload) &&
            Enqueue(new Input { Kind = Kind.Event, Generation = generation, Handle = handle, Event = kind, Payload = Copy(payload) });
        public bool EnqueueViewport(long generation, int width, int height) =>
            Enqueue(new Input { Kind = Kind.Viewport, Generation = generation, Width = width, Height = height });
        static byte[] Copy(byte[] payload)
        {
            if (payload == null || payload.Length > UiSession.MaxEventPayloadBytes) throw new ArgumentException("Input payload quota.");
            return (byte[])payload.Clone();
        }
        bool Enqueue(Input input)
        {
            lock (gate)
            {
                if (stopping || input.Generation != activeGeneration || activeGeneration == 0) return false;
                input.Enqueued = Stopwatch.GetTimestamp();
                // Only replace a matching tail state. An event is an ordering barrier.
                var tail = inputs.Last;
                if (tail != null && input.Kind == Kind.Event && input.Event == UiEventMask.Pointer &&
                    tail.Value.Kind == Kind.Event && tail.Value.Event == UiEventMask.Pointer &&
                    tail.Value.Generation == input.Generation && tail.Value.Handle == input.Handle &&
                    UiInputPayload.SupersedesMove(tail.Value.Payload, input.Payload))
                    tail.Value = input;
                else if (tail != null && input.Kind != Kind.Event && tail.Value.Kind == input.Kind &&
                    tail.Value.Generation == input.Generation && tail.Value.Topic == input.Topic && tail.Value.Schema == input.Schema)
                    tail.Value = input;
                else
                {
                    if (inputs.Count == QueueCapacity) return false;
                    inputs.AddLast(input);
                }
                Monitor.PulseAll(gate);
                return true;
            }
        }
        public bool TryGetCompletion(out UiAsyncCompletion value)
        { lock (gate) { value = acknowledged ? null : completion; return value != null; } }

        // Returns false for superseded candidate completions and stale/repeated acks.
        // Call before exposing the accepted generation to callbacks on the main thread.
        public bool Acknowledge(long sequenceNumber, bool nativeAccepted)
        {
            lock (gate)
            {
                if (stopping || completion == null || completion.Sequence != sequenceNumber || acknowledged ||
                    (completion.Candidate && completion.Generation != requestedGeneration)) return false;
                accepted = nativeAccepted && completion.Error == null;
                acknowledged = true;
                if (accepted && completion.Candidate)
                {
                    activeGeneration = completion.Generation;
                    var node = inputs.First;
                    while (node != null)
                    {
                        var next = node.Next;
                        if (node.Value.Generation != activeGeneration) inputs.Remove(node);
                        node = next;
                    }
                }
                else if (!accepted && !completion.Candidate) activeGeneration = 0;
                Monitor.PulseAll(gate);
                return true;
            }
        }
        public void Dispose()
        {
            lock (gate) { stopping = true; inputs.Clear(); pendingLoad = null; Monitor.PulseAll(gate); }
            // Never join or free a live native store on the caller/main thread.
        }
        static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        internal bool Ready
        {
            get
            {
                lock (gate)
                    return !stopped && (stopping || (completion != null
                        ? acknowledged || completion.Candidate && completion.Generation != requestedGeneration
                        : pendingLoad != null || inputs.Count != 0 || NextDeadline <= Stopwatch.GetTimestamp()));
            }
        }
        internal long? NextDeadline
        {
            get
            {
                lock (gate)
                {
                    if (stopped || completion != null) return null;
                    var due = (active as IUiSchedulingDiagnostics)?.NextWakeTimestamp;
                    return due.HasValue ? Math.Max(due.Value, scheduledNotBefore) : (long?)null;
                }
            }
        }

        // Called only by the shared owner thread. One invocation/ack per turn;
        // waiting for one native publication never blocks another addon.
        internal bool RunOne()
        {
            Load load = null; Input input = null; bool finish = false, retire = false;
            lock (gate)
            {
                if (stopped) return false;
                retire = stopping;
                if (!retire && completion != null)
                {
                    finish = acknowledged || completion.Candidate && completion.Generation != requestedGeneration;
                    if (!finish) return false;
                }
                else if (!retire && pendingLoad != null) { load = pendingLoad; pendingLoad = null; }
                else if (!retire && inputs.Count != 0) { input = inputs.First.Value; inputs.RemoveFirst(); }
                else if (!retire)
                {
                    var due = NextDeadline;
                    if (!due.HasValue || due.Value > Stopwatch.GetTimestamp()) return false;
                    Interlocked.Increment(ref timerWakes);
                    scheduledNotBefore = Stopwatch.GetTimestamp() + Stopwatch.Frequency * TimerMilliseconds / 1000;
                }
                busy = true;
            }
            try
            {
                if (retire)
                {
                    Retire(ref candidateGuest, candidateExchange);
                    Retire(ref active, activeExchange);
                    lock (gate) { activeGeneration = 0; completion = null; stopped = true; Monitor.PulseAll(gate); }
                    if (ownsDispatcher) dispatcher.Dispose();
                }
                else if (finish) FinishPublication();
                else if (load != null) Initialize(load);
                else if (active != null) Update(input);
            }
            catch (Exception failure) { lock (gate) { terminalError = failure.ToString(); stopping = true; } }
            finally { lock (gate) busy = false; }
            return true;
        }

        void FinishPublication()
        {
            bool candidate, commit;
            lock (gate)
            {
                candidate = completion.Candidate;
                commit = acknowledged && accepted;
                completion = null;
            }
            if (candidate)
            {
                if (commit)
                {
                    Retire(ref active, activeExchange);
                    active = candidateGuest; candidateGuest = null;
                    activeExchange = candidateExchange; activeExchange.Candidate = false;
                }
                Retire(ref candidateGuest, candidateExchange);
                candidateExchange = null;
            }
            else if (!commit) Retire(ref active, activeExchange);
        }
        void Initialize(Load load)
        {
            var exchange = new Exchange { Candidate = true };
            IUiGuest candidate = null;
            var start = Stopwatch.GetTimestamp();
            string error = null;
            try
            {
                candidate = load.Factory(exchange, exchange, exchange);
                if (!(candidate is IUiSchedulingDiagnostics scheduling) || !scheduling.SupportsScheduling)
                    throw new InvalidOperationException("Async guest requires versioned scheduling diagnostics.");
                bool ok = candidate.Initialize(out error);
                if (ok) { Interlocked.Increment(ref updateCalls); ok = candidate.Update(out error); }
                error = ok ? null : string.IsNullOrEmpty(error) ? "Candidate initialization failed." : error;
                if (error == null && exchange.Snapshot == null) error = "Candidate did not produce a first snapshot.";
            }
            catch (Exception failure) { error = failure.Message; }
            RecordWork(start, candidate, true);
            candidateGuest = candidate; candidateExchange = exchange;
            if (!Publish(load.Generation, true, exchange, error, start, load.Enqueued, (candidate as IUiFuelDiagnostics)?.LastFuel ?? 0, candidate as IUiLayoutDiagnostics))
                Retire(ref candidateGuest, exchange);
        }
        void Update(Input input)
        {
            long generation;
            lock (gate) generation = activeGeneration;
            if (input != null && input.Generation != generation) return;
            var start = Stopwatch.GetTimestamp();
            ulong before = CallFuel(active);
            activeExchange.Reset();
            string error = null;
            try
            {
                if (input?.Kind == Kind.Observation) activeExchange.Observation = input;
                else if (input?.Kind == Kind.Event)
                {
                    var result = active.Session.QueueEvent(input.Handle, input.Event, input.Payload);
                    if (!result.Success) error = result.Detail;
                }
                else if (input?.Kind == Kind.Viewport)
                {
                    bool ok = active.SetViewport(input.Width, input.Height, out error);
                    error = ok ? null : string.IsNullOrEmpty(error) ? "Viewport failed." : error;
                }
                if (error == null)
                {
                    Interlocked.Increment(ref updateCalls);
                    bool ok = active.Update(out error);
                    error = ok ? null : string.IsNullOrEmpty(error) ? "Guest update failed." : error;
                }
            }
            catch (Exception failure) { error = failure.Message; }
            RecordWork(start, active, false, before);
            // Idle work has no publication/ack traffic. Counters remain available.
            if (error == null && activeExchange.Snapshot == null && activeExchange.Actions.Count == 0) return;
            if (!Publish(generation, false, activeExchange, error, start, input?.Enqueued ?? start, (active as IUiFuelDiagnostics)?.LastFuel ?? 0, active as IUiLayoutDiagnostics))
                Retire(ref active, activeExchange);
        }
        bool Publish(long generation, bool candidate, Exchange exchange, string error, long start, long enqueued, ulong fuel, IUiLayoutDiagnostics layout)
        {
            var end = Stopwatch.GetTimestamp();
            lock (gate)
            {
                if (stopping || (candidate && generation != requestedGeneration)) return false;
                completion = new UiAsyncCompletion(++sequence, generation, candidate, exchange.Snapshot,
                    exchange.Actions.ToArray(), error, Milliseconds(end - start), Milliseconds(start - enqueued), enqueued, fuel, layout);
                acknowledged = false; accepted = false;
                return true;
            }
        }
        void Retire(ref IUiGuest guest, Exchange exchange)
        {
            if (guest == null) return;
            ulong before = CallFuel(guest);
            long start = Stopwatch.GetTimestamp();
            if (exchange != null) exchange.Disposing = true;
            try { guest.Dispose(); }
            catch (Exception failure) { lock (gate) terminalError = "Guest cleanup failed: " + failure.Message; }
            finally
            {
                lock (gate)
                {
                    callFuelAvailable &= (guest as IUiCallFuelDiagnostics)?.HasCallFuel ?? false;
                    disposalCallFuel = checked(disposalCallFuel + CallFuel(guest) - before);
                    disposalMilliseconds += Milliseconds(Stopwatch.GetTimestamp() - start);
                }
                guest = null;
            }
        }
    }
}
