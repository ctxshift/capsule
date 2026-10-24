using System;
using System.Threading;
using System.Collections.Concurrent;
using System.Linq;
using Capsule;

static class HostSessionCases
{
    static int checks;
    static readonly ConcurrentBag<Guest> guests = new ConcurrentBag<Guest>();
    static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    sealed class Theme : IUiTheme
    {
        public bool AllowsStyle(string key) => true;
        public bool AllowsAsset(string key) => true;
    }
    sealed class Guest : IUiGuest, IUiSchedulingDiagnostics
    {
        readonly IUiCommitTarget output;
        readonly int owner = Thread.CurrentThread.ManagedThreadId;
        public int Disposals, DisposalThread;
        public int Updates;
        public ulong EventHandle;
        readonly bool eventFixture;
        readonly ManualResetEventSlim initializeGate, entered;
        public UiSession Session { get; } = new UiSession(1, new Theme());
        public Action<string> InitializationTrace { get; set; }
        public bool SupportsScheduling => true;
        public long? NextWakeTimestamp => null;
        public Guest(IUiCommitTarget output, ManualResetEventSlim gate = null, ManualResetEventSlim entered = null, bool eventFixture = false) { this.output = output; initializeGate = gate; this.entered = entered; this.eventFixture = eventFixture; guests.Add(this); }
        public bool Initialize(out string error)
        { entered?.Set(); if (initializeGate != null && !initializeGate.Wait(3000)) throw new TimeoutException(); error = null; return true; }
        public bool Update(out string error)
        {
            ++Updates;
            if (eventFixture && Updates <= 2)
            {
                if (Updates == 1) Session.ReserveHandle(out EventHandle);
                var packet = Updates == 1
                    ? Program.Packet(Program.Create(EventHandle, 0, UiNodeKind.Window, "", "", "Quest", UiEventMask.Close))
                    : Program.Packet(Program.Remove(EventHandle));
                var result = Session.Submit(packet, output); error = result.Success ? null : result.Detail; return result.Success;
            }
            return output.TryCommit(new UiSnapshot(1, Array.Empty<UiNode>()), out error);
        }
        public bool SetViewport(int width, int height, out string error) { error = null; return true; }
        public void Dispose() { DisposalThread = Thread.CurrentThread.ManagedThreadId; Interlocked.Increment(ref Disposals); }
        public bool DisposedOnOwner => Disposals == 1 && DisposalThread == owner;
    }
    static UiAsyncCompletion Completion(UiAsyncHostSession host)
    {
        UiAsyncCompletion result = null;
        Check(SpinWait.SpinUntil(() => host.TryGetCompletion(out result), 3000), "Bounded completion wait");
        return result;
    }
    static long Load(UiAsyncHostSession host) => host.RequestLoad((output, _, __) => new Guest(output));
    static bool Commit(UiAsyncCompletion completion, out string error) { error = null; return true; }
    static bool Reject(UiAsyncCompletion completion, out string error) { error = "fixture rejected"; return false; }
    static bool Throw(UiAsyncCompletion completion, out string error) { throw new InvalidOperationException("fixture commit exception"); }
    static void Accept(UiAsyncHostSession host, UiAsyncCompletion completion)
    { Check(host.TryApply(completion, Commit, null, out _) == UiAsyncApplyResult.Accepted, "Accepted host publication"); }
    public static int Run()
    {
        using (var host = new UiAsyncHostSession())
        {
            Guest guest = null;
            long generation = host.RequestLoad((output, _, __) => guest = new Guest(output, eventFixture: true));
            Accept(host, Completion(host));
            host.EnqueueObservation(generation, 1, 1, new byte[] { 1 });
            var removed = Completion(host);
            Check(host.EnqueueEvent(generation, guest.EventHandle, UiEventMask.Close, Array.Empty<byte>()), "Late presented input queues during publication");
            host.EnqueueViewport(generation, 100, 100);
            Accept(host, removed);
            var afterLateInput = Completion(host);
            Check(afterLateInput.Error == null && guest.Updates == 3, "Removed-node input is dropped without guest execution or failure");
            Accept(host, afterLateInput);
            Check(host.ActiveGeneration == generation && !host.IsRetired, "Late input retains the addon");
            host.EnqueueEvent(generation, guest.EventHandle + 100, UiEventMask.Close, Array.Empty<byte>());
            Check(Completion(host).Error != null, "Invented handles remain errors");
        }
        using (var dispatcher = new UiAsyncDispatcher(2))
        using (var first = new UiAsyncHostSession(dispatcher))
        using (var second = new UiAsyncHostSession(dispatcher))
        {
            long old = Load(first); var initial = Completion(first); Accept(first, initial);
            long peer = Load(second); Accept(second, Completion(second));
            Check(first.ActiveGeneration == old && second.ActiveGeneration == peer, "Independent generations");
            Check(first.TryApply(initial, Throw, null, out _) == UiAsyncApplyResult.Stale, "Repeated completion never commits");
            Load(first); var rejected = Completion(first);
            Check(first.TryApply(rejected, Reject, null, out _) == UiAsyncApplyResult.Rejected && first.ActiveGeneration == old, "Rejected replacement retains active generation");
            Load(first); var thrown = Completion(first);
            Check(first.TryApply(thrown, Throw, null, out _) == UiAsyncApplyResult.Rejected && first.ActiveGeneration == old, "Throwing candidate commit retains old generation");
            Load(first); var stale = Completion(first); long replacement = Load(first);
            Check(first.TryApply(stale, Throw, null, out _) == UiAsyncApplyResult.Stale, "Superseded candidate never commits");
            Accept(first, Completion(first));
            Check(first.ActiveGeneration == replacement, "Replacement promoted");
            Check(!first.EnqueueEvent(old, 1, UiEventMask.Pointer, UiInputPayload.Pointer(UiPointerPhase.Up, 0, 0, 0)) && !first.IsRetired, "Stale terminal input cannot retire replacement");
            Exception threadFailure = null;
            var thread = new Thread(() => { try { first.EnqueueViewport(replacement, 10, 10); } catch (Exception error) { threadFailure = error; } });
            thread.Start(); thread.Join();
            Check(threadFailure is InvalidOperationException, "Host thread affinity");
            Check(first.EnqueueObservation(replacement, 1, 1, new byte[] { 1 }), "Current observation queued");
            var live = Completion(first); // Hold publication: worker cannot drain input until ack.
            for (int i = 0; i < UiAsyncWorker.QueueCapacity; i++)
                Check(first.EnqueueEvent(replacement, 1, UiEventMask.Pointer, UiInputPayload.Pointer(UiPointerPhase.Down, i, 0, 0)), "Bounded queue accepts capacity");
            Check(!first.EnqueueEvent(replacement, 1, UiEventMask.Pointer, UiInputPayload.Pointer(UiPointerPhase.Up, 0, 0, 0)), "Full queue rejects terminal event");
            Check(first.ActiveGeneration == 0 && first.IsRetired, "Lost terminal input retires immediately");
            Check(first.TryApply(live, Throw, null, out _) == UiAsyncApplyResult.Stale, "Retired completion cannot commit");
            Check(second.ActiveGeneration == peer && second.EnqueueViewport(peer, 100, 100), "Peer unaffected by retirement");
            Accept(second, Completion(second));
        }
        using (var host = new UiAsyncHostSession())
        {
            Load(host); var completion = Completion(host);
            Check(host.TryApply(completion, Commit, _ => throw new Exception("promotion failed"), out _) == UiAsyncApplyResult.Rejected && host.IsRetired && host.ActiveGeneration == 0, "Promotion failure retires mismatched native state");
        }
        using (var host = new UiAsyncHostSession())
        {
            Load(host); var completion = Completion(host);
            bool CommitThenInvalidate(UiAsyncCompletion value, out string error)
            { error = null; host.Worker.Dispose(); return true; } // Fault injection: external worker stop between commit and ack.
            Check(host.TryApply(completion, CommitThenInvalidate, null, out _) == UiAsyncApplyResult.Rejected && host.IsRetired, "Failed ack after commit retires input");
        }
        using (var host = new UiAsyncHostSession())
        {
            Load(host); var completion = Completion(host);
            bool Reenter(UiAsyncCompletion value, out string error) { error = null; host.Dispose(); return true; }
            Check(host.TryApply(completion, Reenter, null, out _) == UiAsyncApplyResult.Rejected && host.ActiveGeneration == 0, "Reentrant lifecycle mutation rejected");
        }
        using (var host = new UiAsyncHostSession())
        using (var gate = new ManualResetEventSlim(false))
        using (var entered = new ManualResetEventSlim(false))
        {
            Load(host); Accept(host, Completion(host));
            host.RequestLoad((output, _, __) => new Guest(output, gate, entered));
            Check(entered.Wait(3000), "Replacement initialization is in flight" );
            host.Dispose(); gate.Set();
            Check(host.ActiveGeneration == 0 && SpinWait.SpinUntil(() => host.Worker.IsStopped, 3000), "Disposal during replacement stops safely");
        }
        using (var host = new UiAsyncHostSession())
        {
            long generation = Load(host); Accept(host, Completion(host));
            Check(host.EnqueueViewport(generation, 100, 100), "Live update queued");
            Check(host.TryApply(Completion(host), Throw, null, out _) == UiAsyncApplyResult.Rejected && host.IsRetired,
                "Throwing live commit retires input");
        }
        Check(SpinWait.SpinUntil(() => guests.All(guest => Volatile.Read(ref guest.Disposals) > 0), 3000),
            "Every instantiated old/candidate guest disposed after bounded shutdown");
        Check(guests.All(guest => guest.DisposedOnOwner), "Every guest disposed exactly once on worker owner thread");
        Console.WriteLine("PASS host lifecycle: " + checks + " checks (includes 128 queue-capacity assertions); " + guests.Count + " guest lifetimes verified");
        return checks;
    }
}
