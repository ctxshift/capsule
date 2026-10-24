import assert from 'node:assert/strict';
import {
  copyFile,
  mkdir,
  readFile,
  realpath,
  writeFile,
} from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { descriptor, generateCSharp } from '@capsule-sdk/features/codegen';
import { contracts as rpg } from '@capsule-sdk/rpg-features/contracts';
import { contracts as mmo } from '@capsule-sdk/mmo-features/contracts';
import { contracts as skills } from '@capsule-sdk/skill-system-features/contracts';
import { items, inventory } from '@capsule-sdk/example-inventory';
import { compiler } from '@capsule-sdk/workshop/compiler';
import { buildSdk } from '@capsule-sdk/react-tools/sdk';

const definitions = [
  ...Object.values(rpg),
  ...Object.values(mmo),
  ...Object.values(skills),
  items,
  inventory,
];
assert.equal(definitions.length, 16);
for (const feature of definitions)
  assert.match(descriptor(feature).fingerprint, /^[a-f0-9]{64}$/);
assert(
  generateCSharp('Consumer.Contracts', rpg).includes(
    'namespace Consumer.Contracts',
  ),
);
const root = dirname(fileURLToPath(import.meta.url));
const sdk = resolve(root, 'sdk.js');
await buildSdk({ output: sdk });
const fixture = dirname(
  await realpath(
    fileURLToPath(
      import.meta
        .resolve('@capsule-sdk/react-tools/fixtures/native/addon.json'),
    ),
  ),
);
const addon = resolve(root, 'addon');
await mkdir(addon);
for (const file of ['main.tsx', 'addon.json'])
  await copyFile(resolve(fixture, file), resolve(addon, file));
const worker = compiler({
  source: resolve(addon, 'main.tsx'),
  projectRoot: addon,
  manifest: resolve(addon, 'addon.json'),
  output: resolve(root, 'publication'),
});
assert.equal((await worker.compile()).success, true);
const first = await readFile(resolve(root, 'publication/bundle.js'));
const original = await readFile(resolve(addon, 'main.tsx'), 'utf8');
await writeFile(
  resolve(addon, 'main.tsx'),
  original.replace('width={240}', 'width="invalid"'),
);
assert.equal((await worker.compile()).success, false);
assert.deepEqual(await readFile(resolve(root, 'publication/bundle.js')), first);
await writeFile(resolve(addon, 'main.tsx'), original);
assert.equal((await worker.compile()).success, true);
console.log(
  'PASS packed consumer: 16 features, public codegen, SDK build, compiler and last-good recovery',
);
