import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import {
  copy,
  extract,
  json,
  sha256,
  tarball,
  validatePackage,
  validateSource,
  run,
  zipBundle,
} from '../lib.mjs';
import { checkArtifacts } from '../verify.mjs';
import { release } from '../config.mjs';

test('release manifests reject workspace, floating, local and source-only metadata', () => {
  const good = {
    name: '@example/api',
    version: '0.1.0',
    dependencies: { react: '18.3.1' },
  };
  validatePackage(good);
  for (const version of [
    'workspace:*',
    'file:../api',
    '^18.3.1',
    'latest',
    'https://example.com/api.tgz',
  ])
    assert.throws(
      () => validatePackage({ ...good, dependencies: { react: version } }),
      /not exact/,
    );
  assert.throws(
    () => validatePackage({ ...good, private: true }),
    /Source-only/,
  );
  assert.throws(
    () => validatePackage({ ...good, scripts: { prepare: 'echo source' } }),
    /Source-only/,
  );
  assert.throws(() => validateSource({ overrides: {} }), /overrides/);
  assert.throws(
    () => validateSource({ dependencies: { example: 'link:../example' } }),
    /floating\/local/,
  );
});

test('tarballs reproduce independently and retain the complete explicit inventory', () => {
  const root = mkdtempSync(join(tmpdir(), 'capsule-tar-proof-'));
  assert.equal(dirname(root), resolve(tmpdir()));
  try {
    const a = join(root, 'a'),
      b = join(root, 'b');
    mkdirSync(a);
    json(join(a, 'package.json'), { name: '@example/api', version: '0.1.0' });
    writeFileSync(join(a, 'index.js'), 'export const value = 7;\n');
    copy(a, b);
    const first = tarball(a, join(root, 'a.tgz'));
    const second = tarball(b, join(root, 'b.tgz'));
    assert.equal(first.sha256, second.sha256);
    assert.equal(first.integrity, second.integrity);
    const unpacked = extract(join(root, 'a.tgz'), join(root, 'extracted'));
    assert.equal(
      readFileSync(join(unpacked, 'index.js'), 'utf8'),
      'export const value = 7;\n',
    );
    assert.deepEqual(first.files, ['index.js', 'package.json']);
    const zipA = zipBundle(a, join(root, 'a.zip'));
    const zipB = zipBundle(b, join(root, 'b.zip'));
    assert.equal(zipA.sha256, zipB.sha256);
    mkdirSync(join(root, 'zip'));
    run('tar', ['-xf', join(root, 'a.zip'), '-C', join(root, 'zip')]);
    assert.deepEqual(
      readFileSync(join(root, 'zip/CapsuleUnity/index.js')),
      readFileSync(join(a, 'index.js')),
    );
    assert.equal(readdirSync(join(root, 'zip/CapsuleUnity')).length, 2);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('verification rejects corruption, path escapes and compatibility drift', () => {
  const root = mkdtempSync(join(tmpdir(), 'capsule-release-negative-'));
  assert.equal(dirname(root), resolve(tmpdir()));
  try {
    const payload = Buffer.from('fixture artifact');
    const list = (kind, count) =>
      Array.from({ length: count }, (_, i) => {
        const file = kind + '/' + i + '.tgz';
        mkdirSync(join(root, kind), { recursive: true });
        writeFileSync(join(root, file), payload);
        return {
          file,
          name: kind + i,
          engine: i === 0 ? 'unity' : 'portable-runtime',
          sha256: sha256(payload),
        };
      });
    const manifest = {
      schemaVersion: 1,
      version: release.version,
      revision: 'a'.repeat(40),
      npm: list('npm', 8),
      upm: list('upm', 8),
      engines: list('engines', 2),
      runtime: release.runtime,
      resources: [],
      featureBaselineSha256: 'b'.repeat(64),
    };
    manifest.compatibilityIdentity = sha256(
      JSON.stringify({
        runtime: manifest.runtime,
        resources: [],
        featureBaselineSha256: manifest.featureBaselineSha256,
      }),
    );
    const save = () => json(join(root, 'release-manifest.json'), manifest);
    save();
    checkArtifacts(root);
    writeFileSync(join(root, manifest.npm[0].file), 'corrupt');
    assert.throws(() => checkArtifacts(root), /integrity mismatch/);
    writeFileSync(join(root, manifest.npm[0].file), payload);
    const safe = manifest.npm[0].file;
    manifest.npm[0].file = '../outside.tgz';
    save();
    assert.throws(() => checkArtifacts(root), /Unsafe/);
    manifest.npm[0].file = safe;
    manifest.compatibilityIdentity = '0'.repeat(64);
    save();
    assert.throws(() => checkArtifacts(root), /identity/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
