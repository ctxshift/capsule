import {
  existsSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  writeFileSync,
} from 'node:fs';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { release } from './config.mjs';
import { installConsumer } from './verify.mjs';
import {
  copy,
  ensureFresh,
  files,
  json,
  readJson,
  run,
  sha256,
  tarball,
  validatePackage,
  validateSource,
  zipBundle,
} from './lib.mjs';

export function packNpm(source, output, work, revision) {
  mkdirSync(output, { recursive: true });
  const versions = new Map(
    release.npm.map((plan) => {
      const p = readJson(join(source, plan.path, 'package.json'));
      return [p.name, p.version];
    }),
  );
  const result = [];
  for (const plan of release.npm) {
    const original = readJson(join(source, plan.path, 'package.json'));
    validateSource(original);
    if (!original.name.startsWith(release.npmScope + '/'))
      throw Error('Package is outside the owned npm scope');
    const stage = join(work, original.name.replaceAll(/[@/]/g, '-'));
    mkdirSync(stage, { recursive: true });
    for (const file of plan.files)
      copy(join(source, plan.path, file), join(stage, file));
    copy(join(source, 'LICENSE'), join(stage, 'LICENSE'));
    const manifest = {
      name: original.name,
      version: original.version,
      ...(original.type ? { type: original.type } : {}),
      license: 'Apache-2.0',
      publishConfig: {
        access: 'public',
        registry: 'https://registry.npmjs.org/',
      },
      repository: {
        type: 'git',
        url: release.repository + '.git',
        directory: plan.path,
      },
      exports: original.exports,
      ...(original.bin ? { bin: original.bin } : {}),
      ...(original.dependencies
        ? { dependencies: { ...original.dependencies } }
        : {}),
      ...(original.peerDependencies
        ? { peerDependencies: { ...original.peerDependencies } }
        : {}),
    };
    for (const name of plan.promote ?? []) {
      manifest.dependencies ??= {};
      manifest.dependencies[name] = original.devDependencies[name];
    }
    if (plan.sdk) {
      manifest.exports = {
        '.': './scripts/build.mjs',
        './sdk': './scripts/build.mjs',
        './fixtures/*': './fixtures/*',
      };
      copy(
        join(source, 'tools/capsule-native-tests/fixture/main.tsx'),
        join(stage, 'fixtures/native/main.tsx'),
      );
      copy(
        join(source, 'tools/capsule-native-tests/fixture/addon.json'),
        join(stage, 'fixtures/native/addon.json'),
      );
    }
    for (const field of ['dependencies', 'peerDependencies'])
      for (const [name, version] of Object.entries(manifest[field] ?? {})) {
        if (version.startsWith('workspace:')) {
          if (!versions.has(name))
            throw Error('Unreleased workspace dependency ' + name);
          manifest[field][name] = versions.get(name);
        }
      }
    validatePackage(manifest);
    json(join(stage, 'package.json'), manifest);
    const filename =
      original.name.replace('@', '').replaceAll('/', '-') +
      '-' +
      original.version +
      '.tgz';
    const archive = tarball(stage, join(output, filename));
    result.push({
      name: manifest.name,
      version: manifest.version,
      file: 'npm/' + filename,
      ...archive,
      dependencies: manifest.dependencies ?? {},
      peerDependencies: manifest.peerDependencies ?? {},
      source: plan.path,
      revision,
    });
  }
  return result;
}

export function packUpm(source, output, work, revision) {
  mkdirSync(output, { recursive: true });
  const result = [];
  for (const name of readdirSync(join(source, 'unity/Packages'))
    .filter((name) => name.startsWith('com.capsule.'))
    .sort()) {
    const original = join(source, 'unity/Packages', name),
      stage = join(work, name);
    mkdirSync(stage, { recursive: true });
    for (const entry of readdirSync(original)) {
      if (entry === 'Tests' || entry === 'Tests.meta') continue;
      copy(join(original, entry), join(stage, entry));
    }
    const manifest = readJson(join(stage, 'package.json'));
    if (manifest.name !== name || manifest.version !== release.version)
      throw Error('UPM identity/version mismatch');
    manifest.license = 'Apache-2.0';
    for (const version of Object.values(manifest.dependencies ?? {}))
      if (!/^\d+\.\d+\.\d+$/.test(version))
        throw Error('Floating UPM dependency');
    copy(join(source, 'LICENSE'), join(stage, 'LICENSE.md'));
    if (!existsSync(join(stage, 'LICENSE.md.meta')))
      writeFileSync(
        join(stage, 'LICENSE.md.meta'),
        'fileFormatVersion: 2\nguid: ' +
          sha256(name + '/LICENSE.md').slice(0, 32) +
          '\n',
      );
    json(join(stage, 'package.json'), manifest);
    const filename = name + '-' + manifest.version + '.tgz';
    result.push({
      name,
      version: manifest.version,
      file: 'upm/' + filename,
      ...tarball(stage, join(output, filename)),
      dependencies: manifest.dependencies ?? {},
      revision,
      git:
        release.repository +
        '.git?path=/unity/Packages/' +
        name +
        '#' +
        revision,
    });
  }
  if (result.length !== 8) throw Error('Expected eight UPM packages');
  const identities = new Set(result.map((p) => p.name));
  for (const p of result)
    for (const [name, version] of Object.entries(p.dependencies)) {
      if (
        name.startsWith('com.capsule.') &&
        (!identities.has(name) || version !== release.version)
      )
        throw Error('Unresolved UPM closure');
    }
  return result;
}

