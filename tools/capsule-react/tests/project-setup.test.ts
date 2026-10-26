import { test, expect } from 'vitest';
import { createHash } from 'node:crypto';
import {
  mkdtemp,
  mkdir,
  readFile,
  writeFile,
  readdir,
  rm,
  realpath,
  symlink,
} from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import {
  initProject,
  inspectProject,
  startProject,
} from '@capsule-sdk/workshop/project';
import { repositoryPath } from '../scripts/paths.mjs';
import { availablePort } from './available-port';

test('project initialization preserves existing files and is idempotent', async () => {
  const root = await mkdtemp(join(tmpdir(), 'capsule-project-'));
  try {
    const existing = join(root, 'existing');
    await mkdir(existing);
    await writeFile(join(existing, 'owned.txt'), 'keep');
    await expect(initProject({ projectRoot: existing })).rejects.toThrow(
      'not empty',
    );
    expect(await readdir(existing)).toEqual(['owned.txt']);
    const project = join(root, 'ui');
    expect((await initProject({ projectRoot: project })).changed).toBe(true);
    const source = await readFile(join(project, 'src/main.tsx'), 'utf8');
    await writeFile(
      join(project, 'src/main.tsx'),
      source.replace('Capsule UI', 'My UI'),
    );
    expect((await initProject({ projectRoot: project })).changed).toBe(false);
    expect(await readFile(join(project, 'src/main.tsx'), 'utf8')).toContain(
      'My UI',
    );
    expect((await inspectProject(project)).id).toBe('example.capsule');
    await writeFile(
      join(project, 'capsule.project.json'),
      JSON.stringify({
        schemaVersion: 1,
        id: 'example.capsule',
        source: '../escape.tsx',
        theme: 'theme.json',
        port: 5173,
      }),
    );
    await expect(inspectProject(project)).rejects.toThrow(
      'Invalid theme asset path',
    );
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test(
  'versioned engine publication reloads and retains last good on compile errors',
  { timeout: 30000 },
  async () => {
    const root = await mkdtemp(join(tmpdir(), 'capsule-engine-project-'));
    let server: Awaited<ReturnType<typeof startProject>> | undefined;
    try {
      const port = await availablePort();
      await initProject({ projectRoot: root, port });
      for (const [name, path] of [
        ['@capsule-sdk/react', 'packages/capsule-react'],
        ['@capsule-sdk/workshop', 'packages/capsule-workshop'],
        ['@capsule-sdk/react-tools', 'tools/capsule-react'],
        ['@capsule-sdk/features', 'packages/capsule-features'],
        ['react', 'tools/capsule-react/node_modules/react'],
      ]) {
        const target = await realpath(repositoryPath(path));
        const link = join(root, 'node_modules', name);
        await mkdir(dirname(link), { recursive: true });
        await symlink(target, link, 'junction');
      }
      const starter = await readFile(join(root, 'src/main.tsx'), 'utf8');
      const source =
        "import {codec} from '@capsule-sdk/features/codec';import {s} from '@capsule-sdk/features';const value=codec(s.u32).decode(new Uint8Array([1,0,0,0]));\n" +
        starter.replace('Capsule UI', 'Capsule UI {value}');
      await writeFile(join(root, 'src/main.tsx'), source);
      server = await startProject(root);
      const get = async () =>
        await (
          await fetch(server!.endpoint + '/capsule/dev/v1/package')
        ).json();
      const first = await get();
      expect(first.schemaVersion).toBe(1);
      expect(first.addonId).toBe('example.capsule');
      expect(first.compiler.success).toBe(true);
      expect(first.sdkSha256).toMatch(/^[a-f0-9]{64}$/);
      expect(first.imageHashes.neutral).toBe(
        createHash('sha256')
          .update(await readFile(join(root, 'assets/neutral.png')))
          .digest('hex'),
      );
      expect(first.entryBundle).toBe(first.bundle);
      expect(first.scriptModules).toEqual([]);
      expect(first.theme.surfaces[0].key).toBe('panel');
      expect(
        (
          await fetch(
            server.endpoint + '/portable-ui/art/' + first.hash + '/neutral',
          )
        ).status,
      ).toBe(200);
      const status = await (
        await fetch(server.endpoint + '/capsule/dev/v1/status')
      ).json();
      expect(status.source).toBeUndefined();
      expect(status.output).toBeUndefined();
      expect(status.ready).toBe(true);
      const until = async (predicate: (p: typeof first) => boolean) => {
        const deadline = Date.now() + 10000;
        while (Date.now() < deadline) {
          const p = await get();
          if (predicate(p)) return p;
          await new Promise((r) => setTimeout(r, 50));
        }
        throw Error('Publication deadline');
      };
      await writeFile(
        join(root, 'src/main.tsx'),
        source.replace('Capsule UI', 'Updated UI'),
      );
      const changed = await until(
        (p) => p.hash !== first.hash && p.compiler.success,
      );
      expect(changed.bundle).toContain('Updated UI');
      await writeFile(
        join(root, 'src/main.tsx'),
        source.replace('width={360}', 'width="invalid"'),
      );
      const invalid = await until((p) => p.compiler.success === false);
      expect(invalid.hash).toBe(changed.hash);
      expect(invalid.bundle).toBe(changed.bundle);
      await writeFile(join(root, 'src/main.tsx'), source);
      expect(
        (await until((p) => p.compiler.success && p.hash === first.hash)).hash,
      ).toBe(first.hash);
    } finally {
      await server?.close();
      await rm(root, { recursive: true, force: true });
    }
  },
);
