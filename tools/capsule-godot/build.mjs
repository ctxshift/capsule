import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import {
  cpSync,
  copyFileSync,
  existsSync,
  mkdirSync,
  readFileSync,
  writeFileSync,
} from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { homedir } from 'node:os';
import { files, zipBundle } from '../release/lib.mjs';

const root = fileURLToPath(new URL('../../', import.meta.url));
const { values } = parseArgs({
  options: { output: { type: 'string' }, generator: { type: 'string' } },
});
if (process.platform !== 'win32' || !values.output)
  throw Error(
    'Requires Windows x64. Usage: node tools/capsule-godot/build.mjs --output <fresh-directory> [--generator <CMake-generator>]',
  );
const output = resolve(values.output);
if (existsSync(output))
  throw Error('Output must be fresh. Never overwrite a loaded addon.');
mkdirSync(output, { recursive: true });
const cache = join(root, '.local/godot-build');
mkdirSync(cache, { recursive: true });
let step = 0;
function run(executable, args) {
  const log = join(
    output,
    `${++step}-${executable.replaceAll(/[^a-z0-9]/gi, '-')}.log`,
  );
  try {
    const bytes = execFileSync(executable, args, {
      cwd: root,
      windowsHide: true,
      timeout: 600000,
      maxBuffer: 32 * 1024 * 1024,
    });
    writeFileSync(log, bytes);
    return bytes.toString().trim();
  } catch (error) {
    writeFileSync(
      log,
      Buffer.concat([
        error.stdout ?? Buffer.alloc(0),
        error.stderr ?? Buffer.alloc(0),
      ]),
    );
    throw Error(`${executable} failed; see ${log}`, { cause: error });
  }
}
const pin = '8070599885b6df675fcad033dd032ce44be2b850';
const cpp = join(cache, 'godot-cpp');
if (!existsSync(cpp)) {
  run('git', ['init', cpp]);
  run('git', [
    '-C',
    cpp,
    'remote',
    'add',
    'origin',
    'https://github.com/godotengine/godot-cpp.git',
  ]);
  run('git', ['-C', cpp, 'fetch', '--depth', '1', 'origin', pin]);
  run('git', ['-C', cpp, 'checkout', '--detach', 'FETCH_HEAD']);
}
if (
  run('git', ['-C', cpp, 'rev-parse', 'HEAD']) !== pin ||
  run('git', ['-C', cpp, 'status', '--porcelain'])
)
  throw Error('Godot C++ dependency must match its clean pinned revision.');
const build = join(cache, 'extension');
const configure = [
  '-S',
  join(root, 'tools/capsule-godot'),
  '-B',
  build,
  '-A',
  'x64',
  `-DCAPSULE_GODOT_CPP:PATH=${cpp}`,
];
if (values.generator) configure.push('-G', values.generator);
run('cmake', configure);
run('cmake', ['--build', build, '--config', 'Release', '--parallel', '4']);
const native = join(output, 'native');
run('dotnet', [
  'publish',
  'tools/capsule-host-native/Capsule.Host.csproj',
  '-c',
  'Release',
  '-p:RestoreLockedMode=true',
  '-o',
  native,
]);
const stage = join(output, 'bundle');
const addon = join(stage, 'addons/capsule');
cpSync(join(root, 'godot/addons/capsule'), addon, {
  recursive: true,
  filter: (name) => !name.replaceAll('\\', '/').includes('/bin'),
});
const bin = join(addon, 'bin');
mkdirSync(bin, { recursive: true });
const resources = {
  'capsule_godot.dll': join(build, 'Release/capsule_godot.dll'),
  'capsule_host.dll': join(native, 'capsule_host.dll'),
  'wasmtime.dll': join(
    root,
    'unity/Packages/com.capsule.wasmtime/Runtime/Plugins/x86_64/wasmtime.dll',
  ),
  'portable_ui_wasmtime.dll': join(
    root,
    'unity/Packages/com.capsule.wasmtime/Runtime/Plugins/x86_64/portable_ui_wasmtime.dll',
  ),
  'portable_ui_yoga.dll': join(
    root,
    'unity/Packages/com.capsule.yoga/Runtime/Plugins/x86_64/portable_ui_yoga.dll',
  ),
  'CapsuleQuickJs.bytes': join(
    root,
    'unity/Packages/com.capsule.wasmtime/Runtime/Carrier/CapsuleQuickJs.bytes',
  ),
  'CapsuleReactSdk.txt': join(
    root,
    'unity/Packages/com.capsule.wasmtime/Runtime/Resources/CapsuleReactSdk.txt',
  ),
};
for (const [name, source] of Object.entries(resources))
  copyFileSync(source, join(bin, name));
