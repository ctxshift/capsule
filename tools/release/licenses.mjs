import { createRequire } from 'node:module';
import { existsSync, readFileSync, readdirSync, realpathSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { json, readJson, sha256 } from './lib.mjs';

function locate(name, parent) {
  const require = createRequire(join(parent, 'package.json'));
  for (const root of require.resolve.paths(name) ?? []) {
    const manifest = join(root, name, 'package.json');
    if (existsSync(manifest) && readJson(manifest).name === name)
      return dirname(realpathSync(manifest));
  }
  try {
    return dirname(require.resolve(name + '/package.json'));
  } catch {
    let current = dirname(require.resolve(name));
    while (dirname(current) !== current) {
      const manifest = join(current, 'package.json');
      if (existsSync(manifest) && readJson(manifest).name === name)
        return current;
      current = dirname(current);
    }
    throw Error('Missing package manifest: ' + name);
  }
}

export function collectLicenses(consumer, output) {
  const visited = new Map(),
    omitted = [];
  function visit(name, parent, optional = false) {
    let directory;
    try {
      directory = locate(name, parent);
    } catch (error) {
      if (optional) {
        omitted.push(name);
        return;
      }
      throw error;
    }
    const manifest = readJson(join(directory, 'package.json'));
    const key = manifest.name + '@' + manifest.version;
    if (visited.has(key)) return;
    let noticeDirectory = directory;
    let notices = readdirSync(directory)
      .filter((file) => /^(licen[cs]e|copying|notice)(\.|$)/i.test(file))
      .sort();
    const provider = readJson(join(parent, 'package.json'));
    if (
      !notices.length &&
      provider.version === manifest.version &&
      ((name.startsWith('@esbuild/') && provider.name === 'esbuild') ||
        (name.startsWith('@rollup/') && provider.name === 'rollup'))
    ) {
      noticeDirectory = parent;
      notices = readdirSync(parent)
        .filter((file) => /^(licen[cs]e|copying|notice)(\.|$)/i.test(file))
        .sort();
    }
    if (
      !notices.length &&
      name === 'yoga-layout' &&
      manifest.version === '3.2.1'
    ) {
      noticeDirectory = fileURLToPath(
        new URL(
          '../../unity/Packages/com.capsule.yoga/Runtime/Plugins/x86_64/',
          import.meta.url,
        ),
      );
      notices = ['LICENSE.yoga.txt'];
    }
    if (!notices.length) throw Error('Missing packaged license for ' + key);
    const entry = {
      name: manifest.name,
      version: manifest.version,
      license: manifest.license ?? 'SEE LICENSE',
      noticeSource:
        name === 'yoga-layout'
          ? 'retained Yoga 3.2.1 upstream notice'
          : noticeDirectory === directory
            ? key
            : provider.name + '@' + provider.version,
      notices: [],
    };
    visited.set(key, entry);
    for (const notice of notices) {
      const bytes = readFileSync(join(noticeDirectory, notice));
      const path = key.replaceAll(/[@/]/g, '-') + '/' + notice;
      json(join(output, path + '.json'), {
        name: key,
        notice,
        text: bytes.toString('utf8'),
      });
      entry.notices.push({
        file: 'licenses/npm/' + path + '.json',
        sha256: sha256(readFileSync(join(output, path + '.json'))),
      });
    }
    for (const dependency of Object.keys(manifest.dependencies ?? {}))
      visit(dependency, directory);
    for (const dependency of Object.keys(manifest.optionalDependencies ?? {}))
      visit(dependency, directory, true);
    for (const dependency of Object.keys(manifest.peerDependencies ?? {}))
      visit(
        dependency,
        directory,
        manifest.peerDependenciesMeta?.[dependency]?.optional === true,
      );
  }
  const root = readJson(resolve(consumer, 'package.json'));
  for (const name of Object.keys(root.dependencies)) visit(name, consumer);
  const inventory = {
    packages: [...visited.values()].sort((a, b) =>
      (a.name + '@' + a.version).localeCompare(b.name + '@' + b.version, 'en'),
    ),
    platformOmissions: [...new Set(omitted)].sort(),
  };
  return inventory;
}
