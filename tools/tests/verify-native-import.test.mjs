import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { verifyImport } from '../verify-native-import.mjs';

test('import verification rejects altered payloads, aliases and escaping targets', () => {
  const root = mkdtempSync(join(tmpdir(), 'capsule-import-'));
  try {
    const content = 'fileFormatVersion: 2\nguid: 12345678901234567890123456789012\n';
    writeFileSync(join(root, 'fixture.meta'), content);
    const entry = { target: 'fixture.meta', guid: '12345678901234567890123456789012',
      sha256: createHash('sha256').update(content).digest('hex') };
    assert.deepEqual(verifyImport(root, { files: [entry] }), { files: 1, guids: 1, lineEndingTranslations: 0 });
    assert.throws(() => verifyImport(root, { files: [entry, { ...entry, target: './fixture.meta' }] }), /Duplicate import target/);
    assert.throws(() => verifyImport(root, { files: [{ ...entry, target: '../outside.meta' }] }), /escapes repository/);
    writeFileSync(join(root, 'fixture.meta'), content.replaceAll('\n', '\r\n'));
    assert.throws(() => verifyImport(root, { files: [entry] }), /Changed import payload/);
    assert.equal(verifyImport(root, { files: [{ ...entry, canonicalSha256: entry.sha256 }] }).lineEndingTranslations, 1);
    writeFileSync(join(root, 'fixture.meta'), content + 'changed');
    assert.throws(() => verifyImport(root, { files: [entry] }), /Changed import payload/);
  } finally { rmSync(root, { recursive: true, force: true }); }
});
