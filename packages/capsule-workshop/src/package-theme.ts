import { readFile, lstat } from 'node:fs/promises';
import { resolve } from 'node:path';
import { parseTheme, themePath, type ThemeLimits } from './theme';

/** Validate the actual staged provider, using the host's trusted theme profile. */
export async function validatePackageTheme(
  root: string,
  limits?: Readonly<ThemeLimits>,
) {
  const manifest = JSON.parse(
    await readFile(resolve(root, 'addon.json'), 'utf8'),
  );
  const declaration = manifest.assets.find(
    (a: { key: string; kind: string }) =>
      a.key === 'ui-theme-revision' && a.kind === 'data',
  );
  if (!declaration) return;
  const read = async (path: string, kind: string, key?: string) => {
    const safe = themePath(path);
    if (
      !manifest.assets.some(
        (a: { path: string; kind: string; key: string }) =>
          a.path === safe && a.kind === kind && (!key || a.key === key),
      )
    )
      throw Error('Theme references an undeclared package asset: ' + safe);
    let full = resolve(root);
    for (const part of safe.split('/')) {
      full = resolve(full, part);
      if ((await lstat(full)).isSymbolicLink())
        throw Error('Theme symlinks are not supported');
    }
    return readFile(full);
  };
  const bytes = await read(declaration.path, 'data', 'ui-theme-revision');
  if (bytes.length > 65536) throw Error('Theme data quota');
  const theme = parseTheme(JSON.parse(bytes.toString()), limits);
  JSON.parse((await read(theme.font.metrics, 'data')).toString());
  for (const asset of theme.assets)
    await read(asset.path, 'image', asset.image ?? asset.key);
}
