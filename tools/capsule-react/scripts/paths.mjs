import { resolve } from 'node:path';
import { packageRoot, sourceRoot } from './package-paths.mjs';
export { packageRoot, sourceRoot };
export const repositoryRoot = resolve(packageRoot, '../..');
export const localRoot = resolve(repositoryRoot, '.local');
export const sourcePath = (name) => resolve(sourceRoot, name);
export const repositoryPath = (name) => resolve(repositoryRoot, name);
