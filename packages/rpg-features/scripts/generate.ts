import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { descriptor, generateCSharp } from '@capsule/features/codegen';
import { contracts } from '../src/contracts';
import { QuestPrerequisites } from '../src/quest-prerequisites';
import { QuestDefinition, QuestBinding, QuestFact } from '../src/quest-catalog';
const root = new URL('../', import.meta.url);
mkdirSync(new URL('generated/', root), { recursive: true });
const files = {
  'generated/quest-prerequisites.json':
    JSON.stringify(
      {
        format: '@rpg/quest-prerequisites',
        version: 1,
        schema: QuestPrerequisites,
      },
      null,
      2,
    ) + '\n',
  'generated/features.json':
    JSON.stringify(Object.values(contracts).map(descriptor), null, 2) + '\n',
  '../../unity/Packages/com.capsule.features.rpg/Runtime/RpgFeatures.Generated.cs':
    generateCSharp('Rpg.Features', contracts, [
      QuestPrerequisites,
      QuestDefinition,
      QuestBinding,
      QuestFact,
    ]).replaceAll('FeatureCodec', 'RpgFeatureCodec'),
};
for (const [path, content] of Object.entries(files)) {
  const url = new URL(path, root);
  if (process.argv.includes('--check')) {
    if (readFileSync(url, 'utf8').replaceAll('\r\n', '\n') !== content)
      throw Error('Generated RPG contract drift: ' + path);
  } else writeFileSync(url, content);
}
console.log('RPG contracts validated');
