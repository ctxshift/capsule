import { build } from 'esbuild';
import { readFileSync } from 'node:fs';
import { packageRoot, sourceRoot } from './package-paths.mjs';
import { resolve } from 'node:path';
export async function buildSdk({
  output = resolve('.local/react-host.js'),
  frontend = 'react',
} = {}) {
  if (!['react', 'bailout', 'hello'].includes(frontend))
    throw Error('Unknown frontend: ' + frontend);
  const options = {
    // Typecheck strictly; preserve the existing bundle's non-strict IIFE emission.
    tsconfigRaw: { compilerOptions: { alwaysStrict: false } },
    bundle: true,
    minify: true,
    format: 'iife',
    platform: 'browser',
    target: 'es2020',
    define: { 'process.env.NODE_ENV': '"production"' },
    outfile: output,
  };
  if (frontend === 'react')
    return build({ ...options, entryPoints: [resolve(sourceRoot, 'host.ts')] });
  if (frontend === 'hello')
    return build({
      ...options,
      entryPoints: [resolve(packageRoot, 'benchmarks/hello.ts')],
    });
  const backend = readFileSync(resolve(sourceRoot, 'host.ts'), 'utf8');
  const start = backend.indexOf('let previous =');
  if (start < 0) throw Error('Shared backend boundary missing');
  const prefix = 'react-bailout-adapter.ts';
  const contents =
    readFileSync(resolve(packageRoot, 'benchmarks', prefix), 'utf8') +
    '\n' +
    backend.slice(start);
  return build({
    ...options,
    stdin: { contents, resolveDir: sourceRoot, loader: 'ts' },
  });
}
if (import.meta.main) {
  const args = process.argv.slice(2);
  const option = (name, fallback) => {
    const index = args.indexOf(name);
    if (index < 0) return fallback;
    if (!args[index + 1]) throw Error(name + ' requires a value');
    return args[index + 1];
  };
  await buildSdk({
    output: resolve(option('--output', '.local/react-host.js')),
    frontend: option('--frontend', 'react'),
  });
}
