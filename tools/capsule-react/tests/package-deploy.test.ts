import { test, expect } from 'vitest';
import {
  mkdtemp,
  cp,
  readFile,
  writeFile,
  mkdir,
  readdir,
  symlink,
} from 'node:fs/promises';
import { join } from 'node:path';
import { compiler } from '@capsule/workshop/compiler';
import { deployPackage } from '@capsule/workshop/deploy';
import { repositoryPath, localRoot } from '../scripts/paths.mjs';

test('shared compiler rejects invalid themes before publication and deployment preserves the Player', async () => {
  const root = await mkdtemp(join(localRoot, 'package-deploy-'));
  const addon = join(root, 'addon'),
    output = join(root, 'published');
  await cp(repositoryPath('examples/portable-demo-game'), addon, {
    recursive: true,
    filter: (path) => !path.includes('node_modules') && !path.includes('dist'),
  });
  const worker = compiler({
    source: join(addon, 'src/main.tsx'),
    projectRoot: addon,
    manifest: join(addon, 'addon.json'),
    output,
  });
  expect((await worker.compile()).success).toBe(true);
  const good = await readFile(join(output, 'theme.json'), 'utf8');
  const bad = JSON.parse(good);
  bad.assets = Array.from({ length: 65 }, (_, i) => ({
    ...bad.assets[0],
    key: 'recipe-' + i,
  }));
  await writeFile(join(addon, 'theme.json'), JSON.stringify(bad));
  expect((await worker.compile()).success).toBe(false);
  expect(await readFile(join(output, 'theme.json'), 'utf8')).toBe(good);
  const undeclared = JSON.parse(good);
  undeclared.assets[0].image = 'missing-image';
  await writeFile(join(addon, 'theme.json'), JSON.stringify(undeclared));
  expect((await worker.compile()).success).toBe(false);
  expect(await readFile(join(output, 'theme.json'), 'utf8')).toBe(good);
  const player = join(root, 'Player.exe'),
    data = join(root, 'Player_Data');
  await writeFile(player, 'unchanged executable');
  await mkdir(data);
  const first = await deployPackage(output, player, 'demo');
  const target = join(data, 'StreamingAssets/demo');
  await writeFile(join(target, 'obsolete.js'), 'stale');
  await writeFile(join(data, 'StreamingAssets/unrelated.txt'), 'preserved');
  const second = await deployPackage(output, player, 'demo');
  expect(second).toEqual(first);
  expect(await readdir(target)).not.toContain('obsolete.js');
  expect(await readFile(player, 'utf8')).toBe('unchanged executable');
  expect(
    await readFile(join(data, 'StreamingAssets/unrelated.txt'), 'utf8'),
  ).toBe('preserved');
  await writeFile(join(output, 'theme.json'), JSON.stringify(bad));
  await expect(deployPackage(output, player, 'demo')).rejects.toThrow();
  expect(await readFile(join(target, 'theme.json'), 'utf8')).toBe(good);
  await writeFile(join(output, 'theme.json'), good);
  await expect(deployPackage(output, player, '../escape')).rejects.toThrow();
  const link = join(data, 'StreamingAssets/linked');
  await symlink(target, link, 'junction');
  await expect(deployPackage(output, player, 'linked')).rejects.toThrow(
    /links/,
  );
});
