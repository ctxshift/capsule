import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from 'vitest';
import { packageRoot, repositoryPath } from '../scripts/paths.mjs';

test('Capsule tooling imports only declared packages and portable test sources', () => {
  const manifest = JSON.parse(
    readFileSync(join(packageRoot, 'package.json'), 'utf8'),
  );
  const dependencies = new Set([
    ...Object.keys(manifest.dependencies),
    ...Object.keys(manifest.devDependencies),
  ]);
  for (const directory of ['src', 'scripts']) {
    const root = join(packageRoot, directory);
    for (const file of readdirSync(root, {
      recursive: true,
      encoding: 'utf8',
    })) {
      if (!/\.(ts|tsx|mjs)$/.test(file)) continue;
      const source = readFileSync(join(root, file), 'utf8');
      const imports = source.matchAll(
        /(?:from\s*|import\s*\(\s*|import\s*)['"]([^'"]+)['"]/g,
      );
      for (const [, specifier] of imports) {
        if (specifier.startsWith('.') || specifier.startsWith('node:'))
          continue;
        const name = specifier.startsWith('@')
          ? specifier.split('/').slice(0, 2).join('/')
          : specifier.split('/')[0];
        expect(dependencies.has(name), `${file}: ${specifier}`).toBe(true);
      }
    }
  }
  const project = readFileSync(
    repositoryPath('tools/capsule-package-tests/Capsule.Package.Tests.csproj'),
    'utf8',
  );
  expect(project).not.toMatch(/client[\\/]Assets/);
});
