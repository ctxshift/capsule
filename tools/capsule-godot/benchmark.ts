import assert from 'node:assert/strict';
import { spawn, execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { createWriteStream } from 'node:fs';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { writeCommand } from './commands';
import { createServer } from 'node:http';
import { join, resolve } from 'node:path';
import { parseArgs } from 'node:util';

const { values } = parseArgs({
  options: {
    godot: { type: 'string' },
    proof: { type: 'string' },
    project: { type: 'string' },
    output: { type: 'string' },
    samples: { type: 'string', default: '20' },
    budget: { type: 'string', default: '300' },
    'startup-budget': { type: 'string' },
    script: { type: 'string', default: 'res://tests/providers.gd' },
    width: { type: 'string', default: '800' },
    height: { type: 'string', default: '480' },
    'cold-images': { type: 'boolean', default: false },
  },
});
assert(
  values.godot && values.proof && values.output,
  'Required: --godot <executable> --proof <verified-proof-directory> --output <fresh-directory>',
);
const count = Number(values.samples),
  budget = Number(values.budget);
const startupBudget = Number(values['startup-budget'] ?? values.budget);
assert(startupBudget > 0);
const width = Number(values.width),
  height = Number(values.height);
assert(
  Number.isInteger(width) &&
    Number.isInteger(height) &&
    width > 0 &&
    height > 0 &&
    width <= 8192 &&
    height <= 8192,
);
assert(Number.isInteger(count) && count >= 1 && count <= 100 && budget > 0);
const proof = resolve(values.proof),
  output = resolve(values.output),
  game = values.project ? resolve(values.project) : join(proof, 'game');
await readFile(join(game, 'project.godot'));
await mkdir(output);
const first = JSON.parse(
  await readFile(join(proof, 'publication-initial.json'), 'utf8'),
);
const second = JSON.parse(
  await readFile(join(proof, 'publication-reloaded.json'), 'utf8'),
);
assert(first.hash !== second.hash && first.sdkSha256 === second.sdkSha256);
const images = new Map<string, Buffer>();
for (const asset of first.theme.assets) {
  const key: string = asset.image ?? asset.key;
  assert(/^[a-z0-9-]{1,64}$/.test(key));
  if (!images.has(key))
    images.set(key, await readFile(join(proof, 'replay-images', `${key}.png`)));
  for (const publication of [first, second])
    assert.equal(
      createHash('sha256').update(images.get(key)!).digest('hex'),
      publication.imageHashes[key],
    );
}
const nativePackets = [first, second].map(
  ({ bundle: _bundle, sdk: _sdk, ...packet }) =>
    Buffer.from(JSON.stringify(packet)),
);
let selected = 0;
const server = createServer((request, response) => {
  const path = request.url?.split('?')[0];
  if (path === '/capsule/dev/v1/package') {
    response.setHeader('Content-Type', 'application/json');
    response.end(nativePackets[selected]);
    return;
  }
  const match = /^\/portable-ui\/art\/([a-f0-9]{64})\/([a-z0-9-]+)$/.exec(
    path ?? '',
  );
  if (
    match &&
    [first.hash, second.hash].includes(match[1]) &&
    images.has(match[2])
  ) {
    response.setHeader('Content-Type', 'image/png');
    response.end(images.get(match[2]));
    return;
  }
  response.statusCode = 404;
  response.end();
});
server.on('connection', (socket) => socket.setNoDelay(true));
await new Promise<void>((done) => server.listen(0, '127.0.0.1', done));
const address = server.address();
assert(address && typeof address === 'object');
const endpoint = `http://127.0.0.1:${address.port}`;
const sleep = (ms: number) => new Promise((done) => setTimeout(done, ms));
type Metric = {
  revision: string;
  candidate: boolean;
  frameAtTicksUs: number;
  presentedAtUnixMs: number;
  hostToFrameMs: number;
  enginePreloadMs: number;
  native: { workerMs: number };
};
type Status = {
  pid: number;
  error: string;
  startedAtTicksMs: number;
  presentation: Metric[];
};
const samples: {
  startupMs: number;
  reloadMs: number;
  enginePreloadMs: number;
  processToFrameMs: number;
  initialWorkerMs: number;
  reloadWorkerMs: number;
}[] = [];
try {
  for (let index = 0; index < count; index++) {
    selected = 0;
    const run = join(output, `run-${String(index + 1).padStart(3, '0')}`);
    await mkdir(run);
    const launched = Date.now();
    const child = spawn(
      values.godot,
      [
        '--path',
        game,
        '--resolution',
        `${width}x${height}`,
        '--position',
        '60,60',
        '--script',
        values.script!,
        '--',
        `--endpoint=${endpoint}`,
        `--proof-dir=${run}`,
        '--latency-only',
      ],
      { windowsHide: true },
    );
    const log = createWriteStream(join(run, 'player.log'));
    child.stdout.pipe(log, { end: false });
    child.stderr.pipe(log, { end: false });
    const exited = new Promise<number | null>((done) =>
      child.once('close', (code) => {
        log.end();
        done(code);
      }),
    );
    async function presented(revision: string) {
      const deadline = Date.now() + 15000;
      while (Date.now() < deadline) {
        let status: Status | undefined;
        try {
          status = JSON.parse(await readFile(join(run, 'status.json'), 'utf8'));
        } catch {
          /* Atomic status writer may be between frames. */
        }
        assert(!status?.error, status?.error);
        const metric = status?.presentation?.find(
          (item) => item.candidate && item.revision === revision,
        );
        if (status && metric) return { status, metric };
        assert(
          child.exitCode === null,
          'Godot exited before presenting the publication',
        );
        await sleep(3);
      }
      throw Error('Timed out waiting for a complete frame');
    }
    async function stop() {
      if (child.exitCode !== null) return;
      await writeCommand(run, 2147483647, 'exit');
      await Promise.race([exited, sleep(5000)]);
      if (child.exitCode === null) {
        if (process.platform === 'win32')
          execFileSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], {
            windowsHide: true,
          });
        else child.kill();
        throw Error('Godot required forced shutdown');
      }
    }
    try {
      const initial = await presented(first.hash);
      if (values['cold-images']) {
        await writeCommand(run, 1, 'clear-image-cache');
        const deadline = Date.now() + 5000;
        let cleared = false;
        while (Date.now() < deadline) {
          try {
            const status = JSON.parse(
              await readFile(join(run, 'status.json'), 'utf8'),
            );
            cleared = status.command === 1;
          } catch {
            /* Wait for atomic status. */
          }
          if (cleared) break;
          await sleep(3);
        }
        assert(cleared, 'Image cache reset was not acknowledged');
      }
      const ready = Date.now();
      selected = 1;
      const reload = await presented(second.hash);
      assert.equal(reload.status.pid, initial.status.pid);
      samples.push({
        startupMs:
          initial.metric.frameAtTicksUs / 1000 -
          initial.status.startedAtTicksMs,
        reloadMs: reload.metric.presentedAtUnixMs - ready,
        enginePreloadMs: initial.metric.enginePreloadMs,
        processToFrameMs: initial.metric.presentedAtUnixMs - launched,
        initialWorkerMs: initial.metric.native.workerMs,
        reloadWorkerMs: reload.metric.native.workerMs,
      });
      await stop();
      assert.equal(await exited, 0);
      assert(
        !/SCRIPT ERROR|^ERROR:|Unhandled exception|leaked at exit/m.test(
          await readFile(join(run, 'player.log'), 'utf8'),
        ),
      );
    } finally {
      await stop();
    }
  }
  function distribution(
    key: 'startupMs' | 'reloadMs' | 'enginePreloadMs' | 'processToFrameMs',
  ) {
    const sorted = samples.map((sample) => sample[key]).sort((a, b) => a - b);
    const percentile = (fraction: number) =>
      Math.round(sorted[Math.ceil(sorted.length * fraction) - 1] * 100) / 100;
    return {
      min: sorted[0],
      p50: percentile(0.5),
      p95: percentile(0.95),
      max: sorted.at(-1),
    };
  }
  const report = {
    schemaVersion: 1,
    passed: samples.every(
      (sample) =>
        sample.startupMs <= startupBudget &&
        sample.reloadMs >= 0 &&
        sample.reloadMs < budget,
    ),
    budgetMs: budget,
    startupBudgetMs: startupBudget,
    startupPassed: samples.every((sample) => sample.startupMs <= startupBudget),
    reloadPassed: samples.every(
      (sample) => sample.reloadMs >= 0 && sample.reloadMs < budget,
    ),
    compilerDuringMeasurement: false,
    gpuReadbackDuringMeasurement: false,
    coldImageReload: values['cold-images'],
    timingBoundary:
      'SceneTree setup / ready publication to frame_post_draw; engine plugin initialization and full process launch reported separately',
    samples,
    startupMs: distribution('startupMs'),
    reloadMs: distribution('reloadMs'),
    enginePreloadMs: distribution('enginePreloadMs'),
    processToFrameMs: distribution('processToFrameMs'),
  };
  await writeFile(
    join(output, 'report.json'),
    JSON.stringify(report, null, 2) + '\n',
  );
  console.log(
    JSON.stringify({
      passed: report.passed,
      count,
      startupMs: report.startupMs,
      reloadMs: report.reloadMs,
      enginePreloadMs: report.enginePreloadMs,
    }),
  );
  assert(
    report.passed,
    'Complete-frame latency budget exceeded; see the retained report',
  );
} finally {
  server.closeAllConnections();
  await new Promise<void>((done) => server.close(() => done()));
}
