import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { release } from './config.mjs';
import { collectLicenses } from './licenses.mjs';
import {
  copy,
  extract,
  files,
  integrity,
  json,
  readJson,
  run,
  sha256,
  validatePackage,
} from './lib.mjs';

export function checkArtifacts(root) {
  root = resolve(root);
  const manifest = readJson(join(root, 'release-manifest.json'));
  if (
    manifest.schemaVersion !== 1 ||
    manifest.version !== release.version ||
    !/^[a-f0-9]{40}$/.test(manifest.revision)
  )
    throw Error('Invalid release identity');
  if (
    manifest.npm.length !== 8 ||
    manifest.upm.length !== 8 ||
    manifest.engines?.length !== 2
  )
    throw Error('Incomplete release set');
  const paths = new Set();
  for (const cohort of [manifest.npm, manifest.upm])
    if (new Set(cohort.map((entry) => entry.name)).size !== cohort.length)
      throw Error('Duplicate package identity');
  if (
    new Set(manifest.engines.map((entry) => entry.engine)).size !== 2 ||
    !manifest.engines.some((entry) => entry.engine === 'unity') ||
    !manifest.engines.some((entry) => entry.engine === 'portable-runtime')
  )
    throw Error('Invalid engine bundle set');
  for (const entry of [...manifest.npm, ...manifest.upm, ...manifest.engines]) {
    if (
      typeof entry.file !== 'string' ||
      !/^(npm|upm|engines)\/[^/\\:]+$/.test(entry.file) ||
      paths.has(entry.file)
    )
      throw Error('Unsafe or repeated artifact path');
    paths.add(entry.file);
    const bytes = readFileSync(join(root, entry.file));
    if (
      sha256(bytes) !== entry.sha256 ||
      (entry.integrity && integrity(bytes) !== entry.integrity)
    )
      throw Error('Artifact integrity mismatch: ' + entry.file);
  }
  const expected = sha256(
    JSON.stringify({
      runtime: manifest.runtime,
      resources: manifest.resources,
      featureBaselineSha256: manifest.featureBaselineSha256,
    }),
  );
  if (manifest.compatibilityIdentity !== expected)
    throw Error('Incompatible resource set identity');
  if (JSON.stringify(manifest.runtime) !== JSON.stringify(release.runtime))
    throw Error('Unsupported runtime ABI/toolchain');
  for (const entry of manifest.auxiliary ?? []) {
    if (
      entry.file.includes('\\') ||
      entry.file.includes(':') ||
      entry.file.split('/').includes('..') ||
      entry.file.startsWith('/')
    )
      throw Error('Unsafe auxiliary path');
    if (sha256(readFileSync(join(root, entry.file))) !== entry.sha256)
      throw Error('Auxiliary integrity mismatch');
  }
  return manifest;
}

