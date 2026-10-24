import { parseArgs } from 'node:util';
import { resolve } from 'node:path';
import { compiler } from '@capsule/workshop/compiler';

const { values } = parseArgs({
  options: {
    source: { type: 'string' },
    output: { type: 'string' },
    'project-root': { type: 'string' },
    manifest: { type: 'string' },
    'watch-root': { type: 'string', multiple: true },
    once: { type: 'boolean', default: false },
  },
});
if (
  !values.source ||
  !values.output ||
  !values['project-root'] ||
  !values.manifest
) {
  throw Error(
    'Required: --source <entry.tsx> --output <directory> --project-root <directory> --manifest <addon.json> [--watch-root <directory>] [--once]',
  );
}
const worker = compiler({
  source: resolve(values.source),
  output: resolve(values.output),
  projectRoot: resolve(values['project-root']),
  manifest: resolve(values.manifest),
  watchRoots: (values['watch-root'] ?? []).map((path) => resolve(path)),
});
let stopped = false;
const stop = () => {
  stopped = true;
};
process.once('SIGINT', stop);
process.once('SIGTERM', stop);
let result = await worker.compile();
if (result.error) console.error(result.error);
if (values.once) {
  if (!result.success) process.exitCode = 1;
} else {
  while (!stopped) {
    await new Promise((done) => setTimeout(done, 200));
    if (stopped) break;
    if ((await worker.fingerprint()) !== result.fingerprint) {
      result = await worker.compile();
      if (result.error) console.error(result.error);
    }
  }
}
