import { createHash, randomBytes } from 'node:crypto';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { files } from '../release/lib.mjs';

const hash = (bytes) => createHash('sha256').update(bytes).digest('hex');
export async function install({ bundle, project }) {
  bundle = path.resolve(bundle);
  project = path.resolve(project);
  if ((await fs.lstat(project)).isSymbolicLink()) {
    throw Error('Project directory must not be a symlink.');
  }
  await fs.access(path.join(project, 'project.godot'));
  const manifest = JSON.parse(
    await fs.readFile(path.join(bundle, 'capsule-godot.json'), 'utf8'),
  );
  if (
    manifest.schemaVersion !== 1 ||
    manifest.engine !== 'Godot' ||
    manifest.platform !== 'windows-x86_64'
  )
    throw Error('Unsupported Capsule Godot bundle.');
  const entries = Object.entries(manifest.payload ?? {});
  if (!entries.length) throw Error('Empty Capsule bundle.');
  for (const [name, digest] of entries) {
    if (
      !name.startsWith('addons/capsule/') ||
      name.split('/').some((part) => !part || part === '.' || part === '..') ||
      name.includes('\\') ||
      path.isAbsolute(name)
    )
      throw Error(`Unsafe bundle path: ${name}`);
    if (hash(await fs.readFile(path.join(bundle, name))) !== digest)
      throw Error(`Bundle hash mismatch: ${name}`);
  }
  const expected = entries
    .map(([name]) => name.slice('addons/capsule/'.length))
    .sort();
  const source = path.join(bundle, 'addons/capsule');
  if (JSON.stringify(files(source).sort()) !== JSON.stringify(expected))
    throw Error('Bundle contains unexpected or missing addon files.');
  const destination = path.join(project, 'addons/capsule');
  const existing = await fs.lstat(destination).catch((error) => {
    if (error.code === 'ENOENT') return null;
    throw error;
  });
  if (existing) {
    if (
      existing.isSymbolicLink() ||
      !existing.isDirectory() ||
      JSON.stringify(files(destination).sort()) !== JSON.stringify(expected)
    )
      throw Error(
        'Existing Capsule addon differs; preserve it and install into a fresh project.',
      );
    for (const [name, digest] of entries)
      if (hash(await fs.readFile(path.join(project, name))) !== digest)
        throw Error(
          'Existing Capsule addon has changes; installation refused.',
        );
    return { installed: false, unchanged: true };
  }
  const parent = path.join(project, 'addons');
  await fs.mkdir(parent, { recursive: true });
  if ((await fs.lstat(parent)).isSymbolicLink())
    throw Error('Addon parent must be a real project directory.');
  const temporary = path.join(
    parent,
    `.capsule-install-${randomBytes(6).toString('hex')}`,
  );
  if (!path.resolve(temporary).startsWith(parent + path.sep)) {
    throw Error('Temporary installation directory escaped the project.');
  }
  try {
    await fs.cp(source, temporary, {
      recursive: true,
      errorOnExist: true,
      force: false,
    });
    await fs.rename(temporary, destination);
  } finally {
    await fs.rm(temporary, { recursive: true, force: true });
  }
  return { installed: true, unchanged: false };
}
if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  const { values } = parseArgs({
    options: { bundle: { type: 'string' }, project: { type: 'string' } },
  });
  if (!values.bundle || !values.project)
    throw Error(
      'Usage: node tools/capsule-godot/install.mjs --bundle <unpacked-bundle> --project <existing-godot-project>',
    );
  console.log(JSON.stringify(await install(values)));
}
