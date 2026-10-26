import assert from 'node:assert/strict';
import { spawn, execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { createWriteStream } from 'node:fs';
import {
  cp,
  mkdir,
  readFile,
  writeFile,
  symlink,
  rename,
} from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { createServer } from 'node:net';
import { defineFeature, s } from '@capsule-sdk/features';
import { descriptor } from '@capsule-sdk/features/codegen';

const root = fileURLToPath(new URL('../../', import.meta.url));
const { values } = parseArgs({
  options: {
    godot: { type: 'string' },
    output: { type: 'string' },
    addon: { type: 'string' },
    providers: { type: 'boolean', default: false },
  },
});
if (!values.godot || !values.output)
  throw Error(
    'Usage: bun tools/capsule-godot/verify.ts --godot <executable> --output <fresh-proof-directory> [--addon <addon-directory>]',
  );
const output = resolve(values.output);
await mkdir(output); // A fresh evidence directory is mandatory.
const game = join(output, 'game');
const ui = join(output, 'external-ui');
await mkdir(game);
await cp(join(root, 'godot/tests'), join(game, 'tests'), { recursive: true });
await cp(join(root, 'godot/project.godot'), join(game, 'project.godot'));
await cp(join(root, 'godot/demo.tscn'), join(game, 'demo.tscn'));
if (values.addon) {
  execFileSync(
    'node',
    [
      join(root, 'tools/capsule-godot/install.mjs'),
      '--bundle',
      resolve(values.addon, '../..'),
      '--project',
      game,
    ],
    { windowsHide: true },
  );
} else {
  await cp(join(root, 'godot/addons/capsule'), join(game, 'addons/capsule'), {
    recursive: true,
  });
}
await cp(join(root, 'examples/portable-demo-game'), ui, {
  recursive: true,
  filter: (name) => !name.includes('node_modules'),
});
await mkdir(join(ui, 'node_modules/@capsule-sdk'), { recursive: true });
for (const [name, location] of Object.entries({
  react: 'packages/capsule-react',
  features: 'packages/capsule-features',
  workshop: 'packages/capsule-workshop',
  'react-tools': 'tools/capsule-react',
}))
  await symlink(
    join(root, location),
    join(ui, 'node_modules/@capsule-sdk', name),
    process.platform === 'win32' ? 'junction' : 'dir',
  );
await symlink(
  join(root, 'node_modules/react'),
  join(ui, 'node_modules/react'),
  process.platform === 'win32' ? 'junction' : 'dir',
);
const source = join(ui, 'src/main.tsx');
const themeFile = join(ui, 'theme.json');
let initialTheme = await readFile(themeFile, 'utf8');
if (values.providers) {
  const theme = JSON.parse(initialTheme);
  theme.assets[0].rotation = 90; // Preserve integer-only contract fields across Godot JSON admission.
  initialTheme = JSON.stringify(theme, null, 2);
  await writeFile(themeFile, initialTheme);
}
let initialSource = `import React from 'react';
import {Box,Button,Text,Image,render,useObservation} from '@capsule-sdk/react';
function App(){const count=useObservation(7,1,b=>new DataView(b.buffer,b.byteOffset,b.byteLength).getUint16(0,true),0);return <Box x={48} y={48} width={440} height={230} direction="column" gap={12}>
<Text width={420} height={28} style="paragraph">Capsule initial</Text>
<Image width={32} height={32} asset="neutral"/>
<Text width={420} height={28} style="paragraph">{\`Requests: \${count}\`}</Text>
<Button width={240} height={42} onActivate={e=>e.request(7,new Uint8Array([1,0]))}><Text width={220} height={24} style="paragraph">Send request</Text></Button>
</Box>}render(<App/>,{layout:'yoga'});`;
if (values.providers) {
  const energy = defineFeature({
    name: '@capsule-example/energy',
    version: '1.0.0',
    dependencies: {},
    states: {
      State: {
        schema: s.record('EnergyState', { count: s.u32 }),
        permission: 'energy.read',
        delivery: 'latest',
      },
    },
    requests: {
      Add: {
        input: s.record('EnergyAdd', { amount: s.u32 }),
        output: s.record('EnergyResult', { accepted: s.bool }),
        permission: 'energy.add',
      },
    },
  });
  const definition = descriptor(energy);
  await writeFile(join(game, 'provider.json'), JSON.stringify(definition));
  await writeFile(
    join(ui, 'feature.ts'),
    `import {defineFeature} from '@capsule-sdk/features';export const energy=defineFeature(${JSON.stringify(energy)});export const fingerprint=${JSON.stringify(definition.fingerprint)};`,
  );
  const manifest = JSON.parse(await readFile(join(ui, 'addon.json'), 'utf8'));
  manifest.features = [
    {
      name: energy.name,
      range: '1.0.*',
      contractVersion: '1.0.0',
      fingerprint: definition.fingerprint,
      permissions: ['energy.read', 'energy.add'],
      optional: false,
    },
  ];
  await writeFile(join(ui, 'addon.json'), JSON.stringify(manifest));
  initialSource = initialSource
    .replace(
      "import React from 'react';",
      "import React,{useSyncExternalStore} from 'react';import {createAddonFeatures} from '@capsule-sdk/features/addon';import {observe,requestReadOnly} from '@capsule-sdk/react';import {energy,fingerprint} from '../feature';const features=createAddonFeatures(observe,requestReadOnly);const connection=features.connect(energy,'1.0.*',fingerprint);const subscribe=(notify:()=>void)=>connection.subscribe('State',notify);",
    )
    .replace(
      'useObservation(7,1,b=>new DataView(b.buffer,b.byteOffset,b.byteLength).getUint16(0,true),0)',
      "(useSyncExternalStore(subscribe,()=>connection.snapshot('State')).value?.count??0)",
    )
    .replace(
      'e.request(7,new Uint8Array([1,0]))',
      "connection.request('Add',{amount:1},e)",
    );
}
await writeFile(source, initialSource);
const portProbe = createServer();
await new Promise<void>((done) => portProbe.listen(0, '127.0.0.1', done));
const available = portProbe.address();
assert(available && typeof available === 'object');
await new Promise<void>((done) => portProbe.close(() => done()));
await writeFile(
  join(ui, 'capsule.project.json'),
  JSON.stringify({
    schemaVersion: 1,
    id: 'demo.starship',
    source: 'src/main.tsx',
    theme: 'theme.json',
    port: available.port,
  }),
);
const endpoint = `http://127.0.0.1:${available.port}`;
const sleep = (ms: number) => new Promise((done) => setTimeout(done, ms));
async function waitFor<T>(
  label: string,
  check: () => Promise<T | false>,
  milliseconds = 20000,
): Promise<T> {
  const deadline = Date.now() + milliseconds;
  while (Date.now() < deadline) {
    const result = await check();
    if (result) return result;
    await sleep(75);
  }
  throw Error(`Timed out: ${label}`);
}
let serverSequence = 0;
async function startServer() {
  const child = spawn(
    process.execPath,
    [
      join(root, 'packages/capsule-workshop/src/project-cli.ts'),
      'dev',
      '--project',
      ui,
      '--watch-stdin',
    ],
    { windowsHide: true },
  );
  const log = createWriteStream(
    join(output, `workshop-${++serverSequence}.log`),
  );
  child.stdout!.pipe(log, { end: false });
  child.stderr!.pipe(log, { end: false });
  const exited = new Promise<number | null>((done) =>
    child.once('close', (code) => {
      log.end();
      done(code);
    }),
  );
  async function close() {
    if (child.exitCode !== null) return;
    child.stdin!.end();
    const code = await Promise.race([
      exited,
      sleep(6000).then(() => 'timeout'),
    ]);
    if (code === 'timeout') {
      child.kill();
      throw Error('Workshop did not shut down.');
    }
    assert.equal(code, 0, 'Workshop clean shutdown');
  }
  try {
    await waitFor('workshop ready', async () => {
      try {
        const response = await fetch(endpoint + '/capsule/dev/v1/status', {
          signal: AbortSignal.timeout(1000),
        });
        const value = (await response.json()) as { ready: boolean };
        return response.ok && value.ready;
      } catch {
        return false;
      }
    });
    return { close };
  } catch (error) {
    await close();
    throw error;
  }
}
let workshop = await startServer();
type Status = {
  pid: number;
  revision: string;
  generation: string;
  error: string;
  requests: number;
  texts: string[];
  nodes: number;
  command: number;
  elapsedMs: number;
  imageColor: number[];
  textColor: number[];
};
async function status(): Promise<Status | null> {
  try {
    return JSON.parse(await readFile(join(output, 'status.json'), 'utf8'));
  } catch {
    return null;
  }
}
let processExit: Promise<number | null> | undefined;
let player: ReturnType<typeof spawn> | undefined;
const steps: unknown[] = [];
async function sendCommand(sequence: number, action: string) {
  const temporary = join(output, 'command.json.tmp');
  await writeFile(temporary, JSON.stringify({ sequence, action }));
  await rename(temporary, join(output, 'command.json'));
}
try {
  if (values.providers) {
    const publication = await (
      await fetch(endpoint + '/capsule/dev/v1/package')
    ).json();
    const style = publication.theme.text[0];
    for (let index = 0; index < 80; index++)
      publication.theme.text.push({ ...style, key: `neutral-font-${index}` });
    await writeFile(
      join(game, 'theme-admission.json'),
      JSON.stringify(publication),
    );
  }
  const importer = spawn(
    values.godot,
    [
      '--editor',
      '--path',
      game,
      '--import',
      '--resolution',
      '800x480',
      '--position',
      '60,60',
    ],
    { windowsHide: true, stdio: 'inherit' },
  );
  assert.equal(
    await new Promise((done) => importer.once('close', done)),
    0,
    'Fresh project import',
  );
  if (values.providers) {
    for (const fixture of ['feature_codec', 'images', 'theme_admission']) {
      const codec = spawn(
        values.godot,
        ['--headless', '--path', game, '--script', `res://tests/${fixture}.gd`],
        { windowsHide: true, stdio: 'inherit' },
      );
      assert.equal(
        await new Promise((done) => codec.once('close', done)),
        0,
        `${fixture} conformance`,
      );
    }
  }
  player = spawn(
    values.godot,
    [
      '--path',
      game,
      '--resolution',
      '800x480',
      '--position',
      '60,60',
      '--script',
      values.providers
        ? 'res://tests/providers.gd'
        : 'res://tests/development.gd',
      '--',
      `--endpoint=${endpoint}`,
      `--proof-dir=${output}`,
    ],
    { windowsHide: true },
  );
  const playerLog = createWriteStream(join(output, 'player.log'));
  player.stdout!.pipe(playerLog, { end: false });
  player.stderr!.pipe(playerLog, { end: false });
  processExit = new Promise((done) =>
    player!.once('close', (code) => {
      playerLog.end(() => done(code));
    }),
  );
  const first = await waitFor(
    'first complete UI',
    async () => {
      const s = await status();
      return s?.revision &&
        s.texts.includes('Capsule initial') &&
        s.texts.includes('Requests: 0')
        ? s
        : false;
    },
    45000,
  );
  // The Windows console launcher owns a separate Godot game process.
  assert(Number.isSafeInteger(first.pid) && first.pid > 0);
  steps.push({ step: 'initial', ...first });
  let sequence = 0;
  async function command(action: string) {
    const id = ++sequence;
    await sendCommand(id, action);
    if (action !== 'exit')
      await waitFor(action, async () => {
        const s = await status();
        return s?.command === id ? s : false;
      });
  }
  await command('click');
  const clicked = await waitFor('native input/action/observation', async () => {
    const s = await status();
    return s?.requests === 1 && s.texts.includes('Requests: 1') ? s : false;
  });
  steps.push({ step: 'input', ...clicked });
  const savedAt = Date.now();
  await writeFile(
    source,
    initialSource
      .replace('Capsule initial', 'Capsule reloaded')
      .replace('width={440}', 'width={480}'),
  );
  const replacement = await waitFor('TSX replacement', async () => {
    const s = await status();
    return s?.revision &&
      s.revision !== first.revision &&
      s.texts.includes('Capsule reloaded') &&
      s.texts.includes('Requests: 1')
      ? s
      : false;
  });
  assert.equal(replacement.pid, first.pid);
  steps.push({
    step: 'tsx-reload',
    saveToObservedMs: Date.now() - savedAt,
    ...replacement,
  });
  const theme = JSON.parse(initialTheme);
  theme.text[0].color = [255, 210, 90, 255];
  await writeFile(themeFile, JSON.stringify(theme));
  const themed = await waitFor('theme replacement', async () => {
    const s = await status();
    return s?.revision && s.revision !== replacement.revision && !s.error
      ? s
      : false;
  });
  assert.deepEqual(themed.textColor, [255, 210, 90, 255]);
  steps.push({ step: 'theme-reload', ...themed });
  await command('create-image');
  await cp(
    join(output, 'changed-image.png'),
    join(ui, 'assets/images/neutral.png'),
  );
  const imaged = await waitFor('image replacement', async () => {
    const s = await status();
    return s?.revision && s.revision !== themed.revision && !s.error
      ? s
      : false;
  });
  assert.deepEqual(imaged.imageColor, [255, 0, 0, 255]);
  steps.push({ step: 'image-reload', ...imaged });
  await command('capture');
  await writeFile(source, 'export const invalid = <Box>');
  const rejected = await waitFor('invalid TSX diagnostic', async () => {
    const s = await status();
    return s?.error ? s : false;
  });
  assert.equal(rejected.revision, imaged.revision);
  await command('click');
  const retained = await waitFor('last-good remains interactive', async () => {
    const s = await status();
    return s?.requests === 2 && s.texts.includes('Requests: 2') ? s : false;
  });
  assert.equal(retained.revision, imaged.revision);
  steps.push({ step: 'invalid-source-last-good', ...retained });
  await writeFile(
    source,
    initialSource.replace('Capsule initial', 'Capsule recovered'),
  );
  const recovered = await waitFor('source recovery', async () => {
    const s = await status();
    return s?.texts.includes('Capsule recovered') && !s.error ? s : false;
  });
  await writeFile(themeFile, '{}');
  const badTheme = await waitFor('invalid theme diagnostic', async () => {
    const s = await status();
    return s?.error ? s : false;
  });
  assert.equal(badTheme.revision, recovered.revision);
  await writeFile(themeFile, initialTheme);
  const final = await waitFor('theme recovery', async () => {
    const s = await status();
    return s?.revision && s.revision !== recovered.revision && !s.error
      ? s
      : false;
  });
  assert.equal(final.pid, first.pid);
  steps.push({ step: 'theme-recovery', ...final });
  const sdkFile = join(ui, '.capsule/sdk.js');
  const originalSdk = await readFile(sdkFile, 'utf8');
  await writeFile(
    sdkFile,
    originalSdk + '\n// Deliberate compatibility rejection fixture.\n',
  );
  const incompatible = await waitFor(
    'SDK compatibility rejection',
    async () => {
      const s = await status();
      return s?.error.includes('SDK compatibility') ? s : false;
    },
  );
  assert.equal(incompatible.revision, final.revision);
  await writeFile(sdkFile, originalSdk);
  await waitFor('SDK compatibility recovery', async () => {
    const s = await status();
    return s?.revision === final.revision && !s.error ? s : false;
  });
  steps.push({ step: 'sdk-rejection-last-good', ...incompatible });
  await workshop.close();
  const disconnected = await waitFor('workshop disconnect', async () => {
    const s = await status();
    return s?.error.includes('disconnected') ? s : false;
  });
  assert.equal(disconnected.revision, final.revision);
  await command('click');
  await waitFor('last-good input while disconnected', async () => {
    const s = await status();
    return s?.requests === 3 ? s : false;
  });
  workshop = await startServer();
  const reconnected = await waitFor('workshop reconnect', async () => {
    const s = await status();
    return s?.revision && !s.error && s.texts.includes('Requests: 3')
      ? s
      : false;
  });
  assert.equal(reconnected.pid, first.pid);
  steps.push({ step: 'reconnected', ...reconnected });
  await command('exit').catch(() => {});
  assert.equal(await processExit, 0, 'Godot clean shutdown');
  const log = await readFile(join(output, 'player.log'), 'utf8');
  assert(
    !/SCRIPT ERROR|^ERROR:|Unhandled exception|leaked at exit/m.test(log),
    log,
  );
  const png = await readFile(join(output, 'capture.png'));
  assert(png.length > 1000);
  await writeFile(
    join(output, 'report.json'),
    JSON.stringify(
      {
        passed: true,
        engine: 'Godot',
        steps,
        captureSha256: createHash('sha256').update(png).digest('hex'),
      },
      null,
      2,
    ),
  );
  console.log(JSON.stringify({ passed: true, steps: steps.length, output }));
} finally {
  if (player && player.exitCode === null) {
    await sendCommand(2147483647, 'exit');
    await Promise.race([processExit, sleep(5000)]);
    if (player.exitCode === null) {
      if (process.platform === 'win32') {
        execFileSync('taskkill', ['/PID', String(player.pid), '/T', '/F'], {
          windowsHide: true,
        });
      } else {
        player.kill();
      }
    }
  }
  await workshop.close();
}
