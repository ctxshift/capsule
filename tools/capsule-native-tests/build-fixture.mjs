import { resolve } from 'node:path';
import { compiler } from '@capsule-sdk/workshop/compiler';

const output = process.argv[2];
if (!output) throw new Error('Provide a fresh fixture output directory');

const projectRoot = resolve(import.meta.dirname, 'fixture');
const result = await compiler({
  source: resolve(projectRoot, 'main.tsx'),
  projectRoot,
  manifest: resolve(projectRoot, 'addon.json'),
  output: resolve(output),
}).compile();

if (!result.success)
  throw new Error(result.error || 'Neutral native fixture compilation failed');
console.log(`Neutral native fixture compiled: ${resolve(output)}`);
