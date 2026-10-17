import { resolve } from 'node:path';

// Package-local paths must not load a game's workshop configuration.
export const packageRoot = resolve(import.meta.dirname, '..');
export const sourceRoot = resolve(packageRoot, 'src');
