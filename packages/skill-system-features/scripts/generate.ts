import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { descriptor, generateCSharp } from '@capsule/features/codegen';
import { contracts } from '../src/contracts';
const root = new URL('../', import.meta.url);
mkdirSync(new URL('generated/', root), { recursive: true });
const files = {
  'generated/features.json':
    JSON.stringify(Object.values(contracts).map(descriptor), null, 2) + '\n',
  '../../unity/Packages/com.capsule.features.skill-system/Runtime/SkillSystemFeatures.Generated.cs':
    generateCSharp('SkillSystem.Features', contracts).replaceAll(
      'FeatureCodec',
      'SkillSystemFeatureCodec',
    ),
};
for (const [path, content] of Object.entries(files)) {
  const url = new URL(path, root);
  if (process.argv.includes('--check')) {
    if (readFileSync(url, 'utf8').replaceAll('\r\n', '\n') !== content)
      throw Error('Generated Skill system contract drift: ' + path);
  } else writeFileSync(url, content);
}
console.log('Skill system contracts validated');
