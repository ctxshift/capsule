import { expect, test } from 'vitest';
import { spawnSync } from 'node:child_process';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';

test('recovery fixture builds from a separate working directory', async () => {
  const tempRoot = resolve(tmpdir());
  const scratch = await mkdtemp(join(tempRoot, 'capsule-recovery-build-'));
  if (dirname(resolve(scratch)) !== tempRoot)
    throw new Error('Unexpected recovery test directory');
  try {
    const output = join(scratch, 'recovery.js');
    const builder = resolve(
      import.meta.dirname,
      '../scripts/build-feature-recovery.ts',
    );
    const result = spawnSync('bun', [builder, output], {
      cwd: scratch,
      encoding: 'utf8',
      timeout: 20_000,
    });
    expect(result.error).toBeUndefined();
    expect(result.status, result.stderr || result.stdout).toBe(0);
    const bundle = await readFile(output, 'utf8');
    expect(bundle).toContain('Unexpected snapshot at step');
    expect(bundle).toContain('portableUiUpdate');
  } finally {
    await rm(scratch, { recursive: true, force: true });
  }
});
