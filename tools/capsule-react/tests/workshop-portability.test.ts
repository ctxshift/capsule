import { test, expect } from 'vitest';
import {
  cp,
  mkdir,
  mkdtemp,
  readFile,
  realpath,
  rm,
  symlink,
  writeFile,
} from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, basename } from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { buildSdk } from '../scripts/build.mjs';
import { repositoryPath } from '../scripts/paths.mjs';
import { availablePort } from './available-port';

test('fictional host previews and reloads outside the source checkout', async () => {
  const root = await mkdtemp(join(tmpdir(), 'capsule-workshop-'));
  const packages = new Map([
    ['@capsule-sdk/react', 'packages/capsule-react'],
    ['@capsule-sdk/workshop', 'packages/capsule-workshop'],
    ['portable-demo-game', 'examples/portable-demo-game'],
  ]);
  try {
    for (const path of packages.values()) {
      const destination = join(root, basename(path));
      await cp(repositoryPath(path), destination, {
        recursive: true,
        filter: (path) => !['node_modules', 'dist'].includes(basename(path)),
      });
      const manifest = JSON.parse(
        await readFile(join(destination, 'package.json'), 'utf8'),
      );
      for (const dependency of Object.keys({
        ...manifest.dependencies,
        ...manifest.devDependencies,
        ...manifest.peerDependencies,
      })) {
        const target = packages.has(dependency)
          ? join(root, basename(packages.get(dependency)!))
          : await realpath(
              repositoryPath(path + '/node_modules/' + dependency),
            );
        const link = join(destination, 'node_modules', dependency);
        await mkdir(dirname(link), { recursive: true });
        await symlink(target, link, 'junction');
      }
    }
    await mkdir(join(root, 'node_modules', '@capsule-sdk'), {
      recursive: true,
    });
    await symlink(
      join(root, 'capsule-workshop'),
      join(root, 'node_modules', '@capsule-sdk', 'workshop'),
      'junction',
    );
    // The fixture receives the same generic runtime artifact a host would ship.
    await buildSdk({ output: join(root, 'sdk.js') });
    await writeFile(
      join(root, 'verify.ts'),
      `
import {startWorkshop} from '@capsule-sdk/workshop';
import {readFile,writeFile} from 'node:fs/promises';
import assert from 'node:assert/strict';
import {resolve} from 'node:path';
const game=resolve('portable-demo-game');
const source=resolve(game,'src/main.tsx');
const workshop=await startWorkshop({addonRoot:game,source,output:resolve('published'),
 browserRoot:resolve(game,'browser'),themeFile:resolve(game,'theme.json'),sdkFile:resolve('sdk.js'),port:${await availablePort()}});
try {
 const origin='http://127.0.0.1:'+workshop.server.httpServer.address().port;
 const publication=async()=> (await fetch(origin+'/portable-ui/package')).json();
 const first=await publication();
 assert.equal(first.compiler.success,true); assert.equal(first.theme.id,'demo.aster');
 assert(first.bundle.includes('Starship Aster'));
 assert.equal((await fetch(origin+'/')).status,200);
 const browser=await fetch(origin+'/main.ts'); assert.equal(browser.status,200);
 assert((await browser.text()).includes('BrowserGeneration'));
 const original=await readFile(source,'utf8');
 const waitFor=async(predicate)=>{const end=Date.now()+12000;while(Date.now()<end){
  const value=await publication();if(predicate(value))return value;
  await new Promise(resolve=>setTimeout(resolve,100));}throw Error('Reload deadline');};
 await writeFile(source,original.replace('Starship Aster','Starship Nova'));
 const next=await waitFor(value=>value.hash!==first.hash && value.compiler.success);
 assert(next.bundle.includes('Starship Nova'));
 await writeFile(source,original.replace('style="paragraph"','style="missing-theme-style"'));
 const invalidTheme=await waitFor(value=>value.compiler.error?.includes('Undeclared theme style'));
 assert.equal(invalidTheme.hash,next.hash); assert.equal(invalidTheme.bundle,next.bundle);
 assert(invalidTheme.compiler.error.includes('main.tsx:'));

 await writeFile(source,original.replace('width={360}','width="invalid"'));
 const rejected=await waitFor(value=>value.compiler.success===false);
 assert.equal(rejected.hash,next.hash); assert.equal(rejected.bundle,next.bundle);
 assert.equal((await fetch(origin+'/portable-ui/art/'+next.hash+'/ui-theme-revision')).status,404);
 const manifestPath=resolve(game,'addon.json');
 const manifest=JSON.parse(await readFile(manifestPath,'utf8'));
 manifest.assets=manifest.assets.filter(asset=>asset.kind!=='image');
 await writeFile(manifestPath,JSON.stringify(manifest));
 const privateReference=await waitFor(value=>value.compiler.error?.includes('undeclared package asset'));
 assert.equal(privateReference.hash,next.hash);
 console.log('isolated preview, valid reload and last-good rejection passed');
} finally {await workshop.close();}
`,
    );
    const executable = await promisify(execFile)(
      'bun',
      ['-p', 'process.execPath'],
      {
        windowsHide: true,
        timeout: 5000,
      },
    );
    // Launch the runtime directly so a deadline kills it rather than a mise shim.
    const result = await promisify(execFile)(
      executable.stdout.trim(),
      ['verify.ts'],
      {
        cwd: root,
        windowsHide: true,
        timeout: 35000,
      },
    );
    expect(result.stdout).toContain(
      'isolated preview, valid reload and last-good rejection passed',
    );
  } finally {
    await rm(root, { recursive: true, force: true });
  }
}, 45000);
