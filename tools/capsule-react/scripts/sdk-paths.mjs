import { createRequire } from 'node:module';
import { dirname, resolve } from 'node:path';

const require = createRequire(import.meta.url);
export const capsuleEntry = require.resolve('@capsule/react');
export const capsuleSourceRoot = dirname(capsuleEntry);
export const reactTypes = resolve(
  dirname(require.resolve('@types/react/package.json')),
  'index.d.ts',
);
