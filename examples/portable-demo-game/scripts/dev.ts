import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { startWorkshop } from '@capsule-sdk/workshop';

const root = fileURLToPath(new URL('../', import.meta.url));
const args = process.argv.slice(2);
const sdkIndex = args.indexOf('--sdk');
if (sdkIndex < 0 || !args[sdkIndex + 1] || args[sdkIndex + 1].startsWith('--'))
  throw Error('Usage: bun scripts/dev.ts --sdk <host-runtime.js>');

const workshop = await startWorkshop({
  addonRoot: root,
  source: resolve(root, 'src/main.tsx'),
  browserRoot: resolve(root, 'browser'),
  themeFile: resolve(root, 'theme.json'),
  sdkFile: resolve(args[sdkIndex + 1]),
  output: resolve(root, '../../.local/portable-demo-game'),
});
workshop.server.printUrls();
const stop = async () => {
  await workshop.close();
  process.exit(0);
};
process.once('SIGINT', stop);
process.once('SIGTERM', stop);