for (const name of ['capsule_godot.dll', 'capsule_host.dll']) {
  const bytes = readFileSync(join(bin, name));
  for (const prefix of [root.replace(/[\\/]$/, ''), homedir()]) {
    for (const variant of [
      prefix.replaceAll('\\', '/'),
      prefix.replaceAll('/', '\\'),
    ]) {
      if (
        bytes.includes(Buffer.from(variant)) ||
        bytes.includes(Buffer.from(variant, 'utf16le'))
      ) {
        throw Error(`Owned native binary embeds a local build path: ${name}`);
      }
    }
  }
}
copyFileSync(join(root, 'LICENSE'), join(addon, 'LICENSE'));
copyFileSync(join(root, 'THIRD-PARTY.md'), join(addon, 'THIRD-PARTY.md'));
copyFileSync(join(cpp, 'LICENSE.md'), join(addon, 'godot-cpp-LICENSE.md'));
cpSync(
  join(root, 'unity/Packages/com.capsule.wasmtime/ThirdPartyNotices'),
  join(addon, 'notices'),
  { recursive: true, filter: (name) => !name.endsWith('.meta') },
);
copyFileSync(
  join(
    root,
    'unity/Packages/com.capsule.wasmtime/Runtime/Wasmtime-LICENSE.txt',
  ),
  join(addon, 'notices/Wasmtime-LICENSE.txt'),
);
copyFileSync(
  join(
    root,
    'unity/Packages/com.capsule.yoga/Runtime/Plugins/x86_64/LICENSE.yoga.txt',
  ),
  join(addon, 'notices/Yoga-LICENSE.txt'),
);
const assets = JSON.parse(
  readFileSync(
    join(root, 'tools/capsule-host-native/obj/project.assets.json'),
    'utf8',
  ),
);
const packageCache = Object.keys(assets.packageFolders)[0];
const nativePack = assets.project.frameworks[
  'net10.0'
].downloadDependencies.find(
  (item) => item.name === 'Microsoft.NETCore.App.Runtime.NativeAOT.win-x64',
);
if (!nativePack || !/^\[([0-9.]+), \1\]$/.test(nativePack.version))
  throw Error('NativeAOT runtime must use one exact package version.');
const nativePackRoot = join(
  packageCache,
  nativePack.name.toLowerCase(),
  nativePack.version.match(/[0-9.]+/)[0],
);
copyFileSync(
  join(nativePackRoot, 'LICENSE.TXT'),
  join(addon, 'notices/DotNet-LICENSE.txt'),
);
copyFileSync(
  join(nativePackRoot, 'THIRD-PARTY-NOTICES.TXT'),
  join(addon, 'notices/DotNet-THIRD-PARTY-NOTICES.txt'),
);
copyFileSync(
  join(packageCache, 'newtonsoft.json/13.0.2/LICENSE.md'),
  join(addon, 'notices/Newtonsoft-LICENSE.md'),
);
const sha256 = (bytes) => createHash('sha256').update(bytes).digest('hex');
const manifest = {
  schemaVersion: 1,
  engine: 'Godot',
  minimumVersion: '4.7',
  platform: 'windows-x86_64',
  developmentOnly: true,
  sourceRevision: run('git', ['rev-parse', 'HEAD']),
  sourceDirty: !!run('git', ['status', '--porcelain']),
  godotCppRevision: pin,
  nativeAbi: 1,
  nativeAot: '.NET 10, self-contained',
  payload: Object.fromEntries(
    files(stage).map((name) => [name, sha256(readFileSync(join(stage, name)))]),
  ),
};
writeFileSync(
  join(stage, 'capsule-godot.json'),
  JSON.stringify(manifest, null, 2) + '\n',
);
const archive = join(output, 'capsule-godot-win-x64-0.1.0.zip');
const packed = zipBundle(stage, archive, '');
writeFileSync(
  join(output, 'archive.json'),
  JSON.stringify(packed, null, 2) + '\n',
);
console.log(JSON.stringify({ output, archive, sha256: packed.sha256 }));
