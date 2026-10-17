// Bounded per-addon scheduler. No browser, Node or host timer references escape.
let origin = Date.now();
let time = 0,
  next = 1;
const tasks = new Map<number, { at: number; callback: () => void }>();
// Optional trusted browser-carrier notification, captured before addon evaluation.
const wake = (globalThis as any).__portableWakeV1 as (() => void) | undefined;
export function wakeHost() {
  wake?.();
}
function now() {
  time = Math.max(time, Date.now() - origin);
  return time;
}
(globalThis as any).performance = { now: () => time };
(globalThis as any).setTimeout = (callback: () => void, delay = 0) => {
  if (tasks.size >= 64) throw Error('Scheduled callback quota exceeded');
  if (!Number.isFinite(delay)) throw Error('Timer delay must be finite');
  const id = next++;
  tasks.set(id, {
    at: now() + Math.min(2147483646, Math.max(0, delay)),
    callback,
  });
  wakeHost();
  return id;
};
(globalThis as any).clearTimeout = (id: number) => tasks.delete(id);
export function pump() {
  now();
  const ready = [...tasks].filter(([, task]) => task.at <= time).slice(0, 16);
  for (const [id, task] of ready) {
    if (tasks.get(id) !== task) continue;
    tasks.delete(id);
    task.callback();
  }
}
export function clearTasks() {
  tasks.clear();
}
// Private SDK/carrier scheduling contract v1. -1 means no pending timer.
export function nextDelay() {
  const current = now();
  let at = Infinity;
  for (const task of tasks.values()) at = Math.min(at, task.at);
  return at === Infinity
    ? -1
    : Math.min(2147483646, Math.max(0, Math.ceil(at - current)));
}
(globalThis as any).__portableNextDelayV1 = nextDelay;
// Private trusted-snapshot seam, called before any addon code is evaluated.
// Refuse an active realm rather than silently discarding its scheduled work.
(globalThis as any).__portableRestoreSchedulerV1 = () => {
  if (tasks.size) throw Error('Cannot restore a scheduler with pending work');
  origin = Date.now();
  time = 0;
  next = 1;
};
