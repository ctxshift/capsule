import { build } from 'esbuild';
import ts from 'typescript';
import { createHash } from 'node:crypto';
import * as fs from 'node:fs/promises';
import { resolve, dirname, join, relative, isAbsolute } from 'node:path';
import { TraceMap, originalPositionFor } from '@jridgewell/trace-mapping';
import { capsuleEntry, capsuleSourceRoot, reactTypes } from './sdk-paths.mjs';
import { scriptChunks } from './script-chunks.mjs';
import { validatePackageTheme } from './package-theme.ts';
import { validateThemeReferences } from './theme-references.mjs';
import { featureRequirements } from './feature-requirements.mjs';
const packageRoot = resolve(import.meta.dirname, '..');
const hash = (bytes) => createHash('sha256').update(bytes).digest('hex');
const exists = async (path) => {
  try {
    await fs.access(path);
    return true;
  } catch {
    return false;
  }
};
const within = (root, path) => {
  const part = relative(root, path);
  return part !== '' && !part.startsWith('..') && !isAbsolute(part);
};
const names = [
  'Box',
  'Button',
  'Range',
  'Select',
  'Text',
  'Image',
  'Portrait',
  'Viewport',
  'Slot',
  'Window',
  'Progress',
  'Overlay',
  'render',
  'useObservation',
];

/**
 * @param {{source: string, output: string, assets?: string | null,
 * runtimeStatus?: string | null, beforePublish?: (() => Promise<unknown>) | null,
 * watchRoots?: string[], projectRoot?: string, manifest?: string, maxDeclarations?: number,
 * themeLimits?: import('./theme').ThemeLimits}} options
 */
