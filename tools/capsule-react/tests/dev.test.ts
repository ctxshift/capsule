import { test } from 'vitest';
import { compiler } from '@capsule/workshop/compiler';
import * as fs from 'node:fs/promises';
import { resolve, join } from 'node:path';
test('owned atomic TSX publication and shared-root hot reload', async () => {
  const root = resolve(
    import.meta.dirname,
    '../../../.local/react-compiler-test-' + Date.now(),
  );
  await fs.mkdir(join(root, 'source'), { recursive: true });
  const source = join(root, 'source', 'app.tsx'),
    output = join(root, 'published');
  const valid =
    "import React from 'react';import {Box,Text,render} from '@capsule/react';render(<Box width={100} height={100}><Text width={100} height={20}>A</Text></Box>);";
  const check = (condition: unknown, name: string) => {
    if (!condition) throw Error(name);
  };
  await fs.writeFile(source, valid);
  await fs.mkdir(output);
  await fs.writeFile(join(output, 'keep.txt'), 'user content');
  try {
    await compiler({ source, output }).compile();
    throw Error('Unowned output accepted');
  } catch (e) {
    check(
      e instanceof Error && e.message.includes('not compiler-owned'),
      'unowned output rejection',
    );
  }
  check(
    (await fs.readFile(join(output, 'keep.txt'), 'utf8')) === 'user content',
    'unowned content preserved',
  );
  const owned = join(root, 'owned'),
    worker = compiler({ source, output: owned });
  const initial = await worker.compile();
  check(initial.success, 'valid TSX');
  const good = await fs.readFile(join(owned, 'bundle.js'), 'utf8');
  check(
    good.split('\n').length <= 2,
    'generated transport is whitespace compact',
  );
  const map = JSON.parse(
    await fs.readFile(
      join(owned + '.maps', initial.bundleHash + '.json'),
      'utf8',
    ),
  );
  check(
    map.sourcesContent.includes(valid),
    'source map preserves authored TSX',
  );
  await fs.writeFile(
    source,
    valid.replace('>A<', '>' + 'x'.repeat(65536) + '<'),
  );
  const oversized = await worker.compile();
  check(
    !oversized.success &&
      /^Addon bundle exceeds 64 KiB \(\d+ bytes\)$/.test(oversized.error ?? ''),
    'compact output still obeys the package byte quota',
  );
  check(
    (await fs.readFile(join(owned, 'bundle.js'), 'utf8')) === good,
    'oversized output preserves last-good package',
  );
  await fs.writeFile(source, valid + 'const invalid: number="text";');
  const type = await worker.compile();
  check(
    !type.success && type.error.includes('app.tsx:'),
    'source-located type error',
  );
  check(
    (await fs.readFile(join(owned, 'bundle.js'), 'utf8')) === good,
    'last-good after compiler failure',
  );
  await fs.writeFile(
    source,
    "import {value} from './missing';" + valid.replace('>A<', '>{value}<'),
  );
  const missing = await worker.compile();
  check(!missing.success, 'missing module');
  await fs.writeFile(
    join(root, 'source', 'missing.ts'),
    'export const value="recovered";',
  );
  check(
    (await worker.fingerprint()) !== missing.fingerprint,
    'new import creation changes watch fingerprint',
  );
  check((await worker.compile()).success, 'recovery from new import');
  const old = await fs.readFile(join(owned, 'bundle.js'), 'utf8');
  await fs.writeFile(source, valid);
  const changing = compiler({
    source,
    output: owned,
    beforePublish: () => fs.writeFile(source, valid.replace('>A<', '>B<')),
  });
  check((await changing.compile()).obsolete, 'edit during compile rejected');
  check(
    (await fs.readFile(join(owned, 'bundle.js'), 'utf8')) === old,
    'obsolete candidate not published',
  );
  check((await worker.compile()).success, 'latest source published');
  check(
    (await fs.readdir(owned + '.maps')).includes(initial.bundleHash + '.json'),
    'old source map retained by bundle hash',
  );
  console.log(
    'PASS compiler ownership, type/source diagnostics, missing-import recovery, stale-build rejection and map retention',
  );

  // Shared components can live outside the entry directory, but only explicitly
  // watched bounded roots may participate in this addon publication.
  const sharedRoot = join(root, 'shared');
  await fs.mkdir(sharedRoot);
  const shared = join(sharedRoot, 'message.ts');
  await fs.writeFile(shared, 'export const message="shared A";');
  await fs.writeFile(
    source,
    "import {message} from '../shared/message';" +
      valid.replace('>A<', '>{message}<'),
  );
  const sharedOutput = join(root, 'shared-owned');
  const sharedWorker = compiler({
    source,
    output: sharedOutput,
    watchRoots: [sharedRoot],
  });
  const sharedInitial = await sharedWorker.compile();
  check(sharedInitial.success, 'explicit shared root compile');
  await fs.writeFile(shared, 'export const message="shared B";');
  check(
    (await sharedWorker.fingerprint()) !== sharedInitial.fingerprint,
    'transitive shared edit observed',
  );
  check((await sharedWorker.compile()).success, 'shared edit published');
  check(
    (await fs.readFile(join(sharedOutput, 'bundle.js'), 'utf8')).includes(
      'shared B',
    ),
    'fresh shared bytes',
  );
  const escaped = await compiler({
    source,
    output: join(root, 'unwatched'),
  }).compile();
  check(
    !escaped.success && escaped.error.includes('watched source roots'),
    'unwatched import rejected',
  );
  const relocated = join(root, 'source', 'moved.tsx');
  await fs.writeFile(relocated, valid);
  try {
    await compiler({ source: relocated, output: owned }).compile();
    throw Error('Moved source adopted old owned output');
  } catch (error) {
    check(
      error instanceof Error &&
        error.message.includes('does not match this source'),
      'moved-source owner identity preserved',
    );
  }
  await fs.writeFile(source, valid + 'window.location.href;');
  const browserApi = await worker.compile();
  check(
    !browserApi.success &&
      browserApi.error.includes("Cannot find name 'window'"),
    'unsupported browser API rejected',
  );
  console.log(
    'PASS shared-root transitive edits, unwatched import rejection, moved-source ownership, and no browser globals',
  );
}, 30000);
