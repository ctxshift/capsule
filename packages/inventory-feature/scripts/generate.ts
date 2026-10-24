import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { descriptor, generateCSharp } from '@capsule/features/codegen';
import { items, inventory } from '../src/contracts';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const files = new Map([
  [
    'packages/inventory-feature/generated/contracts.json',
    JSON.stringify([descriptor(items), descriptor(inventory)], null, 2) + '\n',
  ],
  [
    'packages/inventory-feature/generated/InventoryFeature.Generated.cs',
    generateCSharp('Capsule.InventoryFeature', {
      Items: items,
      Inventory: inventory,
    }),
  ],
]);
for (const [path, content] of files) {
  const destination = resolve(root, path);
  if (process.argv.includes('--check')) {
    if (readFileSync(destination, 'utf8').replaceAll('\r\n', '\n') !== content)
      throw Error(`Generated contract drift: ${path}`);
  } else {
    mkdirSync(dirname(destination), { recursive: true });
    writeFileSync(destination, content);
  }
}