export function compiler({
  source,
  output,
  assets = null,
  runtimeStatus = null,
  beforePublish = null,
  watchRoots = [],
  projectRoot,
  manifest,
  maxDeclarations = 30,
  themeLimits,
}) {
  if (!Number.isSafeInteger(maxDeclarations) || maxDeclarations < 1)
    throw Error('Host manifest profile bounds');
  source = resolve(source);
  output = resolve(output);
  const root = projectRoot ? resolve(projectRoot) : dirname(source),
    parent = dirname(output);
  if (!within(root, source)) throw Error('Entry must stay in project root');
  if (manifest && !within(root, resolve(manifest)))
    throw Error('Manifest must stay in project root');
  const roots = [
    ...new Set([
      root,
      capsuleSourceRoot,
      ...watchRoots.map((path) => resolve(path)),
    ]),
  ];
  if (
    output === parent ||
    roots.some(
      (path) => output === path || within(output, path) || within(path, output),
    )
  )
    throw Error('Output must be a separate development package directory');
  if (assets) {
    assets = resolve(assets);
    if (!within(root, assets))
      throw Error('Asset manifest must stay in watched source root');
  }
  const owner = output + '.owner.json',
    status = output + '.build-status.json',
    request = output + '.reload-request';
  const identity = JSON.stringify({ schema: 1, source, output });
  let revision = 0;
  async function renamePublished(from, to) {
    // Windows readers may briefly omit FileShare.Delete. Retry only sharing/
    // access conflicts within a finite deadline; other failures stay explicit.
    const deadline = Date.now() + 2000;
    while (true) {
      try {
        await fs.rename(from, to);
        return;
      } catch (error) {
        if (
          !['EPERM', 'EACCES', 'EBUSY'].includes(error.code) ||
          Date.now() >= deadline
        )
          throw error;
        await new Promise((resolve) => setTimeout(resolve, 20));
      }
    }
  }
  async function atomic(path, value) {
    await fs.writeFile(path + '.tmp', JSON.stringify(value, null, 2));
    await renamePublished(path + '.tmp', path);
  }
  async function owned() {
    if (await exists(owner)) {
      if ((await fs.readFile(owner, 'utf8')) !== identity)
        throw Error('Output owner does not match this source');
    } else {
      if (await exists(output))
        throw Error(
          'Existing output is not compiler-owned; choose a new directory',
        );
      await fs.mkdir(parent, { recursive: true });
      await fs.writeFile(owner, identity, { flag: 'wx' });
    }
  }
  async function fingerprint() {
    const paths = [];
    let entries = 0;
    async function visit(dir) {
      for (const entry of await fs.readdir(dir, { withFileTypes: true })) {
        if (['node_modules', '.git', 'dist'].includes(entry.name)) continue;
        const path = join(dir, entry.name);
        if (entry.isSymbolicLink())
          throw Error('Source symlinks are not supported');
        if (++entries > 512) throw Error('Source watch entry quota');
        if (entry.isDirectory()) await visit(path);
        else paths.push(path);
      }
    }
    for (const path of roots) {
      if ((await fs.lstat(path)).isSymbolicLink())
        throw Error('Source symlinks are not supported');
      await visit(path);
    }
    if (assets && !paths.includes(assets)) paths.push(assets);
    paths.push(request);
    paths.sort();
    const hashes = [];
    for (const path of paths) {
      try {
        hashes.push(path + ':' + hash(await fs.readFile(path)));
      } catch {
        hashes.push(path + ':missing');
      }
    }
    return hash(hashes.join('\n'));
  }
  async function clean(path) {
    if (
      dirname(path) !== parent ||
      (!path.startsWith(output + '.stage-') &&
        !path.startsWith(output + '.previous-'))
    )
      throw Error('Cleanup escaped generated sibling');
    if (await exists(path)) await fs.rm(path, { recursive: true });
  }
  const shim = {
    name: 'host-imports',
    setup(builder) {
      builder.onResolve(
        { filter: /^@capsule-sdk\/react(?:\/.*)?$/ },
        (args) => {
          const entries = {
            '@capsule-sdk/react': capsuleEntry,
            '@capsule-sdk/react/panel': resolve(capsuleSourceRoot, 'panel.tsx'),
            '@capsule-sdk/react/scroll-list': resolve(
              capsuleSourceRoot,
              'scroll-list.tsx',
            ),
            '@capsule-sdk/react/components': resolve(
              capsuleSourceRoot,
              'components.tsx',
            ),
            '@capsule-sdk/react/window-state': resolve(
              capsuleSourceRoot,
              'window-state.ts',
            ),
          };
          return entries[args.path]
            ? { path: entries[args.path] }
            : {
                errors: [{ text: 'Unknown Capsule SDK export: ' + args.path }],
              };
        },
      );
      builder.onResolve(
        { filter: /^(react|@portable-ui\/react)$/ },
        (args) => ({ path: args.path, namespace: 'host' }),
      );
      builder.onLoad({ filter: /.*/, namespace: 'host' }, (args) => ({
        contents:
          args.path === 'react'
            ? 'const React=globalThis.__portableReact.React;export default React;' +
              [
                'useState',
                'useEffect',
                'useMemo',
                'useCallback',
                'useRef',
                'useReducer',
                'useLayoutEffect',
                'useSyncExternalStore',
                'Fragment',
                'createElement',
              ]
                .map((n) => `export const ${n}=React.${n};`)
                .join('')
            : names
                .map(
                  (n) => `export const ${n}=globalThis.__portableReact.${n};`,
                )
                .join(''),
        loader: 'js',
      }));
      builder.onResolve({ filter: /^\./ }, (args) => {
        if (
          args.importer &&
          !roots.some((path) =>
            within(path, resolve(args.resolveDir, args.path)),
          )
        )
          return {
            errors: [
              {
                text: 'Relative imports must stay inside watched source roots',
              },
            ],
          };
      });
    },
  };
  function typecheck() {
    const config = ts.readConfigFile(
      resolve(packageRoot, 'tsconfig.addon.json'),
      ts.sys.readFile,
    );
    if (config.error)
      throw Error(
        ts.flattenDiagnosticMessageText(config.error.messageText, ' '),
      );
    const parsed = ts.parseJsonConfigFileContent(
      config.config,
      ts.sys,
      packageRoot,
    );
    if (parsed.errors.length)
      throw Error(
        parsed.errors
          .map((error) =>
            ts.flattenDiagnosticMessageText(error.messageText, ' '),
          )
          .join('\n'),
      );
    const program = ts.createProgram(
      [source, resolve(packageRoot, 'src/legacy.d.ts')],
      {
        ...parsed.options,
        typeRoots: [dirname(dirname(reactTypes))],
        paths: {
          react: [reactTypes],
          '@capsule-sdk/react': [capsuleEntry],
          '@capsule-sdk/react/panel': [resolve(capsuleSourceRoot, 'panel.tsx')],
          '@capsule-sdk/react/scroll-list': [
            resolve(capsuleSourceRoot, 'scroll-list.tsx'),
          ],
          '@capsule-sdk/react/components': [
            resolve(capsuleSourceRoot, 'components.tsx'),
          ],
          '@capsule-sdk/react/window-state': [
            resolve(capsuleSourceRoot, 'window-state.ts'),
          ],
        },
      },
    );
    const errors = ts.getPreEmitDiagnostics(program);
    if (errors.length)
      throw Error(
        errors
          .map((d) => {
            const pos = d.file?.getLineAndCharacterOfPosition(d.start ?? 0);
            return `${d.file?.fileName || source}:${(pos?.line ?? 0) + 1}:${(pos?.character ?? 0) + 1}: ${ts.flattenDiagnosticMessageText(d.messageText, ' ')}`;
          })
          .join('\n'),
      );
    return program;
  }
  async function compile() {
    await owned();
    const before = await fingerprint(),
      current = ++revision;
    const stage = output + `.stage-${process.pid}-${current}`,
      backup = output + `.previous-${process.pid}-${current}`;
    try {
      const program = typecheck();
      const sourceManifest = manifest
        ? JSON.parse(await fs.readFile(manifest, 'utf8'))
        : null;
      const split = sourceManifest?.scriptEntries;
      if (
        split &&
        (!Array.isArray(split) ||
          split.length > 7 ||
          split.some(
            (path) =>
              typeof path !== 'string' || !within(root, resolve(root, path)),
          ))
      )
        throw Error('Invalid script entry boundaries');
      const result = await build({
        // Typecheck strictly; preserve the existing bundle's non-strict IIFE emission.
        tsconfigRaw: { compilerOptions: { alwaysStrict: false } },
        entryPoints: split
          ? {
              entry: source,
              ...Object.fromEntries(
                split.map((path, index) => [
                  'module' + index,
                  resolve(root, path),
                ]),
              ),
            }
          : [source],
        bundle: true,
        // Compact generated transport; source maps retain the authored source.
        minifyWhitespace: true,
        minifySyntax: true,
        minifyIdentifiers: true,
        keepNames: true,
        write: false,
        metafile: true,
        format: split ? 'esm' : 'iife',
        splitting: !!split,
        platform: 'browser',
        target: 'es2020',
        jsx: 'transform',
        plugins: [shim],
        sourcemap: 'external',
        sourcesContent: true,
        ...(split
          ? { outdir: 'chunks', chunkNames: 'shared-[hash]' }
          : { outfile: 'bundle.js' }),
        logLevel: 'silent',
      });
      if (
        split &&
        Object.values(result.metafile.outputs).some((output) =>
          output.imports.some((item) => item.kind === 'dynamic-import'),
        )
      )
        throw Error('Script chunks require static imports');
      const modular = split ? await scriptChunks(result.outputFiles) : null;
      const bundle =
        modular?.bundle ??
        result.outputFiles.find((f) => f.path.endsWith('bundle.js'));
      if (bundle.contents.length > 65536)
        throw Error(
          `Addon bundle exceeds 64 KiB (${bundle.contents.length} bytes)`,
        );
      if (
        sourceManifest &&
        (sourceManifest.schemaVersion !== 1 ||
          sourceManifest.apiVersion !== 1 ||
          typeof sourceManifest.id !== 'string')
      )
        throw Error('Invalid source manifest');
      const declarations = [
        ...(sourceManifest
          ? sourceManifest.assets
          : assets
            ? JSON.parse(await fs.readFile(assets, 'utf8'))
            : []),
      ];
      if (modular)
        for (const chunk of modular.chunks)
          declarations.push({
            kind: 'script',
            key: chunk.name,
            path: 'chunks/' + chunk.name,
          });
      if (!Array.isArray(declarations) || declarations.length > maxDeclarations)
        throw Error('Invalid asset declaration count');
      await fs.mkdir(stage);
      await fs.writeFile(join(stage, 'bundle.js'), bundle.contents);
      for (const asset of declarations) {
        if (asset.kind === 'script') {
          const chunk = modular?.chunks.find(
            (file) => 'chunks/' + file.name === asset.path,
          );
          if (!chunk)
            throw Error('Script declarations must be compiler generated');
          await fs.mkdir(join(stage, 'chunks'), { recursive: true });
          await fs.writeFile(join(stage, asset.path), chunk.contents);
          continue;
        }
        const assetRoot = manifest ? root : dirname(assets);
        const from = resolve(assetRoot, asset.path),
          destination = resolve(stage, asset.path);
        if (!within(assetRoot, from) || !within(stage, destination))
          throw Error('Asset path escapes source or stage');
        await fs.mkdir(dirname(destination), { recursive: true });
        await fs.copyFile(from, destination);
      }
      await fs.writeFile(
        join(stage, 'addon.json'),
        JSON.stringify({
          schemaVersion: 1,
          apiVersion: 1,
          id: sourceManifest?.id ?? 'portable.react.fixture',
          entry: 'bundle.js',
          assets: declarations,
          ...(sourceManifest?.features !== undefined
            ? { features: featureRequirements(sourceManifest.features) }
            : {}),
        }),
      );
      const theme = await validatePackageTheme(stage, themeLimits);
      validateThemeReferences(program, theme, root);
      if (beforePublish) await beforePublish();
      if (before !== (await fingerprint())) {
        await clean(stage);
        return { fingerprint: before, obsolete: true };
      }
      const bundleHash = hash(bundle.contents),
        maps = output + '.maps';
      await fs.mkdir(maps, { recursive: true });
      if (!modular)
        await fs.writeFile(
          join(maps, bundleHash + '.json'),
          result.outputFiles.find((f) => f.path.endsWith('.map')).contents,
        );
      else {
        // Carrier stack traces still use bundle.js. Never map those locations
        // against an unrelated chunk; retain build maps for offline inspection.
        await fs.writeFile(
          join(maps, bundleHash + '.json'),
          JSON.stringify({ modular: true }),
        );
        for (const file of result.outputFiles.filter((file) =>
          file.path.endsWith('.map'),
        ))
          await fs.writeFile(
            join(maps, hash(file.contents) + '.map'),
            file.contents,
          );
      }
      if (await exists(output)) await renamePublished(output, backup);
      try {
        await renamePublished(stage, output);
      } catch (error) {
        if (await exists(backup)) await renamePublished(backup, output);
        throw error;
      }
      await clean(backup);
      await atomic(status, {
        revision: current,
        success: true,
        error: '',
        source,
        bundleSha256: bundleHash,
        fingerprint: before,
      });
      console.log(
        `Published ${current}: ${modular?.total ?? bundle.contents.length} bytes${modular ? ' across ' + modular.chunks.length + ' script chunks' : ''}`,
      );
      return { fingerprint: before, success: true, bundleHash };
    } catch (error) {
      const detail =
        error.errors
          ?.map(
            (e) =>
              `${e.location?.file || source}:${e.location?.line || 0}:${e.location?.column || 0}: ${e.text}`,
          )
          .join('\n') || error.message;
      await atomic(status, {
        revision: current,
        success: false,
        error: detail,
        source,
      });
      await clean(stage);
      return { fingerprint: before, success: false, error: detail };
    }
  }
  async function mapRuntime() {
    if (!runtimeStatus || !(await exists(runtimeStatus))) return;
    try {
      const state = JSON.parse(await fs.readFile(runtimeStatus, 'utf8')),
        raw = state.error || '';
      const locations = [...raw.matchAll(/bundle\.js:(\d+):(\d+)/g)];
      if (!locations.length || !state.diagnosticBundleSha256) return;
      const mapData = JSON.parse(
        await fs.readFile(
          join(output + '.maps', state.diagnosticBundleSha256 + '.json'),
          'utf8',
        ),
      );
      if (mapData.modular) {
        await atomic(output + '.runtime-diagnostic.json', { raw, error: raw });
        return;
      }
      const map = new TraceMap(mapData);
      let mapped = raw;
      for (const match of locations) {
        const pos = originalPositionFor(map, {
          line: +match[1],
          column: Math.max(0, +match[2] - 1),
        });
        if (pos.source)
          mapped = mapped.replace(
            match[0],
            `${pos.source}:${pos.line}:${(pos.column ?? 0) + 1}`,
          );
      }
      await atomic(output + '.runtime-diagnostic.json', { raw, error: mapped });
    } catch {
      /* A partially replaced runtime status is retried next tick. */
    }
  }
  return { compile, fingerprint, mapRuntime };
}
