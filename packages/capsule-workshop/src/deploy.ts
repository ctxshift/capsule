import {
  lstat,
  readdir,
  readFile,
  mkdir,
  copyFile,
  rename,
  rm,
} from 'node:fs/promises';
import {
  resolve,
  dirname,
  basename,
  relative,
  isAbsolute,
  join,
} from 'node:path';
import { createHash, randomUUID } from 'node:crypto';
import { validatePackageTheme } from './package-theme';
import type { ThemeLimits } from './theme';

const digest = (bytes: Uint8Array) =>
  createHash('sha256').update(bytes).digest('hex');
const inside = (root: string, path: string) => {
  const part = relative(root, path);
  return part !== '' && !part.startsWith('..') && !isAbsolute(part);
};
async function noLinks(path: string) {
  for (let current = resolve(path); ; current = dirname(current)) {
    const entry = await lstat(current).catch((error) => {
      if (error.code === 'ENOENT') return null;
      throw error;
    });
    if (entry?.isSymbolicLink())
      throw Error('Deployment must not traverse filesystem links');
    if (dirname(current) === current) return;
  }
}
async function files(root: string) {
  const paths: string[] = [];
  async function visit(directory: string) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isSymbolicLink())
        throw Error('Deployment must not contain filesystem links');
      if (entry.isDirectory()) await visit(path);
      else if (entry.isFile()) paths.push(relative(root, path));
      else throw Error('Deployment requires regular files');
      if (paths.length > 512) throw Error('Deployment file quota');
    }
  }
  await visit(root);
  return paths.sort();
}

/** Replace only one addon beneath an existing Windows Player; never rebuild it. */
export async function deployPackage(
  source: string,
  player: string,
  name: string,
  themeLimits?: Readonly<ThemeLimits>,
) {
  if (!/^[a-z0-9][a-z0-9-]*$/.test(name) || !player.endsWith('.exe'))
    throw Error(
      'Expected an addon directory name and Windows Player executable',
    );
  source = resolve(source);
  player = resolve(player);
  const root = resolve(
    dirname(player),
    basename(player, '.exe') + '_Data',
    'StreamingAssets',
  );
  const target = join(root, name);
  if (source === target || inside(source, target) || inside(target, source))
    throw Error('Deployment source and destination must be separate');
  await noLinks(source);
  await noLinks(target);
  await noLinks(player);
  const playerHash = digest(await readFile(player));
  if (!(await lstat(dirname(root))).isDirectory())
    throw Error('Player data directory is missing');
  const sourceManifest = JSON.parse(
    await readFile(join(source, 'addon.json'), 'utf8'),
  );
  const existing = await lstat(target).catch((error) => {
    if (error.code === 'ENOENT') return null;
    throw error;
  });
  if (existing) {
    await files(target);
    const old = JSON.parse(await readFile(join(target, 'addon.json'), 'utf8'));
    if (old.id !== sourceManifest.id)
      throw Error('Destination addon identity differs');
  }
  const paths = await files(source);
  const stage = join(root, name + '.stage-' + randomUUID());
  const backup = stage + '.previous';
  const hashes: Record<string, string> = {};
  await mkdir(stage, { recursive: true });
  try {
    for (const path of paths) {
      const destination = join(stage, path);
      await mkdir(dirname(destination), { recursive: true });
      await copyFile(join(source, path), destination);
      hashes[path.replaceAll('\\', '/')] = digest(await readFile(destination));
    }
    await validatePackageTheme(stage, themeLimits);
    // Reject a package that changed during the copy (for example a concurrent compiler).
    if (JSON.stringify(await files(source)) !== JSON.stringify(paths))
      throw Error('Package changed during deployment');
    for (const path of paths)
      if (
        digest(await readFile(join(source, path))) !==
        hashes[path.replaceAll('\\', '/')]
      )
        throw Error('Package changed during deployment');
    if (existing) await rename(target, backup);
    try {
      await rename(stage, target);
    } catch (error) {
      if (existing) await rename(backup, target);
      throw error;
    }
    if (existing) await rm(backup, { recursive: true });
    return {
      playerSha256: playerHash,
      packageId: sourceManifest.id,
      files: hashes,
    };
  } finally {
    // Both are freshly allocated children of the verified StreamingAssets root.
    if (inside(root, stage)) await rm(stage, { recursive: true, force: true });
  }
}
