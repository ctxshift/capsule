import { test, expect } from 'vitest';
import { mkdtemp, mkdir, readFile, writeFile, cp } from 'node:fs/promises';
import { join } from 'node:path';
import { createContext, runInContext } from 'node:vm';
import { compiler } from '@capsule/workshop/compiler';
import { buildSdk } from '../scripts/build.mjs';
import { repositoryPath, localRoot } from '../scripts/paths.mjs';
import { applyPacket, type UiNode } from '../src/browser/packet';

test('community package renders, handles a click and retains last-good on public type errors', async () => {
  const root = await mkdtemp(join(localRoot, 'capsule-consumer-'));
  const sourceRoot = join(root, 'addon');
  await mkdir(join(sourceRoot, 'src'), { recursive: true });
  const source = join(sourceRoot, 'src/main.tsx');
  await cp(
    repositoryPath('examples/addons/capsule-counter/src/main.tsx'),
    source,
  );
  const output = join(root, 'published');
  const worker = compiler({ source, output, projectRoot: sourceRoot });
  expect((await worker.compile()).success).toBe(true);
  const bundle = await readFile(join(output, 'bundle.js'), 'utf8');
  const sdk = join(root, 'sdk.js');
  await buildSdk({ output: sdk });
  let next = 0n;
  let nodes = new Map<bigint, UiNode>();
  const events: Uint8Array[] = [];
  const context = createContext({
    portableUi: {
      reserve: () => ++next,
      submit: (bytes: Uint8Array) => {
        nodes = applyPacket(nodes, bytes);
        return 0;
      },
      nextEvent: () => events.shift() ?? new Uint8Array(),
      nextObservation: () => new Uint8Array(),
      requestAction: () => {},
    },
  });
  runInContext(await readFile(sdk, 'utf8'), context);
  runInContext(bundle, context);
  runInContext('portableUiUpdate()', context);
  expect([...nodes.values()].some((node) => node.text === '0')).toBe(true);
  const button = [...nodes.values()].find((node) => node.events & 1)!;
  const click = Buffer.alloc(16);
  click.writeUInt16LE(1, 2);
  click.writeBigUInt64LE(button.id, 8);
  events.push(click);
  runInContext('portableUiUpdate()', context);
  expect([...nodes.values()].some((node) => node.text === '1')).toBe(true);
  const valid = await readFile(source, 'utf8');
  await writeFile(source, valid.replace('width={240}', 'width="invalid"'));
  const rejected = await worker.compile();
  expect(rejected.success).toBe(false);
  expect(rejected.error).toContain('main.tsx:');
  expect(await readFile(join(output, 'bundle.js'), 'utf8')).toBe(bundle);
});
