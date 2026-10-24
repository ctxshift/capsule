import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { resolve, dirname, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
for (const [name, namespace, allowed] of [
  ['rpg-features', 'rpg', ['@capsule/features']],
  ['skill-system-features', 'skill-system', ['@capsule/features']],
  ['mmo-features', 'mmo', ['@capsule/features', '@capsule/rpg-features']],
]) {
  const base = resolve(root, 'packages', name),
    source = resolve(base, 'src'),
    pkg = JSON.parse(readFileSync(resolve(base, 'package.json'), 'utf8'));
  for (const dependency of Object.keys(pkg.dependencies ?? {}))
    if (!allowed.includes(dependency))
      throw Error(name + ': forbidden runtime dependency ' + dependency);
  for (const file of readdirSync(source, { recursive: true }).filter((f) =>
    f.endsWith('.ts'),
  )) {
    const full = resolve(source, file),
      text = readFileSync(full, 'utf8');
    for (const match of text.matchAll(
      /(?:from\s*|import\s*\(\s*|import\s*)['"]([^'"]+)['"]/g,
    )) {
      const spec = match[1];
      if (spec.startsWith('.')) {
        const target = relative(source, resolve(dirname(full), spec));
        if (target.startsWith('..') || target.includes(':'))
          throw Error(name + ': source escaped package: ' + file);
      } else if (!allowed.some((a) => spec === a || spec.startsWith(a + '/')))
        throw Error(name + ': forbidden import ' + spec + ' in ' + file);
    }
  }
  const definitions = JSON.parse(
    readFileSync(resolve(base, 'generated/features.json'), 'utf8'),
  );
  for (const d of definitions) {
    if (!d.name.startsWith('@' + namespace + '/'))
      throw Error('Incorrect feature ownership: ' + d.name);
    const allowedFeatures =
      namespace === 'mmo' ? ['@mmo/', '@rpg/'] : ['@' + namespace + '/'];
    for (const dep of Object.keys(d.dependencies))
      if (!allowedFeatures.some((prefix) => dep.startsWith(prefix)))
        throw Error(
          'Dependency points outside allowed feature direction: ' + dep,
        );
    if (
      d.name === '@mmo/player-trade' &&
      Object.keys(d.dependencies).some((dep) => dep.startsWith('@rpg/'))
    )
      throw Error('Shared item schema must not require an RPG provider');
  }
  const cap =
      namespace === 'rpg' ? 'Rpg' : namespace === 'mmo' ? 'Mmo' : 'SkillSystem',
    nativePackage = resolve(
      root,
      'unity/Packages/com.capsule.features.' + namespace,
    ),
    nativeFile = cap + 'Features.Generated.cs',
    generated = readFileSync(
      resolve(nativePackage, 'Runtime', nativeFile),
      'utf8',
    ),
    asmdef = JSON.parse(
      readFileSync(
        resolve(nativePackage, 'Runtime', cap + '.Features.asmdef'),
        'utf8',
      ),
    ),
    manifest = JSON.parse(
      readFileSync(resolve(nativePackage, 'package.json'), 'utf8'),
    );
  if (!generated.includes('namespace ' + cap + '.Features'))
    throw Error('Incorrect native namespace: ' + name);
  if (existsSync(resolve(base, 'generated', nativeFile)))
    throw Error('Duplicate generated native source: ' + name);
  const standalone = readFileSync(
    resolve(base, 'native', cap + '.Features.csproj'),
    'utf8',
  );
  if (
    !standalone.includes(
      'com.capsule.features.' + namespace + '/Runtime/' + nativeFile,
    )
  )
    throw Error(
      'Standalone project does not compile package-owned source: ' + name,
    );
  if (
    asmdef.name !== cap + '.Features' ||
    !asmdef.references.includes('Capsule.Core')
  )
    throw Error('Incorrect native assembly dependency: ' + name);
  if (manifest.dependencies?.['com.capsule.core'] !== '0.1.0')
    throw Error('Missing portable core UPM dependency: ' + name);
}
console.log('Feature package boundaries and native contract identities passed');
