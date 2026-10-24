import { build } from 'esbuild';
import { resolve } from 'node:path';

const output = process.argv[2];
if (!output) throw new Error('Provide a fixture output path');

await build({
  entryPoints: [
    resolve(import.meta.dirname, '../fixtures/feature-recovery.ts'),
  ],
  outfile: resolve(output),
  bundle: true,
  platform: 'browser',
  format: 'iife',
  target: 'es2020',
});
