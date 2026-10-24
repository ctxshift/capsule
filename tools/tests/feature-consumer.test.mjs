import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cp, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const run = promisify(execFile);
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..');

test(
  'neutral source consumer installs and uses public feature packages independently',
  { timeout: 120_000 },
  async () => {
    const scratch = await mkdtemp(join(tmpdir(), 'capsule-feature-consumer-'));
    assert.equal(dirname(scratch), resolve(tmpdir()));
    const packages = [
      'capsule-features',
      'capsule-react',
      'rpg-features',
      'mmo-features',
      'skill-system-features',
      'inventory-feature',
    ];
    try {
      for (const name of packages) {
        await cp(
          join(repository, 'packages', name),
          join(scratch, 'packages', name),
          {
            recursive: true,
            filter: (path) =>
              !['node_modules', 'bin', 'obj', 'dist'].includes(basename(path)),
          },
        );
      }
      await writeFile(
        join(scratch, 'package.json'),
        JSON.stringify({
          name: 'capsule-feature-consumer-workspace',
          private: true,
          workspaces: ['packages/*', 'consumer'],
        }),
      );
      const consumer = join(scratch, 'consumer');
      await mkdir(consumer);
      const dependencies = { react: '18.3.1' };
      for (const name of packages) {
        const manifest = JSON.parse(
          await readFile(
            join(scratch, 'packages', name, 'package.json'),
            'utf8',
          ),
        );
        dependencies[manifest.name] = 'workspace:*';
      }
      await writeFile(
        join(consumer, 'package.json'),
        JSON.stringify({
          name: 'neutral-feature-consumer',
          private: true,
          type: 'module',
          dependencies,
        }),
      );
      await writeFile(
        join(consumer, 'main.ts'),
        `
import assert from 'node:assert/strict';
import { descriptor, generateCSharp } from '@capsule-sdk/features/codegen';
import { contracts as rpg } from '@capsule-sdk/rpg-features/contracts';
import { contracts as mmo } from '@capsule-sdk/mmo-features/contracts';
import { contracts as skills } from '@capsule-sdk/skill-system-features/contracts';
import { items, inventory } from '@capsule-sdk/example-inventory';
import React from 'react';
import { FeatureRegistry } from '@capsule-sdk/features/runtime';
import { chat } from '@capsule-sdk/mmo-features/chat';
import { createChatMock } from '@capsule-sdk/mmo-features/chat-mock';
const definitions = [...Object.values(rpg), ...Object.values(mmo), ...Object.values(skills), items, inventory];
assert.equal(definitions.length, 16);
for (const feature of definitions) assert.match(descriptor(feature).fingerprint, /^[a-f0-9]{64}$/);
assert(import.meta.resolve('@capsule-sdk/example-inventory/view').endsWith('view.tsx'));
assert.equal(React.version, '18.3.1');
assert(generateCSharp('Consumer.Contracts', rpg).includes('namespace Consumer.Contracts'));
const registry = new FeatureRegistry();
registry.register(chat, descriptor(chat).fingerprint, createChatMock().transport);
const ui = registry.connect(chat, '1.0.*', new Set(['chat.read']));
assert.equal(ui.snapshot('State').available, true);
ui.dispose();
console.log('Independent consumer: 16 public features, native generation and mock admission passed');
`,
      );
      const executable = await run('bun', ['-p', 'process.execPath'], {
        cwd: repository,
        windowsHide: true,
        timeout: 5_000,
      });
      const bun = executable.stdout.trim();
      await run(bun, ['install', '--ignore-scripts'], {
        cwd: scratch,
        windowsHide: true,
        timeout: 90_000,
      });
      const result = await run(bun, ['main.ts'], {
        cwd: consumer,
        windowsHide: true,
        timeout: 15_000,
      });
      assert.match(result.stdout, /Independent consumer: 16 public features/);
    } finally {
      await rm(scratch, { recursive: true, force: true });
    }
  },
);
