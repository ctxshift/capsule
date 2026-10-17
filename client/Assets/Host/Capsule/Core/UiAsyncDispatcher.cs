using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Capsule
{
    // One finite host-owned set of addon endpoints. Guest calls and disposal all
    // occur on this thread; main-thread presentation acknowledges each separately.
    public sealed class UiAsyncDispatcher : IDisposable
    {
        internal readonly object Gate = new object();
        readonly List<UiAsyncWorker> workers = new List<UiAsyncWorker>();
        readonly int capacity;
        bool stopping, stopped;
        public bool IsStopped { get { lock (Gate) return stopped; } }

        public UiAsyncDispatcher(int maxAddons)
        {
            if (maxAddons < 1 || maxAddons > 256) throw new ArgumentOutOfRangeException(nameof(maxAddons));
            capacity = maxAddons;
            new Thread(Run) { IsBackground = true, Name = "Capsule UI owner" }.Start();
        }

        internal void Register(UiAsyncWorker worker)
        {
            lock (Gate)
            {
                if (stopping) throw new ObjectDisposedException(nameof(UiAsyncDispatcher));
                workers.RemoveAll(item => item.IsStopped);
                if (workers.Count >= capacity) throw new InvalidOperationException("Addon worker capacity exceeded.");
                workers.Add(worker);
                Monitor.PulseAll(Gate);
            }
        }

        public void Dispose()
        {
            lock (Gate)
            {
                stopping = true;
                foreach (var worker in workers) worker.Dispose();
                Monitor.PulseAll(Gate);
            }
        }

        void Run()
        {
            // No per-addon wait/join on publication. Every ready endpoint gets
            // one bounded existing guest invocation before the next round.
            while (true)
            {
                UiAsyncWorker[] round;
                lock (Gate)
                {
                    workers.RemoveAll(item => item.IsStopped);
                    if (stopping && workers.Count == 0) { stopped = true; return; }
                    round = workers.ToArray();
                }
                bool worked = false;
                foreach (var worker in round) worked |= worker.RunOne();
                if (worked) continue;
                lock (Gate)
                {
                    // Recheck under the same lock as producers to avoid losing
                    // a wake between the last turn and entering the wait.
                    bool ready = stopping;
                    long? due = null;
                    foreach (var worker in workers)
                    {
                        ready |= worker.Ready;
                        var next = worker.NextDeadline;
                        if (next.HasValue && (!due.HasValue || next.Value < due.Value)) due = next;
                    }
                    if (ready) continue;
                    int timeout = due.HasValue
                        ? (int)Math.Min(int.MaxValue, Math.Max(0, Math.Ceiling((due.Value - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency)))
                        : Timeout.Infinite;
                    Monitor.Wait(Gate, timeout);
                }
            }
        }
    }
}
