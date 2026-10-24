import { build } from 'esbuild';
import { createContext, runInContext } from 'node:vm';
import { expect, test } from 'vitest';
import { applyPacket, input, type UiNode } from '@capsule-sdk/workshop/packet';
import { repositoryPath } from '../scripts/paths.mjs';

test('focus changes preserve legacy enter-only callbacks and validate leave payloads', async () => {
  const sdk = await build({
    entryPoints: [repositoryPath('tools/capsule-react/src/host.ts')],
    bundle: true,
    write: false,
    minify: true,
    format: 'iife',
    platform: 'browser',
    define: { 'process.env.NODE_ENV': '"production"' },
  });
  let handle = 0n;
  let nodes = new Map<bigint, UiNode>();
  const events: Uint8Array[] = [];
  const calls: (boolean | string)[] = [];
  const context = createContext({
    calls,
    portableUi: {
      reserve: () => ++handle,
      submit: (bytes: Uint8Array) => {
        nodes = applyPacket(nodes, bytes);
        return 0;
      },
      nextEvent: () => events.shift() ?? new Uint8Array(),
      nextObservation: () => new Uint8Array(),
      requestAction: () => {},
    },
  });
  runInContext(sdk.outputFiles[0].text, context);
  runInContext(
    `const {React,Box,render}=__portableReact;
render(React.createElement(Box,{width:64,height:64,onFocus:()=>calls.push('legacy'),onFocusChange:value=>calls.push(value)}),{layout:'yoga'});
portableUiUpdate();`,
    context,
  );
  const node = [...nodes.values()][0];
  expect(node.events & 32).toBe(32);
  for (const payload of [[], [1, 0], [1, 1]]) {
    events.push(input(node.id, 32, payload));
    runInContext('portableUiUpdate();', context);
  }
  expect(calls).toEqual(['legacy', true, false, 'legacy', true]);
  events.push(input(node.id, 32, [1, 2]));
  expect(() => runInContext('portableUiUpdate();', context)).toThrow(
    'Invalid focus event',
  );
});
