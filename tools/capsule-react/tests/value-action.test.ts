import { build } from 'esbuild';
import { createContext, runInContext } from 'node:vm';
import { expect, test } from 'vitest';
import { applyPacket, input, type UiNode } from '@capsule-sdk/workshop/packet';
import { repositoryPath } from '../scripts/paths.mjs';

test('range and select value callbacks carry the original control request handle', async () => {
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
  const requests: unknown[] = [];
  const context = createContext({
    portableUi: {
      reserve: () => ++handle,
      submit: (bytes: Uint8Array) => {
        nodes = applyPacket(nodes, bytes);
        return 0;
      },
      nextEvent: () => events.shift() ?? new Uint8Array(),
      nextObservation: () => new Uint8Array(),
      requestAction: (id: bigint, capability: number, bytes: Uint8Array) =>
        requests.push([id, capability, [...bytes]]),
    },
  });
  runInContext(sdk.outputFiles[0].text, context);
  runInContext(
    `const {React,Box,Range,Select,render}=__portableReact;render(React.createElement(Box,{},
    React.createElement(Range,{value:1,onValueChange:(v,e)=>e.request(213,new Uint8Array([1,Math.round(v*100)]))}),
    React.createElement(Select,{options:['Off','On'],selectedIndex:1,onSelectionChange:(v,e)=>e.request(213,new Uint8Array([2,v]))})),{layout:'yoga'});portableUiUpdate();`,
    context,
  );
  const controls = [...nodes.values()].filter((n) => (n.events & 256) !== 0);
  expect(controls).toHaveLength(2);
  events.push(input(controls[0].id, 256, [1, 0x88, 0x13]));
  events.push(input(controls[1].id, 256, [1, 0, 0]));
  runInContext('portableUiUpdate();', context);
  expect(requests).toEqual([
    [controls[0].id, 213, [1, 50]],
    [controls[1].id, 213, [2, 0]],
  ]);
});
