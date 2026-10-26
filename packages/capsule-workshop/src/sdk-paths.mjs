import { createRequire } from 'node:module';
import { dirname, resolve } from 'node:path';

const require = createRequire(import.meta.url);
export const capsuleEntry = require.resolve('@capsule-sdk/react');
export const capsuleSourceRoot = dirname(capsuleEntry);
export const featureSourceRoot = dirname(
  require.resolve('@capsule-sdk/features'),
);
export const reactTypes = resolve(
  dirname(require.resolve('@types/react/package.json')),
  'index.d.ts',
);
