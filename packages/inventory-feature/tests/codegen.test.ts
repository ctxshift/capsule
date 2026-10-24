import { expect, it } from 'vitest';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { descriptor, generateCSharp } from '@capsule/features/codegen';
import { inventory, items, inventoryCodec } from '../src/contracts';

it('keeps the example descriptors current', () => {
  const actual = JSON.parse(
    readFileSync(
      new URL('../generated/contracts.json', import.meta.url),
      'utf8',
    ),
  );
  expect(actual).toEqual([descriptor(items), descriptor(inventory)]);
});

it('keeps the generated native interfaces current', () => {
  const actual = readFileSync(
    new URL('../generated/InventoryFeature.Generated.cs', import.meta.url),
    'utf8',
  );
  expect(actual.replaceAll('\r\n', '\n')).toBe(
    generateCSharp('Capsule.InventoryFeature', {
      Items: items,
      Inventory: inventory,
    }),
  );
});

it('compiles a C# provider and agrees on bytes in both directions', () => {
  const value = {
    available: true,
    epoch: 7,
    revision: 9,
    entries: [
      { item: 'sword-é', container: 'bag', quantity: 2, transferable: true },
    ],
  };
  const base64 = Buffer.from(inventoryCodec.encode(value)).toString('base64');
  const project = fileURLToPath(
    new URL('native/ContractProof.csproj', import.meta.url),
  );
  const result = execFileSync(
    'dotnet',
    ['run', '--project', project, '--', base64],
    { encoding: 'utf8', timeout: 60000 },
  );
  expect(result).toContain('Native contract and cross-language fixture passed');
}, 65000);
