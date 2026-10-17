import { test, expect } from 'vitest';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { compiler } from '../scripts/dev.mjs';
import { featureRequirements } from '../../../packages/capsule-workshop/src/feature-requirements.mjs';

const requirement = {
  name: '@example/inventory',
  range: '1.2.*',
  contractVersion: '1.2.5',
  fingerprint: 'a'.repeat(64),
  optional: false,
  permissions: ['inventory.read'],
};
test('strict versioned feature requirement metadata', () => {
  expect(featureRequirements(undefined)).toEqual([]);
  expect(featureRequirements([requirement])).toEqual([requirement]);
  for (const value of [
    null,
    {},
    [requirement, requirement],
    [{ ...requirement, optional: 1 }],
    [{ ...requirement, range: '^1.2' }],
    [{ ...requirement, range: '1.3.*' }],
    [{ ...requirement, fingerprint: 'bad' }],
    [{ ...requirement, permissions: ['inventory.read', 'inventory.read'] }],
    [{ ...requirement, extra: true }],
  ])
    expect(() => featureRequirements(value)).toThrow();
});
test('compiler retains requirements and preserves last good on invalid requirements', async () => {
  const root = await mkdtemp(resolve('.local/feature-manifest-'));
  try {
    const source = join(root, 'source');
    await mkdir(source);
    await writeFile(
      join(source, 'app.tsx'),
      "import React from 'react';import {Box,render} from '@capsule/react';render(<Box width={10} height={10}/>);",
    );
    const manifest = {
      schemaVersion: 1,
      apiVersion: 1,
      id: 'example.features',
      entry: 'app.tsx',
      assets: [],
      features: [requirement],
    };
    const path = join(source, 'addon.json');
    await writeFile(path, JSON.stringify(manifest));
    const output = join(root, 'package');
    const worker = compiler({
      source: join(source, 'app.tsx'),
      output,
      projectRoot: source,
      manifest: path,
    });
    const first = await worker.compile();
    expect(first.success, first.error).toBe(true);
    const before = await readFile(join(output, 'addon.json'), 'utf8');
    expect(JSON.parse(before).features).toEqual([requirement]);
    await writeFile(
      path,
      JSON.stringify({
        ...manifest,
        features: [{ ...requirement, range: '*' }],
      }),
    );
    expect((await worker.compile()).success).toBe(false);
    expect(await readFile(join(output, 'addon.json'), 'utf8')).toBe(before);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});
