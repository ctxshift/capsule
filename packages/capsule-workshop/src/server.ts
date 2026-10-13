import { createServer, type ViteDevServer } from 'vite';
import { readFile, stat, lstat } from 'node:fs/promises';
import { resolve, dirname, relative, isAbsolute } from 'node:path';
import { createHash } from 'node:crypto';
import { compiler } from './compiler.mjs';
import { parseTheme, scopedTheme, themePath } from './theme';
import { capsuleSourceRoot } from './sdk-paths.mjs';

export interface WorkshopOptions {
  source: string;
  output: string;
  addonRoot: string;
  browserRoot: string;
  themeFile: string;
  sdkFile: string;
  runtimeStatus?: string;
  port?: number;
  watchRoots?: string[];
}
export async function startWorkshop(options: WorkshopOptions) {
  const source = resolve(options.source);
  const output = resolve(options.output);
  const runtimeStatus = options.runtimeStatus ?? output + '.native-status.json';
  const roots = [
    resolve(options.addonRoot),
    capsuleSourceRoot,
    ...(options.watchRoots ?? []),
  ];
  const worker = compiler({
    source,
    output,
    runtimeStatus,
    projectRoot: roots[0],
    watchRoots: roots.slice(1),
    manifest: resolve(roots[0], 'addon.json'),
  });
  let publication: {
    bundle: string;
    sdk: string;
    hash: string;
    theme: ReturnType<typeof parseTheme>;
    metrics: unknown;
  } | null = null;
  const images = new Map<string, Map<string, Uint8Array>>();
  let result: Awaited<ReturnType<typeof worker.compile>> | null = null;
  let compiling = false,
    requested = false,
    closed = false;
  let debounce: ReturnType<typeof setTimeout> | null = null;
  async function publish() {
    if (compiling) {
      requested = true;
      return;
    }
    compiling = true;
    try {
      do {
        requested = false;
        const themeFile = resolve(options.themeFile);
        let theme = parseTheme(JSON.parse(await readFile(themeFile, 'utf8')));
        const providerRoot = dirname(themeFile);
        const provider = JSON.parse(
          await readFile(resolve(providerRoot, 'addon.json'), 'utf8'),
        ) as {
          assets: { key: string; kind: string; path: string }[];
        };
        const sourceBytes = new Map<string, Buffer>();
        const readAsset = async (path: string, kind: string, key?: string) => {
          const safe = themePath(path);
          if (
            !Array.isArray(provider.assets) ||
            !provider.assets.some(
              (asset) =>
                asset.path === safe &&
                asset.kind === kind &&
                (!key || asset.key === key),
            )
          )
            throw Error('Theme references an undeclared package asset');
          const cached = sourceBytes.get(safe);
          if (cached) return cached;
          let full = providerRoot;
          for (const part of safe.split('/')) {
            full = resolve(full, part);
            if ((await lstat(full)).isSymbolicLink())
              throw Error('Theme symlinks are not supported');
          }
          const bytes = await readFile(full);
          sourceBytes.set(safe, bytes);
          return bytes;
        };
        await readAsset(
          relative(providerRoot, themeFile).replaceAll('\\', '/'),
          'data',
          'ui-theme-revision',
        );
        const metrics = JSON.parse(
          (await readAsset(theme.font.metrics, 'data')).toString(),
        );
        const art = new Map<string, Uint8Array>();
        for (const asset of theme.assets)
          art.set(
            asset.image ?? asset.key,
            await readAsset(asset.path, 'image', asset.image ?? asset.key),
          );
        result = await worker.compile();
        if (result.success) {
          const manifest = JSON.parse(
            await readFile(resolve(output, 'addon.json'), 'utf8'),
          ) as { assets: { key: string; kind: string; path: string }[] };
          const override = manifest.assets.find(
            (asset) =>
              asset.key === 'ui-theme-overrides' && asset.kind === 'data',
          );
          if (override)
            theme = scopedTheme(
              theme,
              JSON.parse(
                await readFile(
                  resolve(output, themePath(override.path)),
                  'utf8',
                ),
              ),
            );
          const bundle = await readFile(resolve(output, 'bundle.js'), 'utf8');
          const sdk = await readFile(resolve(options.sdkFile), 'utf8');
          const digest = createHash('sha256')
            .update(bundle)
            .update(sdk)
            .update(JSON.stringify(theme))
            .update(JSON.stringify(metrics));
          for (const [key, bytes] of art) digest.update(key).update(bytes);
          const hash = digest.digest('hex');
          publication = { bundle, sdk, hash, theme, metrics };
          images.set(hash, art);
          while (images.size > 2) images.delete(images.keys().next().value!);
        }
        if (result.obsolete) requested = true;
        else
          server.ws.send({
            type: 'custom',
            event: 'portable-ui:publication',
            data: result,
          });
      } while (requested && !closed);
    } catch (error) {
      result = {
        success: false,
        error: error instanceof Error ? error.message : String(error),
        fingerprint: '',
      };
      server.ws.send({
        type: 'custom',
        event: 'portable-ui:publication',
        data: result,
      });
    } finally {
      compiling = false;
    }
  }
  const server: ViteDevServer = await createServer({
    configFile: false,
    root: resolve(options.browserRoot),
    server: {
      host: '127.0.0.1',
      port: options.port ?? 5173,
      strictPort: true,
      fs: {
        allow: [
          resolve(options.browserRoot),
          resolve(import.meta.dirname, '..'),
          capsuleSourceRoot,
        ],
      },
    },
    optimizeDeps: { exclude: ['yoga-layout'] },
    build: { target: 'esnext' },
    plugins: [
      {
        name: 'portable-ui-workshop',
        configureServer(server) {
          server.middlewares.use(async (request, response, next) => {
            const path = request.url?.split('?')[0];
            try {
              const match = path?.match(
                /^\/portable-ui\/art\/([a-f0-9]{64})\/([a-z0-9-]+)$/,
              );
              if (match) {
                const bytes = images.get(match[1])?.get(match[2]);
                if (!bytes) {
                  response.statusCode = 404;
                  response.end();
                  return;
                }
                response.setHeader('Content-Type', 'image/png');
                response.setHeader(
                  'Cache-Control',
                  'public,max-age=31536000,immutable',
                );
                response.end(bytes);
                return;
              }
              if (path === '/portable-ui/package') {
                if (!publication)
                  throw Error('No successful addon publication');
                response.setHeader('Content-Type', 'application/json');
                response.setHeader('Cache-Control', 'no-store');
                response.end(
                  JSON.stringify({ ...publication, compiler: result }),
                );
                return;
              }
              if (path === '/portable-ui/status') {
                let native = null;
                try {
                  const metadata = await stat(runtimeStatus);
                  if (metadata.size > 262144)
                    throw Error('Native status quota');
                  native = {
                    connected: Date.now() - metadata.mtimeMs < 5000,
                    report: JSON.parse(await readFile(runtimeStatus, 'utf8')),
                  };
                } catch {
                  /* Game is optional. */
                }
                await worker.mapRuntime();
                response.setHeader('Content-Type', 'application/json');
                response.setHeader('Cache-Control', 'no-store');
                response.end(
                  JSON.stringify({ compiler: result, native, output, source }),
                );
                return;
              }
              next();
            } catch (error) {
              response.statusCode = 503;
              response.end(
                JSON.stringify({
                  error: error instanceof Error ? error.message : String(error),
                }),
              );
            }
          });
        },
      },
    ],
  });
  try {
    await server.listen();
  } catch (error) {
    await server.close();
    throw error;
  }
  const watchedRoots = [
    ...roots,
    dirname(resolve(options.themeFile)),
    resolve(options.sdkFile),
  ];
  server.watcher.add(watchedRoots);
  server.watcher.on('all', (_event, path) => {
    const watched = watchedRoots.some((root) => {
      const part = relative(root, path);
      return !part.startsWith('..') && !isAbsolute(part);
    });
    if (!watched || closed) return;
    if (debounce) clearTimeout(debounce);
    debounce = setTimeout(() => {
      void publish().catch((error) => {
        console.error(error);
      });
    }, 100);
  });
  await publish();
  const close = async () => {
    closed = true;
    if (debounce) clearTimeout(debounce);
    await server.close();
  };
  return { server, close, output, runtimeStatus, source };
}
