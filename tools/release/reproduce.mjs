import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { buildRelease } from './build.mjs';
import { verifyRelease } from './verify.mjs';
import { ensureFresh, files, json, sha256 } from './lib.mjs';

export function compareCandidates(first, second) {
  const inventory = (root) =>
    files(root).map((file) => ({
      file,
      sha256: sha256(readFileSync(join(root, file))),
    }));
  const a = inventory(first),
    b = inventory(second);
  if (JSON.stringify(a) !== JSON.stringify(b)) {
    const hashes = new Map(b.map((entry) => [entry.file, entry.sha256]));
    const changed = a
      .filter((entry) => hashes.get(entry.file) !== entry.sha256)
      .map((entry) => entry.file);
    const added = b
      .filter((entry) => !a.some((old) => old.file === entry.file))
      .map((entry) => entry.file);
    throw Error('Candidates differ: ' + [...changed, ...added].join(', '));
  }
  return a;
}

export function reproduceRelease({ repository, output, revision = 'HEAD' }) {
  output = resolve(output);
  ensureFresh(output);
  const first = buildRelease({
    repository,
    output: join(output, 'a'),
    revision,
  });
  buildRelease({
    repository,
    output: join(output, 'b'),
    revision: first.manifest.revision,
  });
  const inventory = compareCandidates(join(output, 'a'), join(output, 'b'));
  verifyRelease(join(output, 'a'));
  json(join(output, 'reproducibility.json'), {
    schemaVersion: 1,
    revision: first.manifest.revision,
    identicalFiles: inventory.length,
    inventory,
    portableConsumers: 'passed',
    unityEditorAcceptance: 'separate-required-gate',
    published: false,
  });
  console.log(
    'PASS two independent snapshots: ' +
      inventory.length +
      ' identical release files; portable consumers passed',
  );
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
  reproduceRelease({
    repository: resolve(dirname(fileURLToPath(import.meta.url)), '../..'),
    ...values,
  });
}
