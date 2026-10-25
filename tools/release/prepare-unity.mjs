import {
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { checkArtifacts, verifyArchives, verifyBundles } from './verify.mjs';
import { copy, files, json, sha256 } from './lib.mjs';

export function prepareUnity(artifacts, scratch) {
  if (readdirSync(scratch).length)
    throw Error('Consumer scratch must be empty');
  artifacts = resolve(artifacts);
  const manifest = checkArtifacts(artifacts);
  const work = join(scratch, 'archives');
  mkdirSync(work, { recursive: true });
  verifyArchives(artifacts, manifest, work);
  verifyBundles(artifacts, manifest, work);
  const project = join(scratch, 'project');
  const packages = join(project, 'Packages');
  copy(join(work, 'unity/CapsuleUnity/Packages'), packages);
  const editor = join(project, 'Assets/Tests/Editor');
  mkdirSync(editor, { recursive: true });
  json(join(packages, 'manifest.json'), {
    dependencies: {
      'com.unity.inputsystem': '1.19.0',
      'com.unity.ugui': '2.6.0',
      'com.unity.nuget.newtonsoft-json': '3.2.2',
      'com.unity.test-framework': '1.8.0',
      'com.unity.pipeline': '0.8.0-exp.1',
    },
  });
  mkdirSync(join(project, 'ProjectSettings'));
  writeFileSync(
    join(project, 'ProjectSettings/ProjectVersion.txt'),
    'm_EditorVersion: ' + manifest.runtime.unity + '\n',
  );
  json(join(editor, 'Capsule.Release.Tests.asmdef'), {
    name: 'Capsule.Release.Tests',
    rootNamespace: 'Capsule.Release.Tests',
    references: [
      'Capsule.Core',
      'Capsule.Package',
      'Capsule.Wasmtime',
      'Capsule.Yoga',
      'Capsule.Unity',
      'Rpg.Features',
      'Mmo.Features',
      'SkillSystem.Features',
    ],
    optionalUnityReferences: ['TestAssemblies'],
    autoReferenced: false,
    includePlatforms: ['Editor'],
  });
  const cases = (rows) =>
    rows
      .map(
        (row) =>
          '        [TestCase(' +
          row.map((value) => JSON.stringify(value)).join(', ') +
          ')]',
      )
      .join('\n');
  const resources = manifest.resources
    .filter((r) => !r.path.endsWith('.dll'))
    .map((r) => ['Packages/' + r.package + '/' + r.path, r.sha256]);
  const plugins = manifest.resources
    .filter((r) => r.path.endsWith('.dll'))
    .map((r) => ['Packages/' + r.package + '/' + r.path, r.sha256]);
  const guids = [];
  for (const pkg of manifest.upm) {
    for (const file of files(join(packages, pkg.name)).filter(
      (file) =>
        file.startsWith('Runtime/') &&
        /\.(cs|asmdef|shader|dll|txt|bytes)\.meta$/.test(file),
    )) {
      const guid = readFileSync(join(packages, pkg.name, file), 'utf8').match(
        /^guid: (\w+)/m,
      )?.[1];
      if (!guid) throw Error('Missing package GUID');
      guids.push(['Packages/' + pkg.name + '/' + file.slice(0, -5), guid]);
    }
  }
  let probe = readFileSync(
    new URL('./fixtures/CapsuleReleaseProbe.cs', import.meta.url),
    'utf8',
  );
  probe = probe
    .replace(
      '__PACKAGE_CASES__',
      cases(manifest.upm.map((p) => [p.name, p.version])),
    )
    .replace('__RESOURCE_CASES__', cases(resources))
    .replace('__PLUGIN_CASES__', cases(plugins))
    .replace('__GUID_CASES__', cases(guids));
  writeFileSync(join(editor, 'CapsuleReleaseProbe.cs'), probe);
  const bundle = manifest.engines.find((entry) => entry.engine === 'unity');
  const evidence = {
    schemaVersion: 1,
    revision: manifest.revision,
    bundle: bundle.file,
    bundleSha256: bundle.sha256,
    manifestSha256: sha256(
      readFileSync(join(artifacts, 'release-manifest.json')),
    ),
    unity: manifest.runtime.unity,
    packageCount: manifest.upm.length,
    resourceCount: resources.length,
    pluginCount: plugins.length,
    guidCount: guids.length,
    expectedTests:
      1 +
      manifest.upm.length +
      resources.length +
      plugins.length +
      guids.length,
    project,
  };
  json(join(scratch, 'consumer.json'), evidence);
  return evidence;
}

if (
  process.argv[1] &&
  resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  const { values } = parseArgs({
    options: { artifacts: { type: 'string' }, output: { type: 'string' } },
  });
  if (!values.artifacts || !values.output)
    throw Error(
      'Required: --artifacts <release-directory> --output <evidence-json>',
    );
  const scratch = mkdtempSync(join(tmpdir(), 'capsule-unity-bundle-'));
  const evidence = prepareUnity(values.artifacts, scratch);
  mkdirSync(dirname(resolve(values.output)), { recursive: true });
  json(resolve(values.output), evidence);
  console.log(
    'Prepared fresh embedded Unity consumer: ' +
      evidence.expectedTests +
      ' checks',
  );
}