export function buildRelease({ repository, revision = 'HEAD', output }) {
  const resolved = run('git', [
    '-C',
    repository,
    'rev-parse',
    revision + '^{commit}',
  ])
    .toString()
    .trim();
  if (!/^[a-f0-9]{40}$/.test(resolved)) throw Error('Invalid revision');
  // Build tools and inputs must belong to the named source commit.
  run('git', [
    '-C',
    repository,
    'diff',
    '--exit-code',
    resolved,
    '--',
    'tools/release',
    'packages',
    'unity/Packages',
    'bun.lock',
    'package.json',
    'global.json',
  ]);
  if (
    run('git', [
      '-C',
      repository,
      'ls-files',
      '--others',
      '--exclude-standard',
      '--',
      'tools/release',
    ]).length
  )
    throw Error('Uncommitted release tooling');
  output = resolve(output);
  ensureFresh(output);
  const work = mkdtempSync(join(tmpdir(), 'capsule-release-'));
  const logs = join(repository, '.local/release');
  mkdirSync(logs, { recursive: true });
  writeFileSync(join(logs, 'work-location.txt'), work);
  const snapshot = join(work, 'source');
  mkdirSync(snapshot);
  run('git', [
    '-c',
    'core.autocrlf=false',
    '-C',
    repository,
    'archive',
    '--format=tar',
    '--mtime=2000-01-01T00:00:00Z',
    '--output=' + join(work, 'source.tar'),
    resolved,
  ]);
  run('tar', ['-xf', join(work, 'source.tar'), '-C', snapshot]);
  validateSource(readJson(join(snapshot, 'package.json')));
  const bun = run('bun', ['-p', 'process.execPath'], { cwd: repository })
    .toString()
    .trim();
  if (run(bun, ['--version']).toString().trim() !== release.tools.bun)
    throw Error('Unexpected Bun version');
  if (process.versions.node !== release.tools.node)
    throw Error('Unexpected Node version');
  run(
    bun,
    ['install', '--frozen-lockfile', '--cache-dir', join(work, 'source-cache')],
    { cwd: snapshot, log: join(logs, 'source-install.log') },
  );
  const npm = packNpm(
    snapshot,
    join(output, 'npm'),
    join(work, 'npm-stages'),
    resolved,
  );
  const upm = packUpm(
    snapshot,
    join(output, 'upm'),
    join(work, 'upm-stages'),
    resolved,
  );
  run(
    'pwsh',
    [
      '-NoProfile',
      '-File',
      join(snapshot, 'tools/release/build-managed.ps1'),
      '-Output',
      join(work, 'runtime-bundle/managed'),
      '-Revision',
      resolved,
    ],
    {
      cwd: snapshot,
      log: join(logs, 'managed-build.log'),
      timeout: 300_000,
      env: { NUGET_PACKAGES: join(work, 'managed-cache') },
    },
  );
  const runtimeStage = join(work, 'runtime-bundle');
  for (const [source, target] of [
    [
      'com.capsule.wasmtime/Runtime/Plugins/x86_64/wasmtime.dll',
      'native/wasmtime.dll',
    ],
    [
      'com.capsule.wasmtime/Runtime/Plugins/x86_64/portable_ui_wasmtime.dll',
      'native/portable_ui_wasmtime.dll',
    ],
    [
      'com.capsule.yoga/Runtime/Plugins/x86_64/portable_ui_yoga.dll',
      'native/portable_ui_yoga.dll',
    ],
    [
      'com.capsule.wasmtime/Runtime/Carrier/CapsuleQuickJs.bytes',
      'resources/CapsuleQuickJs.bytes',
    ],
    [
      'com.capsule.wasmtime/Runtime/Resources/CapsuleReactSdk.txt',
      'resources/CapsuleReactSdk.txt',
    ],
    [
      'com.capsule.wasmtime/Runtime/Wasmtime-LICENSE.txt',
      'licenses/Wasmtime-LICENSE.txt',
    ],
    [
      'com.capsule.yoga/Runtime/Plugins/x86_64/LICENSE.yoga.txt',
      'licenses/LICENSE.yoga.txt',
    ],
  ])
    copy(join(snapshot, 'unity/Packages', source), join(runtimeStage, target));
  for (const notice of files(
    join(snapshot, 'unity/Packages/com.capsule.wasmtime/ThirdPartyNotices'),
  ).filter((file) => file.endsWith('.txt')))
    copy(
      join(
        snapshot,
        'unity/Packages/com.capsule.wasmtime/ThirdPartyNotices',
        notice,
      ),
      join(runtimeStage, 'licenses', notice),
    );
  copy(join(snapshot, 'LICENSE'), join(runtimeStage, 'LICENSE'));
  writeFileSync(
    join(runtimeStage, 'README.md'),
    '# Capsule runtime bundle\n\nSee docs/release-native.md for direct DLL installation and docs/releases.md for compatibility, upgrades and rollback. This bundle is not an engine adapter.\n',
  );
  const bundleStage = join(work, 'unity-bundle');
  for (const entry of upm)
    for (const file of entry.files)
      copy(
        join(work, 'upm-stages', entry.name, file),
        join(bundleStage, 'Packages', entry.name, file),
      );
  copy(join(snapshot, 'LICENSE'), join(bundleStage, 'LICENSE'));
  for (const stage of [runtimeStage, bundleStage]) {
    copy(join(snapshot, 'docs'), join(stage, 'docs'));
    copy(join(snapshot, 'THIRD-PARTY.md'), join(stage, 'THIRD-PARTY.md'));
  }
  writeFileSync(
    join(bundleStage, 'README.md'),
    '# Capsule Unity bundle\n\nClose the target Editor before changing packages. Copy the contents of Packages/ into your project Packages/ directory, preserving existing unrelated packages. All eight Capsule packages must come from this bundle. Open Unity and follow docs/unity-setup.md. Do not install alongside older copied Capsule assemblies.\n\nThe Capsule setup window is planned; this candidate uses explicit package installation. Godot and Unreal adapters are not included.\n',
  );
  json(join(bundleStage, 'bundle-manifest.json'), {
    version: release.version,
    revision: resolved,
    packages: upm.map(({ name, version, sha256 }) => ({
      name,
      version,
      sha256,
    })),
  });
  mkdirSync(join(output, 'engines'));
  const unityBundle = {
    engine: 'unity',
    version: release.version,
    revision: resolved,
    file: 'engines/capsule-unity-' + release.version + '.zip',
    ...zipBundle(
      bundleStage,
      join(output, 'engines/capsule-unity-' + release.version + '.zip'),
    ),
  };
  const runtimeBundle = {
    engine: 'portable-runtime',
    version: release.version,
    revision: resolved,
    file: 'engines/capsule-runtime-win-x64-' + release.version + '.zip',
    ...zipBundle(
      runtimeStage,
      join(
        output,
        'engines/capsule-runtime-win-x64-' + release.version + '.zip',
      ),
      'CapsuleRuntime/',
    ),
  };
  const resources = [];
  for (const packagePath of ['com.capsule.wasmtime', 'com.capsule.yoga']) {
    const root = join(snapshot, 'unity/Packages', packagePath);
    for (const path of files(root).filter(
      (path) => /\.(dll|bytes)$/.test(path) || /Resources\/.*\.txt$/.test(path),
    ))
      resources.push({
        package: packagePath,
        path,
        sha256: sha256(readFileSync(join(root, path))),
      });
  }
  const compatibility = {
    runtime: release.runtime,
    resources,
    featureBaselineSha256: sha256(
      readFileSync(join(snapshot, 'docs/provenance/game-features.json')),
    ),
  };
  const manifest = {
    schemaVersion: 1,
    version: release.version,
    revision: resolved,
    distribution: {
      kind: 'npm-and-github-engines',
      repository: release.repository,
      tag: 'v' + release.version,
      published: false,
      npm: { registry: 'https://registry.npmjs.org', published: false },
    },
    tools: release.tools,
    runtime: release.runtime,
    compatibilityIdentity: sha256(JSON.stringify(compatibility)),
    featureBaselineSha256: compatibility.featureBaselineSha256,
    resources,
    npm,
    upm,
    engines: [unityBundle, runtimeBundle],
  };
  json(join(output, 'release-manifest.json'), manifest);
  json(join(output, 'unity-manifest.json'), {
    dependencies: Object.fromEntries(upm.map((p) => [p.name, p.git])),
  });
  copy(join(snapshot, 'LICENSE'), join(output, 'LICENSE'));
  installConsumer(output, manifest, { frozen: false, licenses: true });
  manifest.auxiliary = files(output)
    .filter(
      (path) =>
        path !== 'release-manifest.json' &&
        ![...manifest.npm, ...manifest.upm, ...manifest.engines].some(
          (entry) => entry.file === path,
        ),
    )
    .map((file) => ({
      file,
      sha256: sha256(readFileSync(join(output, file))),
    }));
  json(join(output, 'release-manifest.json'), manifest);
  writeFileSync(
    join(output, 'SHA256SUMS'),
    files(output)
      .filter((file) => file !== 'SHA256SUMS')
      .map((file) => sha256(readFileSync(join(output, file))) + '  ' + file)
      .join('\n') + '\n',
  );
  console.log('Built release ' + release.version + ' at ' + resolved);
  return { output, snapshot, work, manifest };
}

if (
  process.argv[1] &&
  resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  const { values } = parseArgs({
    options: {
      output: { type: 'string' },
      revision: { type: 'string', default: 'HEAD' },
    },
  });
  if (!values.output)
    throw Error('Required: --output <fresh-directory> [--revision <commit>]');
  buildRelease({
    repository: resolve(dirname(fileURLToPath(import.meta.url)), '../..'),
    ...values,
  });
}
