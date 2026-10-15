import { transform } from 'esbuild';
import { basename } from 'node:path';

// These match the package loader. Chunking changes source transport only: one
// guest, React instance, capability grant and activation fuel allowance remain.
export const maxScriptBytes = 65536;
export const maxScriptModules = 8;
export const maxScriptTotalBytes = 262144;

export async function scriptChunks(files) {
  const scripts = files.filter((file) => file.path.endsWith('.js'));
  if (!scripts.length || scripts.length > maxScriptModules)
    throw Error('Script module count quota');
  const chunks = [];
  for (const file of scripts) {
    const name = basename(file.path);
    const converted = await transform(file.text, {
      format: 'cjs',
      target: 'es2020',
      minify: true,
      keepNames: true,
      sourcefile: name,
    });
    const contents = Buffer.from(
      `globalThis.__capsuleFactories??=Object.create(null);globalThis.__capsuleFactories[${JSON.stringify(name)}]=(module,exports,require)=>{${converted.code}\n};`,
    );
    chunks.push({ name, contents });
  }
  const contents = Buffer.from(`(()=>{
const factories=globalThis.__capsuleFactories,cache=Object.create(null);
delete globalThis.__capsuleFactories;
function require(name){
if(!name.startsWith('./'))throw Error('Undeclared module');
name=name.slice(2);
if(!Object.prototype.hasOwnProperty.call(factories,name))throw Error('Undeclared module');
if(cache[name])return cache[name].exports;
const module={exports:{}};cache[name]=module;
factories[name](module,module.exports,require);return module.exports;
}
require('./entry.js');
})();`);
  const total = chunks.reduce(
    (size, file) => size + file.contents.length,
    contents.length,
  );
  if (
    chunks.some((file) => file.contents.length > maxScriptBytes) ||
    contents.length > maxScriptBytes
  )
    throw Error('Script chunk exceeds 64 KiB');
  if (total > maxScriptTotalBytes)
    throw Error('Aggregate script source exceeds 256 KiB');
  return { chunks, bundle: { contents }, total };
}
