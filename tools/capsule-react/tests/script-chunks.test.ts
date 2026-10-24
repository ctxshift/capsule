import { createContext, runInContext } from 'node:vm';
import { expect, test } from 'vitest';
import { scriptChunks } from '../../../packages/capsule-workshop/src/script-chunks.mjs';
import { compiler } from '@capsule-sdk/workshop/compiler';
import { mkdtemp, mkdir, writeFile, readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const file = (path: string, text: string) => ({ path, text });
test('compiler publishes declared chunks atomically and retains a good revision on failure', async () => {
  const root = await mkdtemp(join(tmpdir(), 'capsule-chunks-'));
  const projectRoot = join(root, 'source'),
    output = join(root, 'output');
  await mkdir(projectRoot);
  const source = join(projectRoot, 'main.ts'),
    manifest = join(projectRoot, 'addon.json');
  await writeFile(
    source,
    "import {value} from './shared';if(value!==42)throw Error('wrong');",
  );
  await writeFile(join(projectRoot, 'shared.ts'), 'export const value=42;');
  await writeFile(
    manifest,
    JSON.stringify({
      schemaVersion: 1,
      apiVersion: 1,
      id: 'modules.fixture',
      entry: 'main.ts',
      assets: [],
      scriptEntries: ['shared.ts'],
    }),
  );
  const worker = compiler({ source, manifest, projectRoot, output });
  expect((await worker.compile()).success).toBe(true);
  const good = await readFile(join(output, 'addon.json'), 'utf8');
  const published = JSON.parse(good);
  const context = createContext({});
  expect(published.assets.length).toBeGreaterThan(1);
  for (const asset of published.assets) {
    expect(asset.kind).toBe('script');
    runInContext(await readFile(join(output, asset.path), 'utf8'), context);
  }
  runInContext(await readFile(join(output, published.entry), 'utf8'), context);
  await writeFile(
    join(projectRoot, 'shared.ts'),
    `export const value=${JSON.stringify('x'.repeat(65536))};`,
  );
  expect((await worker.compile()).success).toBe(false);
  expect(await readFile(join(output, 'addon.json'), 'utf8')).toBe(good);
});
test('chunks share exports and execute once in a fresh session', async () => {
  const result = await scriptChunks([
    file(
      'entry.js',
      "import {value} from './shared.js'; import './other.js';globalThis.result=value;",
    ),
    file(
      'shared.js',
      'globalThis.calls=(globalThis.calls??0)+1;export const value=42;',
    ),
    file(
      'other.js',
      "import {value} from './shared.js';if(value!==42)throw Error('wrong');",
    ),
  ]);
  for (let round = 0; round < 2; round++) {
    const context = createContext({});
    for (const chunk of result.chunks)
      runInContext(chunk.contents.toString(), context);
    expect(context.calls).toBeUndefined();
    runInContext(result.bundle.contents.toString(), context);
    expect(context.result).toBe(42);
    expect(context.calls).toBe(1);
    expect(context.__capsuleFactories).toBeUndefined();
  }
});
test('chunks reject excessive count, source bytes and undeclared imports', async () => {
  await expect(
    scriptChunks(
      Array.from({ length: 9 }, (_, i) => file(`${i}.js`, 'void 0')),
    ),
  ).rejects.toThrow('count');
  await expect(
    scriptChunks([
      file('entry.js', `globalThis.value=${JSON.stringify('x'.repeat(65536))}`),
    ]),
  ).rejects.toThrow('64 KiB');
  await expect(
    scriptChunks(
      Array.from({ length: 5 }, (_, i) =>
        file(
          `${i}.js`,
          `globalThis.value=${JSON.stringify('x'.repeat(60000))}`,
        ),
      ),
    ),
  ).rejects.toThrow('256 KiB');
  const result = await scriptChunks([
    file('entry.js', "import './missing.js'"),
  ]);
  const context = createContext({});
  for (const chunk of result.chunks)
    runInContext(chunk.contents.toString(), context);
  expect(() =>
    runInContext(result.bundle.contents.toString(), context),
  ).toThrow('Undeclared module');
});
