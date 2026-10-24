import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { verifyImport, verifyBoundaries } from '../verify-native-import.mjs';

test('import verification rejects altered payloads, aliases and escaping targets', () => {
  const root = mkdtempSync(join(tmpdir(), 'capsule-import-'));
  try {
    const content =
      'fileFormatVersion: 2\nguid: 12345678901234567890123456789012\n';
    writeFileSync(join(root, 'fixture.meta'), content);
    const entry = {
      target: 'fixture.meta',
      guid: '12345678901234567890123456789012',
      sha256: createHash('sha256').update(content).digest('hex'),
    };
    assert.deepEqual(verifyImport(root, { files: [entry] }), {
      files: 1,
      guids: 1,
      lineEndingTranslations: 0,
    });
    assert.throws(
      () =>
        verifyImport(root, {
          files: [entry, { ...entry, target: './fixture.meta' }],
        }),
      /Duplicate import target/,
    );
    assert.throws(
      () =>
        verifyImport(root, {
          files: [{ ...entry, target: '../outside.meta' }],
        }),
      /escapes repository/,
    );
    writeFileSync(join(root, 'fixture.meta'), content.replaceAll('\n', '\r\n'));
    assert.throws(
      () => verifyImport(root, { files: [entry] }),
      /Changed import payload/,
    );
    assert.equal(
      verifyImport(root, {
        files: [{ ...entry, canonicalSha256: entry.sha256 }],
      }).lineEndingTranslations,
      1,
    );
    writeFileSync(join(root, 'fixture.meta'), content + 'changed');
    assert.throws(
      () => verifyImport(root, { files: [entry] }),
      /Changed import payload/,
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('native package inventory rejects missing, renamed and foreign dependencies', () => {
  const root = mkdtempSync(join(tmpdir(), 'capsule-boundaries-'));
  const names = [
    'core',
    'package',
    'wasmtime',
    'yoga',
    'unity',
    'features.rpg',
    'features.mmo',
    'features.skill-system',
  ];
  try {
    for (const name of names) {
      const directory = join(root, 'unity/Packages', 'com.capsule.' + name);
      mkdirSync(join(directory, 'Runtime'), { recursive: true });
      writeFileSync(
        join(directory, 'package.json'),
        JSON.stringify({ name: 'com.capsule.' + name }),
      );
    }
    assert.equal(verifyBoundaries(root), 8);
    const path = join(
      root,
      'unity/Packages/com.capsule.features.rpg/package.json',
    );
    writeFileSync(path, JSON.stringify({ name: 'wrong' }));
    assert.throws(() => verifyBoundaries(root), /identity mismatch/);
    writeFileSync(
      path,
      JSON.stringify({
        name: 'com.capsule.features.rpg',
        dependencies: { 'com.example.game': '1.0.0' },
      }),
    );
    assert.throws(
      () => verifyBoundaries(root),
      /Unexpected package dependency/,
    );
    rmSync(join(root, 'unity/Packages/com.capsule.features.rpg'), {
      recursive: true,
    });
    assert.throws(
      () => verifyBoundaries(root),
      /Unexpected native package inventory/,
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
