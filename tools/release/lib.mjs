import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import {
  cpSync,
  existsSync,
  lstatSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  writeFileSync,
} from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { gzipSync } from 'node:zlib';

export const sha256 = (bytes) =>
  createHash('sha256').update(bytes).digest('hex');
export const integrity = (bytes) =>
  'sha512-' + createHash('sha512').update(bytes).digest('base64');
export const readJson = (path) => JSON.parse(readFileSync(path, 'utf8'));
export function json(path, value) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, JSON.stringify(value, null, 2) + '\n');
}
export function run(
  executable,
  args,
  { cwd, log, timeout = 180_000, env = {} } = {},
) {
  const result = spawnSync(executable, args, {
    cwd,
    env: { ...process.env, ...env },
    encoding: null,
    windowsHide: true,
    timeout,
    maxBuffer: 40 * 1024 * 1024,
  });
  if (log) {
    mkdirSync(dirname(log), { recursive: true });
    writeFileSync(
      log,
      Buffer.concat([
        result.stdout ?? Buffer.alloc(0),
        result.stderr ?? Buffer.alloc(0),
      ]),
    );
  }
  if (result.error || result.status !== 0)
    throw Error(
      `${executable} failed (${result.status}): ${result.error?.message ?? result.stderr?.toString().slice(-2500)}${log ? '; see retained log' : ''}`,
    );
  return result.stdout;
}
export function copy(from, to) {
  if (lstatSync(from).isSymbolicLink())
    throw Error('Release input may not be a symlink');
  mkdirSync(dirname(to), { recursive: true });
  cpSync(from, to, { recursive: true, dereference: false });
}
export function files(root) {
  const result = [];
  function visit(directory) {
    for (const entry of readdirSync(directory, { withFileTypes: true }).sort(
      (a, b) => a.name.localeCompare(b.name, 'en'),
    )) {
      if (entry.name === '.git') continue;
      const path = join(directory, entry.name);
      if (entry.isSymbolicLink()) throw Error('Release tree contains a link');
      if (entry.isDirectory()) visit(path);
      else result.push(relative(root, path).replaceAll('\\', '/'));
    }
  }
  visit(root);
  return result;
}
export function exact(version) {
  return /^\d+\.\d+\.\d+(?:-[\w.-]+)?$/.test(version);
}
export function validatePackage(manifest) {
  if (!manifest.name || !exact(manifest.version))
    throw Error('Invalid package identity');
  if (
    manifest.private ||
    manifest.workspaces ||
    manifest.devDependencies ||
    manifest.scripts
  )
    throw Error('Source-only package metadata');
  for (const field of [
    'dependencies',
    'peerDependencies',
    'optionalDependencies',
  ]) {
    for (const version of Object.values(manifest[field] ?? {}))
      if (!exact(version))
        throw Error('Release dependency is not exact: ' + version);
  }
}
export function validateSource(manifest) {
  if (manifest.overrides || manifest.resolutions)
    throw Error('Source dependency overrides are active');
  for (const version of Object.values(manifest.dependencies ?? {})) {
    if (!version.startsWith('workspace:') && !exact(version))
      throw Error('Source runtime dependency is floating/local');
  }
}
export function tarball(stage, output) {
  run('git', ['init', '--quiet', stage]);
  run('git', ['-c', 'core.autocrlf=false', '-C', stage, 'add', '--all']);
  const tree = run('git', ['-C', stage, 'write-tree']).toString().trim();
  const tar = run('git', [
    '-c',
    'core.autocrlf=false',
    '-C',
    stage,
    'archive',
    '--format=tar',
    '--mtime=2000-01-01T00:00:00Z',
    '--prefix=package/',
    tree,
  ]);
  const bytes = gzipSync(tar, { level: 9 });
  writeFileSync(output, bytes);
  return {
    sha256: sha256(bytes),
    integrity: integrity(bytes),
    files: files(stage),
  };
}
export function extract(archive, destination) {
  const names = run('tar', ['-tf', archive])
    .toString()
    .split(/\r?\n/)
    .filter(Boolean);
  for (const name of names) {
    if (
      !name.startsWith('package/') ||
      name.includes('\\') ||
      name.includes(':') ||
      name.split('/').includes('..')
    )
      throw Error('Unsafe archive entry');
  }
  const modes = run('tar', ['-tvf', archive])
    .toString()
    .split(/\r?\n/)
    .filter(Boolean);
  if (modes.some((line) => !['-', 'd'].includes(line[0])))
    throw Error('Archive links are forbidden');
  mkdirSync(destination, { recursive: true });
  run('tar', ['-xf', archive, '-C', destination]);
  return resolve(destination, 'package');
}
export function zipBundle(stage, output, prefix = 'CapsuleUnity/') {
  run('git', ['init', '--quiet', stage]);
  run('git', ['-c', 'core.autocrlf=false', '-C', stage, 'add', '--all']);
  const tree = run('git', ['-C', stage, 'write-tree']).toString().trim();
  const bytes = run('git', [
    '-c',
    'core.autocrlf=false',
    '-C',
    stage,
    'archive',
    '--format=zip',
    '--mtime=2000-01-01T00:00:00Z',
    '--prefix=' + prefix,
    tree,
  ]);
  writeFileSync(output, bytes);
  return { sha256: sha256(bytes), files: files(stage) };
}
export function ensureFresh(path) {
  if (existsSync(path))
    throw Error('Output already exists; use a new directory');
  mkdirSync(path, { recursive: true });
}
