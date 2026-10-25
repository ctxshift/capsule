import { test, expect } from 'vitest';
import { execFileSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import {
  mkdir,
  mkdtemp,
  readFile,
  rm,
  symlink,
  writeFile,
} from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { packNpm } from '../../release/build.mjs';
import { extract } from '../../release/lib.mjs';
import { repositoryRoot, repositoryPath } from '../scripts/paths.mjs';

test('packed workshop exposes strict compiler and server types', async () => {
  const root = await mkdtemp(join(tmpdir(), 'capsule-workshop-types-'));
  try {
    const output = join(root, 'archives');
    const packed = packNpm(
      repositoryRoot,
      output,
      join(root, 'stages'),
      'types',
    );
    const dependencies = new Set<string>();
    for (const entry of packed) {
      const extracted = extract(
        join(output, entry.file.replace(/^npm\//, '')),
        join(root, 'extracted', entry.name.replaceAll(/[@/]/g, '-')),
      );
      const manifest = JSON.parse(
        await readFile(join(extracted, 'package.json'), 'utf8'),
      ) as {
        dependencies?: Record<string, string>;
        peerDependencies?: Record<string, string>;
      };
      for (const name of Object.keys({
        ...manifest.dependencies,
        ...manifest.peerDependencies,
      }))
        if (!name.startsWith('@capsule-sdk/')) dependencies.add(name);
      const link = join(root, 'node_modules', entry.name);
      await mkdir(dirname(link), { recursive: true });
      await symlink(extracted, link, 'junction');
    }
    dependencies.add('@types/node');
    dependencies.add('@types/react');
    dependencies.add('typescript');
    for (const name of dependencies) {
      const link = join(root, 'node_modules', name);
      const target = [
        'packages/capsule-workshop',
        'packages/capsule-react',
        'tools/capsule-react',
        '.',
      ]
        .map((packagePath) =>
          repositoryPath(packagePath + '/node_modules/' + name),
        )
        .find((path) => existsSync(path));
      if (!target) throw Error('Missing test dependency: ' + name);
      await mkdir(dirname(link), { recursive: true });
      await symlink(target, link, 'junction');
    }
    await writeFile(
      join(root, 'consumer.ts'),
      `import { compiler, type CompilerOptions } from '@capsule-sdk/workshop/compiler';
import type { WorkshopOptions } from '@capsule-sdk/workshop';
const options: CompilerOptions = { source: 'src/main.tsx', output: 'published' };
const worker = compiler(options);
const fingerprint: Promise<string> = worker.fingerprint();
const mapped: Promise<void> = worker.mapRuntime();
const workshop: WorkshopOptions = { source: options.source, output: options.output, addonRoot: '.', browserRoot: '.', themeFile: 'theme.json', sdkFile: 'sdk.js' };
void fingerprint; void mapped; void workshop;
async function check() {
  const result = await worker.compile();
  if (result.success) { const hash: string = result.bundleHash; return hash; }
  if (result.obsolete) return result.fingerprint;
  return result.error;
}
void check;
// @ts-expect-error source must be a path string
compiler({ source: 42, output: 'published' });
// @ts-expect-error compile is asynchronous
const synchronous: string = worker.compile();
void synchronous;
`,
    );
    await writeFile(
      join(root, 'tsconfig.json'),
      JSON.stringify({
        compilerOptions: {
          strict: true,
          noEmit: true,
          target: 'ES2022',
          module: 'ESNext',
          moduleResolution: 'Bundler',
          jsx: 'react-jsx',
          types: ['node'],
          lib: ['ES2022', 'DOM'],
        },
        files: ['consumer.ts'],
      }),
    );
    let result: string;
    try {
      result = execFileSync(
        process.execPath,
        [
          repositoryPath('tools/capsule-react/node_modules/typescript/bin/tsc'),
          '-p',
          root,
        ],
        { cwd: root, encoding: 'utf8', windowsHide: true, timeout: 30_000 },
      );
    } catch (error) {
      throw Error(
        (error as { stdout?: string; stderr?: string }).stdout ||
          (error as { stderr?: string }).stderr ||
          'External typecheck failed',
      );
    }
    expect(result).toBe('');
  } finally {
    await rm(root, { recursive: true, force: true });
  }
}, 30_000);
