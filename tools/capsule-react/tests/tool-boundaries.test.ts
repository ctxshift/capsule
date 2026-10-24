import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from 'vitest';
import { packageRoot, repositoryPath } from '../scripts/paths.mjs';

test('Capsule tooling does not load Host policy or game contracts', () => {
  for (const directory of ['src', 'scripts']) {
    const root = join(packageRoot, directory);
    for (const file of readdirSync(root, {
      recursive: true,
      encoding: 'utf8',
    })) {
      if (!/\.(ts|tsx|mjs)$/.test(file)) continue;
      const source = readFileSync(join(root, file), 'utf8');
      expect(source, file).not.toMatch(
        /@host\/|host-game-api|addons\/host-ui|dev\/addon-workshop\.json|hostThemeLimits/,
      );
    }
  }
  const manifest = JSON.parse(
    readFileSync(join(packageRoot, 'package.json'), 'utf8'),
  );
  expect(Object.keys(manifest.dependencies)).not.toContain('@host/game-api');
  const project = readFileSync(
    repositoryPath('tools/capsule-package-tests/Capsule.Package.Tests.csproj'),
    'utf8',
  );
  expect(project).not.toMatch(/host-game-api|client\/Assets\/Host\/Addon/);
});
