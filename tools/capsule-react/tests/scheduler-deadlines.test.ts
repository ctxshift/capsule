import { test } from 'vitest';
import { build } from 'esbuild';
import vm from 'node:vm';
import assert from 'node:assert/strict';
test('bounded scheduler deadlines and cancellation', async () => {
  const built = await build({
    entryPoints: [
      new URL('../src/scheduler.ts', import.meta.url).pathname.slice(1),
    ],
    bundle: true,
    write: false,
    format: 'iife',
    globalName: 'scheduler',
  });
  let clock = 1000;
  let wakes = 0;
  const context = vm.createContext({
    Date: { now: () => clock },
    __portableWakeV1: () => wakes++,
  });
  vm.runInContext(built.outputFiles[0].text, context);
  const run = (source: string) => vm.runInContext(source, context);
  run('globalThis.startup=0;setTimeout(()=>startup++,100)');
  assert.equal(wakes, 1);
  run('scheduler.pump()');
  assert.equal(run('startup'), 0);
  clock += 99;
  run('scheduler.pump()');
  assert.equal(run('startup'), 0);
  clock++;
  run('scheduler.pump()');
  assert.equal(run('startup'), 1);
  assert.equal(run('scheduler.nextDelay()'), -1);
  const beforeAsync = wakes;
  await run('Promise.resolve().then(()=>setTimeout(()=>{},0))');
  assert.equal(wakes, beforeAsync + 1);
  run('scheduler.pump()');
  run('globalThis.fired=0; globalThis.id=setTimeout(()=>fired++,100)');
  assert.equal(run('scheduler.nextDelay()'), 100);
  clock += 60;
  assert.equal(run('scheduler.nextDelay()'), 40);
  run('clearTimeout(id)');
  assert.equal(run('scheduler.nextDelay()'), -1);
  run('setTimeout(()=>{fired++;setTimeout(()=>fired++,30)},10)');
  clock += 10;
  run('scheduler.pump()');
  assert.equal(run('fired'), 1);
  assert.equal(run('scheduler.nextDelay()'), 30);
  clock += 30;
  run('scheduler.pump()');
  assert.equal(run('fired'), 2);
  assert.equal(run('scheduler.nextDelay()'), -1);
  run(
    'globalThis.second=0; setTimeout(()=>clearTimeout(second),0);second=setTimeout(()=>fired+=100,0)',
  );
  run('scheduler.pump()');
  assert.equal(run('fired'), 2);
  assert.throws(() => run('setTimeout(()=>{},Infinity)'), /finite/);
  run('setTimeout(()=>{},2147483647000)');
  assert.equal(run('scheduler.nextDelay()'), 2147483646);
  run('scheduler.clearTasks()');
  assert.equal(run('scheduler.nextDelay()'), -1);
  clock += 86400000;
  run('__portableRestoreSchedulerV1()');
  assert.equal(run('performance.now()'), 0);
  run('setTimeout(()=>fired++,100)');
  assert.equal(run('scheduler.nextDelay()'), 100);
  assert.throws(() => run('__portableRestoreSchedulerV1()'), /pending work/);
  clock += 100;
  run('scheduler.pump()');
  assert.equal(run('fired'), 3);
  assert.equal(run('performance.now()'), 100);
  console.log(
    'PASS scheduler deadlines: none/set/cancel/fire/reschedule/callback cancellation/finite bounds',
  );
});
