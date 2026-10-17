import { resolve } from 'node:path';
import { readFileSync } from 'node:fs';
import { packageRoot, sourceRoot } from './package-paths.mjs';

export { packageRoot, sourceRoot };
export const repositoryRoot = resolve(packageRoot, '../..');
export const workshopConfig = JSON.parse(
  readFileSync(resolve(repositoryRoot, 'dev/addon-workshop.json'), 'utf8'),
);
export const examplesRoot = resolve(
  repositoryRoot,
  workshopConfig.defaultAddon,
);
export const localRoot = resolve(repositoryRoot, '.local');
export const sourcePath = (name) => resolve(sourceRoot, name);
export const examplePath = (name) => {
  const path = workshopConfig.fixtures[name];
  if (!path) throw Error('Unknown configured fixture: ' + name);
  return resolve(repositoryRoot, path);
};
export const repositoryPath = (name) => resolve(repositoryRoot, name);
// Host's typed contracts are an explicit source dependency of its core addon.
export const hostWatchRoots = [
  sourceRoot,
  repositoryPath('packages/host-game-api/src'),
  repositoryPath('packages/rpg-features/src'),
  repositoryPath('packages/mmo-features/src'),
  repositoryPath('packages/skill-system-features/src'),
  repositoryPath('packages/host-game-api/generated'),
  repositoryPath('packages/capsule-features/src'),
  repositoryPath('packages/inventory-feature/src'),
];