export function verifyArchives(root, manifest, work) {
  const npmNames = new Map(manifest.npm.map((p) => [p.name, p.version]));
  const upmNames = new Map(manifest.upm.map((p) => [p.name, p.version]));
  const guids = new Set();
  for (const entry of [...manifest.npm, ...manifest.upm]) {
    const directory = extract(
      join(root, entry.file),
      join(work, entry.name.replaceAll(/[@/]/g, '-')),
    );
    const actual = files(directory);
    if (JSON.stringify(actual) !== JSON.stringify(entry.files))
      throw Error('Archive file inventory mismatch');
    const pkg = readJson(join(directory, 'package.json'));
    if (pkg.name !== entry.name || pkg.version !== entry.version)
      throw Error('Archive identity mismatch');
    if (entry.file.startsWith('npm/')) {
      validatePackage(pkg);
      function checkExport(value) {
        if (typeof value === 'string') {
          const path = value.replace(/^\.\//, '');
          if (
            !value.startsWith('./') ||
            path.split('/').includes('..') ||
            !(path.includes('*')
              ? actual.some(
                  (file) =>
                    file.startsWith(path.split('*')[0]) &&
                    file.endsWith(path.split('*')[1]),
                )
              : actual.includes(path))
          )
            throw Error('Missing or unsafe public export');
        } else
          for (const child of Object.values(value ?? {})) checkExport(child);
      }
      checkExport(pkg.exports);
      for (const [name, version] of Object.entries(pkg.dependencies ?? {})) {
        if (
          (name.startsWith('@capsule/') ||
            name.startsWith('@capsule-example/')) &&
          npmNames.get(name) !== version
        )
          throw Error('Unresolved npm package closure');
      }
      for (const file of actual)
        if (
          /(^|\/)(tests?|node_modules|\.local|\.bees|bin|obj)(\/|$)/.test(file)
        )
          throw Error('Source-only files in npm package');
    } else {
      if (
        entry.revision !== manifest.revision ||
        entry.git !==
          release.repository +
            '.git?path=/unity/Packages/' +
            entry.name +
            '#' +
            manifest.revision
      )
        throw Error('UPM revision drift');
      for (const [name, version] of Object.entries(pkg.dependencies ?? {})) {
        if (
          !/^\d+\.\d+\.\d+$/.test(version) ||
          (name.startsWith('com.capsule.') && upmNames.get(name) !== version)
        )
          throw Error('Unresolved UPM package closure');
      }
      for (const file of actual.filter((file) => file.endsWith('.meta'))) {
        const guid = readFileSync(join(directory, file), 'utf8').match(
          /^guid: (\w+)/m,
        )?.[1];
        if (!guid || guids.has(guid)) throw Error('UPM GUID collision');
        guids.add(guid);
      }
      for (const resource of manifest.resources.filter(
        (r) => r.package === entry.name,
      )) {
        if (
          sha256(readFileSync(join(directory, resource.path))) !==
          resource.sha256
        )
          throw Error('UPM runtime resource mismatch');
      }
    }
  }
  return { upmGuids: guids.size };
}

export function verifyBundles(root, manifest, work) {
  let runtime;
  for (const entry of manifest.engines) {
    const prefix =
      entry.engine === 'unity' ? 'CapsuleUnity/' : 'CapsuleRuntime/';
    const archive = join(root, entry.file);
    const names = run('tar', ['-tf', archive])
      .toString()
      .split(/\r?\n/)
      .filter(Boolean);
    if (
      new Set(names).size !== names.length ||
      names.some(
        (name) =>
          !name.startsWith(prefix) ||
          name.includes('\\') ||
          name.includes(':') ||
          name.split('/').includes('..'),
      )
    )
      throw Error('Unsafe bundle entry');
    const modes = run('tar', ['-tvf', archive])
      .toString()
      .split(/\r?\n/)
      .filter(Boolean);
    if (modes.some((line) => !['-', 'd'].includes(line[0])))
      throw Error('Bundle links forbidden');
    const destination = join(work, entry.engine);
    mkdirSync(destination);
    run('tar', ['-xf', archive, '-C', destination]);
    const unpacked = join(destination, prefix);
    if (JSON.stringify(files(unpacked)) !== JSON.stringify(entry.files))
      throw Error('Bundle inventory mismatch');
    if (
      entry.revision !== manifest.revision ||
      entry.version !== manifest.version
    )
      throw Error('Bundle revision mismatch');
    if (entry.engine === 'unity') {
      for (const pkg of manifest.upm) {
        const original = join(
          work,
          pkg.name.replaceAll(/[@/]/g, '-'),
          'package',
        );
        for (const file of pkg.files)
          if (
            sha256(readFileSync(join(original, file))) !==
            sha256(readFileSync(join(unpacked, 'Packages', pkg.name, file)))
          )
            throw Error('Unity bundle package drift: ' + pkg.name + '/' + file);
      }
    } else {
      runtime = unpacked;
      for (const name of [
        'Capsule.Core',
        'Capsule.Package',
        'Capsule.Wasmtime',
        'Capsule.Yoga',
        'Rpg.Features',
        'Mmo.Features',
        'SkillSystem.Features',
        'Newtonsoft.Json',
      ])
        readFileSync(join(unpacked, 'managed', name + '.dll'));
      for (const [path, packageName, source] of [
        [
          'native/wasmtime.dll',
          'com.capsule.wasmtime',
          'Runtime/Plugins/x86_64/wasmtime.dll',
        ],
        [
          'native/portable_ui_wasmtime.dll',
          'com.capsule.wasmtime',
          'Runtime/Plugins/x86_64/portable_ui_wasmtime.dll',
        ],
        [
          'native/portable_ui_yoga.dll',
          'com.capsule.yoga',
          'Runtime/Plugins/x86_64/portable_ui_yoga.dll',
        ],
        [
          'resources/CapsuleQuickJs.bytes',
          'com.capsule.wasmtime',
          'Runtime/Carrier/CapsuleQuickJs.bytes',
        ],
        [
          'resources/CapsuleReactSdk.txt',
          'com.capsule.wasmtime',
          'Runtime/Resources/CapsuleReactSdk.txt',
        ],
      ])
        if (
          sha256(readFileSync(join(unpacked, path))) !==
          manifest.resources.find(
            (r) => r.package === packageName && r.path === source,
          )?.sha256
        )
          throw Error('Runtime bundle resource mismatch');
    }
  }
  if (!runtime) throw Error('Missing runtime bundle');
  return runtime;
}

export function installConsumer(
  root,
  manifest,
  { frozen = false, licenses = false } = {},
) {
  const work = mkdtempSync(join(tmpdir(), 'capsule-packed-consumer-'));
  const consumer = join(work, 'consumer');
  mkdirSync(consumer);
  copy(join(root, 'npm'), join(work, 'npm'));
  const template = join(root, 'consumer');
  if (frozen) {
    copy(join(template, 'package.json'), join(consumer, 'package.json'));
    copy(join(template, 'bun.lock'), join(consumer, 'bun.lock'));
  } else {
    const dependencies = Object.fromEntries(
      manifest.npm.map((p) => [p.name, 'file:../' + p.file]),
    );
    json(join(consumer, 'package.json'), {
      name: 'capsule-release-consumer',
      private: true,
      type: 'module',
      dependencies,
      overrides: dependencies,
    });
    copy(
      fileURLToPath(new URL('./consumer.bun.lock', import.meta.url)),
      join(consumer, 'bun.lock'),
    );
  }
  copy(
    fileURLToPath(new URL('./consumer.ts', import.meta.url)),
    join(consumer, 'verify.ts'),
  );
  const bun = run('bun', ['-p', 'process.execPath'], {
    cwd: dirname(fileURLToPath(import.meta.url)),
  })
    .toString()
    .trim();
  const logs = resolve(
    dirname(fileURLToPath(import.meta.url)),
    '../../.local/release',
  );
  mkdirSync(logs, { recursive: true });
  writeFileSync(join(logs, 'consumer-location.txt'), consumer);
  run(
    bun,
    ['install', '--cache-dir', join(work, 'cache'), '--frozen-lockfile'],
    { cwd: consumer, log: join(logs, 'packed-install.log') },
  );
  run(bun, ['verify.ts'], {
    cwd: consumer,
    log: join(logs, 'packed-consumer.log'),
  });
  if (
    sha256(readFileSync(join(consumer, 'sdk.js'))) !==
    manifest.resources.find(
      (r) => r.path === 'Runtime/Resources/CapsuleReactSdk.txt',
    )?.sha256
  )
    throw Error('Packed SDK/carrier set mismatch');
  if (!frozen) {
    mkdirSync(template, { recursive: true });
    copy(join(consumer, 'package.json'), join(template, 'package.json'));
    copy(join(consumer, 'bun.lock'), join(template, 'bun.lock'));
  }
  if (licenses)
    json(
      join(root, 'npm-dependencies.json'),
      collectLicenses(consumer, join(root, 'licenses/npm')),
    );
  writeFileSync(join(logs, 'consumer-location.txt'), consumer);
  return {
    consumer,
    sdk: join(consumer, 'sdk.js'),
    addon: join(consumer, 'publication/bundle.js'),
  };
}

export function verifyRelease(root, { native = true, frozen = true } = {}) {
  root = resolve(root);
  const manifest = checkArtifacts(root);
  const declared = [
    ...manifest.npm,
    ...manifest.upm,
    ...manifest.engines,
    ...manifest.auxiliary,
  ]
    .map((entry) => entry.file)
    .concat(['release-manifest.json', 'SHA256SUMS'])
    .sort();
  if (
    new Set(declared).size !== declared.length ||
    JSON.stringify(declared) !== JSON.stringify(files(root).sort())
  )
    throw Error('Unlisted or missing release file');
  const checksums =
    files(root)
      .filter((file) => file !== 'SHA256SUMS')
      .map((file) => sha256(readFileSync(join(root, file))) + '  ' + file)
      .join('\n') + '\n';
  if (readFileSync(join(root, 'SHA256SUMS'), 'utf8') !== checksums)
    throw Error('Checksum inventory mismatch');
  const work = mkdtempSync(join(tmpdir(), 'capsule-release-archives-'));
  const archive = verifyArchives(root, manifest, work);
  const consumer = installConsumer(root, manifest, {
    frozen,
    licenses: !frozen,
  });
  const runtime = verifyBundles(root, manifest, work);
  if (native)
    run(
      'pwsh',
      [
        '-NoProfile',
        '-File',
        fileURLToPath(new URL('./verify-managed.ps1', import.meta.url)),
        '-Artifacts',
        runtime,
        '-Sdk',
        consumer.sdk,
        '-AddonBundle',
        consumer.addon,
      ],
      {
        log: resolve(
          dirname(fileURLToPath(import.meta.url)),
          '../../.local/release/native-verify.log',
        ),
        timeout: 300_000,
      },
    );
  console.log(
    'PASS portable release checks: npm/UPM archives and direct DLL bundles; Unity Editor acceptance is a separate gate',
  );
  return { manifest, archive, ...consumer };
}

if (
  process.argv[1] &&
  resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  const { values } = parseArgs({
    options: {
      artifacts: { type: 'string' },
      initialize: { type: 'boolean', default: false },
      'skip-native': { type: 'boolean', default: false },
    },
  });
  if (!values.artifacts)
    throw Error('Required: --artifacts <release-directory>');
  verifyRelease(values.artifacts, {
    native: !values['skip-native'],
    frozen: !values.initialize,
  });
}
