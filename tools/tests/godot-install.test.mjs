import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { install } from '../capsule-godot/install.mjs';

async function fixture(run) {
  const root = await fs.mkdtemp(
    path.join(os.tmpdir(), 'capsule-godot-install-'),
  );
  try {
    const bundle = path.join(root, 'bundle');
    const project = path.join(root, 'game');
    await fs.mkdir(path.join(bundle, 'addons/capsule'), { recursive: true });
    await fs.mkdir(project);
    await fs.writeFile(
      path.join(project, 'project.godot'),
      'config_version=5\n',
    );
    const payload = 'owned addon\n';
    await fs.writeFile(path.join(bundle, 'addons/capsule/plugin.gd'), payload);
    const manifest = {
      schemaVersion: 1,
      engine: 'Godot',
      platform: 'windows-x86_64',
      payload: {
        'addons/capsule/plugin.gd': createHash('sha256')
          .update(payload)
          .digest('hex'),
      },
    };
    await fs.writeFile(
      path.join(bundle, 'capsule-godot.json'),
      JSON.stringify(manifest),
    );
    await run({ root, bundle, project, manifest });
  } finally {
    assert(root.startsWith(path.join(os.tmpdir(), 'capsule-godot-install-')));
    await fs.rm(root, { recursive: true, force: true });
  }
}
test('Godot bundle installation is exact, idempotent and preserves unrelated files', () =>
  fixture(async ({ bundle, project }) => {
    await fs.writeFile(path.join(project, 'keep.txt'), 'keep');
    assert.equal((await install({ bundle, project })).installed, true);
    assert.equal((await install({ bundle, project })).unchanged, true);
    assert.equal(
      await fs.readFile(path.join(project, 'keep.txt'), 'utf8'),
      'keep',
    );
  }));
test('Godot installation rejects a modified existing addon without overwriting it', () =>
  fixture(async ({ bundle, project }) => {
    await install({ bundle, project });
    const source = path.join(project, 'addons/capsule/plugin.gd');
    await fs.writeFile(source, 'local changes');
    await assert.rejects(install({ bundle, project }), /has changes/);
    assert.equal(await fs.readFile(source, 'utf8'), 'local changes');
  }));
test('Godot installation rejects corrupt, additional and unsafe bundle payloads', () =>
  fixture(async ({ bundle, project, manifest }) => {
    await fs.writeFile(
      path.join(bundle, 'addons/capsule/plugin.gd'),
      'corrupt',
    );
    await assert.rejects(install({ bundle, project }), /hash mismatch/);
    await fs.writeFile(
      path.join(bundle, 'addons/capsule/plugin.gd'),
      'owned addon\n',
    );
    await fs.writeFile(
      path.join(bundle, 'addons/capsule/unexpected.txt'),
      'extra',
    );
    await assert.rejects(install({ bundle, project }), /unexpected or missing/);
    manifest.payload = { 'addons/capsule/../../escape': 'bad' };
    await fs.writeFile(
      path.join(bundle, 'capsule-godot.json'),
      JSON.stringify(manifest),
    );
    await assert.rejects(install({ bundle, project }), /Unsafe bundle path/);
    await assert.rejects(fs.access(path.join(project, 'addons/capsule')));
  }));

test('Godot installation refuses an addon parent junction without changing its target', () =>
  fixture(async ({ root, bundle, project }) => {
    const target = path.join(root, 'other-owned-directory');
    await fs.mkdir(target);
    await fs.writeFile(path.join(target, 'keep.txt'), 'keep');
    await fs.symlink(
      target,
      path.join(project, 'addons'),
      process.platform === 'win32' ? 'junction' : 'dir',
    );
    await assert.rejects(
      install({ bundle, project }),
      /real project directory/,
    );
    assert.deepEqual(await fs.readdir(target), ['keep.txt']);
  }));
