import { build } from 'esbuild';
import { createContext, runInContext } from 'node:vm';
import { expect, test } from 'vitest';
import { repositoryPath } from '../scripts/paths.mjs';

test('whole observation retains its backing buffer and replays without a payload copy', async () => {
  const sdk = await build({
    entryPoints: [repositoryPath('tools/capsule-react/src/host.ts')],
    bundle: true,
    write: false,
    format: 'iife',
    platform: 'browser',
    define: { 'process.env.NODE_ENV': '"production"' },
  });
  const packet = new Uint8Array(100008);
  const view = new DataView(packet.buffer);
  view.setUint16(0, 7, true);
  view.setUint16(2, 1, true);
  view.setUint32(4, 100000, true);
  packet[100007] = 42;
  const queue = [packet];
  const received: Uint8Array[] = [];
  const context = createContext({
    received,
    portableUi: {
      reserve: () => 1n,
      submit() {},
      nextEvent: () => new Uint8Array(),
      nextObservation: () => queue.shift() ?? new Uint8Array(),
      requestAction() {},
    },
  });
  runInContext(sdk.outputFiles[0].text, context);
  runInContext(
    'const stop=__portableReact.observe(7,1,b=>received.push(b));portableUiUpdate();__portableReact.observe(7,1,b=>received.push(b));stop();',
    context,
  );
  expect(received).toHaveLength(2);
  expect(received[0]).toBe(received[1]);
  expect(received[0].buffer).toBe(packet.buffer);
  expect(received[0].byteOffset).toBe(8);
  expect(received[0][99999]).toBe(42);
  const invalid = packet.slice();
  invalid[4] = 0;
  queue.push(invalid);
  expect(() => runInContext('portableUiUpdate()', context)).toThrow('envelope');
});
