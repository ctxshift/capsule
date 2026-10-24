import { build } from 'esbuild';
import { sourceRoot } from './package-paths.mjs';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
const base = sourceRoot;
let source = readFileSync(resolve(base, 'host.ts'), 'utf8').replaceAll(
  '\r\n',
  '\n',
);
function replace(from, to) {
  if (!source.includes(from))
    throw Error('Missing instrumentation site: ' + from);
  source = source.replace(from, to);
}
replace(
  'type Node =',
  `let renders=0, notices=0, subscriptions=0, cleanups=0;
const profile=(id:number)=>native.requestAction(0n,999,new Uint8Array([id,renders,notices,subscriptions,cleanups]));
type Node =`,
);
replace('  const [, refresh]', '  renders++;\n  const [, refresh]');
replace('    const notify = () => {', '    const notify = () => { notices++;');
replace(
  '    subscribers.add(notify);',
  '    subscriptions++; subscribers.add(notify);',
);
replace(
  '      subscribers!.delete(notify);',
  '      cleanups++; subscribers!.delete(notify);',
);
replace(
  '    const bytes = native.nextObservation();',
  '    profile(10); const bytes = native.nextObservation(); profile(11);',
);
replace(
  '    renderer.flushSync(() => {\n      for (const notify of [...(listeners.get(key) ?? [])]) notify();\n    });',
  '    profile(12); renderer.flushSync(() => {\n      for (const notify of [...(listeners.get(key) ?? [])]) notify();\n    }); profile(13);',
);
replace(
  '  const writer = packetWriter;',
  '  profile(14); const writer = packetWriter;',
);
replace(
  '  const next = new Map<bigint, any>();',
  '  profile(17); const next = new Map<bigint, any>();',
);
replace(
  '      renderer.flushSync(() => {',
  '      profile(20); renderer.flushSync(() => { profile(23);',
);
replace(
  '      });\n    }\n  }\n}\nconst observations',
  '        profile(24);\n      }); profile(21);\n    }\n  }\n}\nconst observations',
);
replace(
  '  writer.view.setUint32(0,',
  '  profile(15); writer.view.setUint32(0,',
);
replace(
  '  const bytes = writer.bytes.subarray(0, writer.offset);',
  '  profile(16); const bytes = writer.bytes.subarray(0, writer.offset);',
);
replace(
  '  native.submit(bytes);',
  '  profile(8); native.submit(bytes); profile(9);',
);
replace(
  '  pump();\n  renderer.flushPassiveEffects();\n  receiveObservations();\n  events();\n  renderer.flushPassiveEffects();\n  commit();',
  '  profile(1); profile(30); profile(31); pump(); profile(2);\n  renderer.flushPassiveEffects(); profile(3);\n  receiveObservations(); profile(4);\n  events(); profile(5);\n  renderer.flushPassiveEffects(); profile(6);\n  commit(); profile(7);',
);
await build({
  // Typecheck strictly; preserve the existing bundle's non-strict IIFE emission.
  tsconfigRaw: { compilerOptions: { alwaysStrict: false } },
  stdin: { contents: source, loader: 'ts', resolveDir: base },
  bundle: true,
  minify: true,
  format: 'iife',
  platform: 'browser',
  target: 'es2020',
  define: { 'process.env.NODE_ENV': '"production"' },
  outfile: resolve(base, '../../../.local/react-profile-host.js'),
});
