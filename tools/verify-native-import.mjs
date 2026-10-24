import { readFileSync, readdirSync } from 'node:fs';
import { resolve, relative, isAbsolute, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';

export function verifyImport(root, manifest) {
  root = resolve(root);
  const targets = new Set(), guids = new Set();
  let lineEndingTranslations = 0;
  for (const file of manifest.files) {
    const target = resolve(root, file.target);
    const local = relative(root, target);
    if (!local || local.startsWith('..') || isAbsolute(local))
      throw Error(`Import target escapes repository: ${file.target}`);
    if (targets.has(target)) throw Error(`Duplicate import target: ${file.target}`);
    targets.add(target);
    const bytes = readFileSync(target);
    const hash = createHash('sha256').update(bytes).digest('hex');
    if (hash !== file.sha256) {
      const canTranslate = /\.(cs|meta)$/.test(file.target) && file.canonicalSha256;
      const canonical = canTranslate && createHash('sha256')
        .update(bytes.toString('utf8').replaceAll('\r\n', '\n')).digest('hex');
      if (!canTranslate || canonical !== file.canonicalSha256) throw Error(`Changed import payload: ${file.target}`);
      lineEndingTranslations++;
    }
    if (file.guid) {
      const actual = bytes.toString('utf8').match(/^guid: (\w+)/m)?.[1];
      if (actual !== file.guid) throw Error(`Changed GUID: ${file.target}`);
      if (guids.has(actual)) throw Error(`Duplicate imported GUID: ${actual}`);
      guids.add(actual);
    }
  }
  return { files: targets.size, guids: guids.size, lineEndingTranslations };
}

export function verifyBoundaries(root) {
  const packages = resolve(root, 'unity/Packages');
  let checked = 0;
  for (const name of readdirSync(packages).filter(name => name.startsWith('com.capsule.'))) {
    const directory = resolve(packages, name);
    const manifest = JSON.parse(readFileSync(resolve(directory, 'package.json'), 'utf8'));
    for (const dependency of Object.keys(manifest.dependencies ?? {}))
      if (!dependency.startsWith('com.capsule.') && ![
        'com.unity.ugui', 'com.unity.inputsystem', 'com.unity.nuget.newtonsoft-json',
      ].includes(dependency)) throw Error(`Unexpected package dependency: ${dependency}`);
    const runtime = resolve(directory, 'Runtime');
    for (const file of readdirSync(runtime, { recursive: true }).filter(name => name.endsWith('.cs'))) {
      const text = readFileSync(resolve(runtime, file), 'utf8');
      if (/^\s*(?:using|namespace)\s+(?:Host|Myriad)\b/m.test(text))
        throw Error(`Game dependency in runtime: ${name}/${file}`);
    }
    checked++;
  }
  if (checked !== 5) throw Error(`Expected five native packages, found ${checked}`);
  return checked;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
  const manifest = JSON.parse(readFileSync(resolve(root, 'docs/provenance/native-import.json'), 'utf8'));
  const result = verifyImport(root, manifest);
  console.log(`Native import verified: ${result.files} files, ${result.guids} GUIDs, ${verifyBoundaries(root)} package boundaries; ${result.lineEndingTranslations} text line-ending translations`);
}
