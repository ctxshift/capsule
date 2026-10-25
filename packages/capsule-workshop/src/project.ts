import {
  mkdir,
  readFile,
  readdir,
  lstat,
  copyFile,
  writeFile,
} from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { resolve, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { tmpdir } from 'node:os';
import { startWorkshop } from './server';
import { themePath, parseTheme } from './theme';

export interface ProjectConfig {
  schemaVersion: 1;
  id: string;
  source: string;
  theme: string;
  port: number;
}
const configName = 'capsule.project.json';
const digest = (bytes: Buffer) =>
  createHash('sha256').update(bytes).digest('hex');
async function json(path: string, data: unknown) {
  await writeFile(path, JSON.stringify(data, null, 2) + '\n', { flag: 'wx' });
}
function validate(config: ProjectConfig) {
  if (config.schemaVersion !== 1 || !/^[a-z][a-z0-9.-]{1,95}$/.test(config.id))
    throw Error('Invalid Capsule project identity');
  themePath(config.source);
  themePath(config.theme);
  if (!Number.isInteger(config.port) || config.port < 1 || config.port > 65535)
    throw Error('Invalid development port');
  return config;
}
export async function inspectProject(projectRoot: string) {
  projectRoot = resolve(projectRoot);
  if ((await lstat(projectRoot)).isSymbolicLink())
    throw Error('Project root must not be a symlink');
  const config = validate(
    JSON.parse(await readFile(join(projectRoot, configName), 'utf8')),
  );
  const manifest = JSON.parse(
    await readFile(join(projectRoot, 'addon.json'), 'utf8'),
  );
  if (
    manifest.id !== config.id ||
    manifest.apiVersion !== 1 ||
    manifest.entry !== config.source
  )
    throw Error('Project and addon manifests disagree');
  parseTheme(
    JSON.parse(await readFile(join(projectRoot, config.theme), 'utf8')),
  );
  await readFile(join(projectRoot, config.source));
  return { ...config, projectRoot };
}
export async function initProject(options: {
  projectRoot: string;
  id?: string;
  port?: number;
  packages?: string;
}) {
  const root = resolve(options.projectRoot);
  const config = validate({
    schemaVersion: 1,
    id: options.id ?? 'example.capsule',
    source: 'src/main.tsx',
    theme: 'theme.json',
    port: options.port ?? 5173,
  });
  try {
    const stat = await lstat(root);
    if (!stat.isDirectory() || stat.isSymbolicLink())
      throw Error('Project destination must be a directory');
    const entries = await readdir(root);
    if (entries.includes(configName))
      return { ...(await inspectProject(root)), changed: false };
    if (entries.length)
      throw Error(
        'Project destination is not empty; connect an existing Capsule project instead',
      );
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
  }
  const candidate: { name: string; file: string; sha256: string }[] = [];
  if (options.packages) {
    const release = JSON.parse(
      await readFile(
        join(resolve(options.packages), 'release-manifest.json'),
        'utf8',
      ),
    );
    if (
      release.schemaVersion !== 1 ||
      !Array.isArray(release.npm) ||
      release.npm.length !== 8
    )
      throw Error('Invalid local release candidate');
    for (const entry of release.npm) {
      if (
        !/^@capsule-sdk\/[a-z-]+$/.test(entry.name) ||
        !/^npm\/[a-z0-9.-]+\.tgz$/.test(entry.file)
      )
        throw Error('Unsafe local package identity/path');
      const bytes = await readFile(join(resolve(options.packages), entry.file));
      if (digest(bytes) !== entry.sha256)
        throw Error('Local package hash mismatch');
      candidate.push(entry);
    }
  }
  await mkdir(join(root, 'src'), { recursive: true });
  await mkdir(join(root, 'assets'));
  const starter = fileURLToPath(new URL('./starter/', import.meta.url));
  await copyFile(join(starter, 'main.tsx'), join(root, 'src/main.tsx'));
  await copyFile(join(starter, 'theme.json'), join(root, 'theme.json'));
  for (const file of ['font-metrics.json', 'neutral.png'])
    await copyFile(join(starter, 'assets', file), join(root, 'assets', file));
  const dependencies = {
    '@capsule-sdk/react': '0.1.0',
    '@capsule-sdk/workshop': '0.1.0',
    '@capsule-sdk/react-tools': '0.1.0',
    react: '18.3.1',
  };
  const overrides: Record<string, string> = {};
  if (candidate.length) {
    await mkdir(join(root, '.capsule/packages'), { recursive: true });
    for (const entry of candidate) {
      const name = entry.file.slice(4);
      await copyFile(
        join(resolve(options.packages!), entry.file),
        join(root, '.capsule/packages', name),
      );
      overrides[entry.name] = 'file:./.capsule/packages/' + name;
    }
  }
  await json(join(root, 'package.json'), {
    name: config.id,
    version: '0.0.0',
    private: true,
    type: 'module',
    scripts: { dev: 'capsule-workshop dev --project .' },
    dependencies,
    ...(candidate.length ? { overrides } : {}),
  });
  await json(join(root, configName), config);
  await json(join(root, 'addon.json'), {
    schemaVersion: 1,
    apiVersion: 1,
    id: config.id,
    entry: config.source,
    assets: [
      { kind: 'data', key: 'ui-theme-revision', path: 'theme.json' },
      { kind: 'data', key: 'font-metrics', path: 'assets/font-metrics.json' },
      { kind: 'image', key: 'neutral', path: 'assets/neutral.png' },
    ],
  });
  await json(join(root, 'tsconfig.json'), {
    compilerOptions: {
      strict: true,
      noEmit: true,
      jsx: 'react',
      module: 'ESNext',
      moduleResolution: 'Bundler',
      target: 'ES2022',
      esModuleInterop: true,
    },
    include: ['src/**/*.tsx'],
  });
  await writeFile(join(root, '.gitignore'), 'node_modules/\n.capsule/\n', {
    flag: 'wx',
  });
  return { ...config, projectRoot: root, changed: true };
}
export async function startProject(projectRoot: string, port?: number) {
  const project = await inspectProject(projectRoot);
  if (port !== undefined) validate({ ...project, port });
  const require = createRequire(join(project.projectRoot, 'package.json'));
  const tools = require.resolve('@capsule-sdk/react-tools/sdk');
  const { buildSdk } = await import(pathToFileURL(tools).href);
  const cache = join(project.projectRoot, '.capsule');
  await mkdir(cache, { recursive: true });
  const sdk = join(cache, 'sdk.js');
  await buildSdk({ output: sdk });
  const workshop = await startWorkshop({
    addonRoot: project.projectRoot,
    source: join(project.projectRoot, project.source),
    output: join(
      tmpdir(),
      'capsule-publication-' +
        digest(Buffer.from(project.projectRoot)).slice(0, 24),
    ),
    browserRoot: project.projectRoot,
    themeFile: join(project.projectRoot, project.theme),
    sdkFile: sdk,
    port: port ?? project.port,
  });
  return {
    ...workshop,
    projectId: project.id,
    endpoint: 'http://127.0.0.1:' + (port ?? project.port),
  };
}
