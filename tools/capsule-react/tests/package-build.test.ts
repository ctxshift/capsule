import { test, expect } from 'vitest';
import {
  cp,
  mkdtemp,
  mkdir,
  readFile,
  rm,
  symlink,
  writeFile,
} from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { createRequire } from 'node:module';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { buildSdk } from '../scripts/build.mjs';
import { packageRoot } from '../scripts/package-paths.mjs';

const run = promisify(execFile);
const require = createRequire(import.meta.url);

test('SDK builds with identical output outside the game checkout', async () => {
  const root = await mkdtemp(join(tmpdir(), 'capsule-sdk-'));
  try {
    const isolated = join(root, 'sdk');
    await mkdir(join(isolated, 'scripts'), { recursive: true });
    await cp(join(packageRoot, 'src'), join(isolated, 'src'), {
      recursive: true,
    });
    for (const name of ['build.mjs', 'package-paths.mjs']) {
      await cp(
        join(packageRoot, 'scripts', name),
        join(isolated, 'scripts', name),
      );
    }
    // Reuse installed dependencies only; no host paths/configuration are copied.
    await mkdir(join(isolated, 'node_modules'));
    await mkdir(join(isolated, 'node_modules', '@capsule'));
    await symlink(
      dirname(dirname(require.resolve('@capsule/react'))),
      join(isolated, 'node_modules', '@capsule', 'react'),
      'junction',
    );
    for (const name of ['esbuild', 'react', 'react-reconciler', 'scheduler']) {
      await symlink(
        dirname(require.resolve(name + '/package.json')),
        join(isolated, 'node_modules', name),
        'junction',
      );
    }
    await writeFile(
      join(isolated, 'build-test.mjs'),
      "import { buildSdk } from './scripts/build.mjs'; await buildSdk();",
    );
    await run(process.execPath, [join(isolated, 'build-test.mjs')], {
      cwd: isolated,
      windowsHide: true,
      timeout: 20000,
    });
    const expected = join(root, 'expected.js');
    await buildSdk({ output: expected });
    const actual = await readFile(
      join(isolated, '.local/react-host.js'),
      'utf8',
    );
    expect(actual.length).toBeGreaterThan(0);
    expect(actual).toBe(await readFile(expected, 'utf8'));
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});
