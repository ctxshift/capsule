using System;

namespace Capsule
{
    // Caller supplies monotonic seconds and counter deltas. No per-frame allocation.
    public sealed class UiPerformanceWindow
    {
        double start, previous, worst, work, apply, queue;
        long frames, commits, updates, completions;
        public void Reset(double now)
        {
            start = previous = now;
            worst = work = apply = queue = 0;
            frames = commits = updates = completions = 0;
        }
        public void Frame(double now, long commitDelta, long updateDelta)
        {
            worst = Math.Max(worst, now - previous);
            previous = now; frames++;
            commits += Math.Max(0, commitDelta); updates += Math.Max(0, updateDelta);
        }
        public void Completion(double workMs, double applyMs, double queueMs)
        {
            completions++; work += workMs; apply += applyMs; queue += queueMs;
        }
        public bool TryRead(double now, out UiPerformanceSample sample)
        {
            double seconds = now - start;
            sample = default;
            if (seconds < .5 || frames == 0) return false;
            sample = new UiPerformanceSample(frames / seconds, commits / seconds,
                updates / seconds, seconds * 1000 / frames, worst * 1000,
                completions == 0 ? 0 : work / completions,
                completions == 0 ? 0 : apply / completions,
                completions == 0 ? 0 : queue / completions);
            Reset(now);
            return true;
        }
    }
    public readonly struct UiPerformanceSample
    {
        public readonly double GameRate, CommitRate, JsRate, FrameMs, WorstFrameMs, WorkerMs, ApplyMs, QueueMs;
        public UiPerformanceSample(double game, double commits, double js, double frame,
            double worst, double worker, double apply, double queue)
        {
            GameRate = game; CommitRate = commits; JsRate = js;
            FrameMs = frame; WorstFrameMs = worst; WorkerMs = worker; ApplyMs = apply; QueueMs = queue;
        }
    }
}
