using System;
using System.Threading;

namespace PortableUi
{
    public enum UiAsyncApplyResult { Stale, Rejected, Accepted }
    public delegate bool UiAsyncHostCommit(UiAsyncCompletion completion, out string error);

    // Host-thread lifecycle composition. The existing worker/dispatcher remains
    // the only scheduler. Presentation callbacks run inline and are never retained.
    public sealed class UiAsyncHostSession : IDisposable
    {
        readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        bool applying, retired;
        long candidateGeneration;
        public UiAsyncWorker Worker { get; }
        public long ActiveGeneration { get; private set; }
        public string RetirementReason { get; private set; }
        public bool IsRetired => retired;

        public UiAsyncHostSession() { Worker = new UiAsyncWorker(); }
        public UiAsyncHostSession(UiAsyncDispatcher dispatcher) { Worker = new UiAsyncWorker(dispatcher); }

        void CheckAccess()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("UI host session owner thread required.");
            if (applying) throw new InvalidOperationException("Reentrant UI host lifecycle mutation.");
        }
        public long RequestLoad(UiAsyncGuestFactory factory)
        {
            CheckAccess();
            if (retired) throw new ObjectDisposedException(nameof(UiAsyncHostSession));
            return candidateGeneration = Worker.RequestLoad(factory);
        }
        public bool TryGetCompletion(out UiAsyncCompletion completion)
        {
            CheckAccess();
            if (Worker.IsStopped && Worker.TerminalError != null) RetireCore(Worker.TerminalError);
            if (Worker.TryGetCompletion(out completion) && Current(completion)) return true;
            completion = null; return false;
        }
        public bool IsCurrent(UiAsyncCompletion completion)
        {
            CheckAccess();
            return Current(completion);
        }
        bool Current(UiAsyncCompletion completion) => !retired && completion != null &&
            (completion.Candidate ? completion.Generation == candidateGeneration :
                ActiveGeneration != 0 && completion.Generation == ActiveGeneration);

        public UiAsyncApplyResult TryApply(UiAsyncCompletion completion, UiAsyncHostCommit commit,
            Action<UiAsyncCompletion> promote, out string error)
        {
            CheckAccess();
            if (commit == null) throw new ArgumentNullException(nameof(commit));
            error = null;
            // Identity alone is insufficient: repeated/already acknowledged work must
            // never invoke a presentation callback a second time.
            if (!Current(completion) || !Worker.TryGetCompletion(out var pending) || !ReferenceEquals(pending, completion))
            { error = "Stale async completion."; return UiAsyncApplyResult.Stale; }
            applying = true;
            bool committed = false, acknowledged = false;
            try
            {
                error = completion.Error;
                if (error != null || !commit(completion, out error))
                {
                    error = error ?? "Host commit rejected.";
                    Worker.Acknowledge(completion.Sequence, false);
                    if (completion.Candidate) candidateGeneration = 0;
                    else RetireCore(error);
                    return UiAsyncApplyResult.Rejected;
                }
                committed = true;
                if (!Worker.Acknowledge(completion.Sequence, true))
                    throw new InvalidOperationException("Stale async acknowledgment after host commit.");
                acknowledged = true;
                if (completion.Candidate)
                { ActiveGeneration = completion.Generation; candidateGeneration = 0; }
                promote?.Invoke(completion);
                return UiAsyncApplyResult.Accepted;
            }
            catch (Exception failure)
            {
                error = failure.Message;
                if (!acknowledged) Worker.Acknowledge(completion.Sequence, false);
                if (committed || acknowledged || !completion.Candidate) RetireCore(error);
                else candidateGeneration = 0;
                return UiAsyncApplyResult.Rejected;
            }
            finally { applying = false; }
        }

        public bool EnqueueObservation(long generation, ushort topic, ushort schema, byte[] payload)
        {
            CheckAccess();
            return Live(generation) && Worker.EnqueueObservation(generation, topic, schema, payload);
        }
        public bool EnqueueViewport(long generation, int width, int height)
        {
            CheckAccess();
            return Live(generation) && Worker.EnqueueViewport(generation, width, height);
        }
        public bool EnqueueOwnedObservation(long generation, ushort topic, ushort schema, byte[] payload, bool replaceable = true)
        {
            CheckAccess();
            return Live(generation) && Worker.EnqueueOwnedObservation(generation, topic, schema, payload, replaceable);
        }
        public bool EnqueueSegmentedObservation(long generation, ushort topic, ushort schema, UiObservationPayload payload, bool replaceable = true)
        {
            CheckAccess();
            return Live(generation) && Worker.EnqueueSegmentedObservation(generation, topic, schema, payload, replaceable);
        }
        public bool EnqueueEvent(long generation, ulong handle, UiEventMask kind, byte[] payload)
        {
            CheckAccess();
            if (!Live(generation)) return false;
            if (Worker.EnqueueEvent(generation, handle, kind, payload)) return true;
            if (kind == UiEventMask.Pointer && UiInputPayload.Valid(kind, payload) &&
                (payload[1] == (byte)UiPointerPhase.Up || payload[1] == (byte)UiPointerPhase.Cancel))
                RetireCore("Input queue rejected pointer termination.");
            return false;
        }
        bool Live(long generation) => !retired && generation != 0 && generation == ActiveGeneration;
        public void Retire(string reason)
        {
            CheckAccess();
            RetireCore(reason);
        }
        void RetireCore(string reason)
        {
            if (retired) return;
            retired = true; RetirementReason = reason;
            ActiveGeneration = candidateGeneration = 0;
            Worker.Dispose();
        }
        public void Dispose() { CheckAccess(); RetireCore(null); }
    }
}
