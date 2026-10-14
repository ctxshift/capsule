import type { ComponentTheme } from '@capsule/react/components';

export type Rgba = [number, number, number, number];
export interface ThemeAsset {
  key: string;
  /** Declared physical image; defaults to key for a single-region asset. */
  image?: string;
  path: string;
  width: number;
  height: number;
  rect: [number, number, number, number];
  border: number;
  edge: number;
  alphaAware: boolean;
  brightness?: number;
}
export interface UiTheme {
  schemaVersion: 1;
  id: string;
  font: {
    family: string;
    style: string;
    pointSize: number;
    sampleSize: number;
    metrics: string;
  };
  components: ComponentTheme;
  window: {
    background: Rgba;
    backing: string;
    backingInset: number;
    backingOpacity: number;
    frame: string;
    titleSize: number;
    titleSpacing: number;
    titleAlignment: 'left' | 'center' | 'right';
    titleColor: Rgba;
    ruleColor: Rgba;
    ruleTop: number;
    ruleHeight: number;
    closeFrame: string;
    closeCross: string;
    closeInset: number;
    closeSize: number;
    crossInset: number;
    crossSize: number;
    shadowOffset: number;
    shadowBlur: number;
    shadowColor: Rgba;
  };
  button: {
    background: Rgba;
    hover: Rgba;
    highlight: string;
    highlightOpacity: number;
    highlightInset: number;
    frame: string;
    foreground: Rgba;
  };
  range: {
    track: Rgba;
    border: Rgba;
    fill: Rgba;
    trackHeight: number;
    trackRadius: number;
    borderWidth: number;
    thumbWidth: number;
    thumbHeight: number;
    thumbRadius: number;
  };
  select: {
    background: Rgba;
    border: Rgba;
    foreground: Rgba;
    fontSize: number;
    itemHeight: number;
    paddingLeft: number;
    paddingRight: number;
    borderWidth: number;
    arrowWidth: number;
    arrowHeight: number;
  };
  focus: { color: Rgba; width: number; inset: number };
  text: {
    key: string;
    size: number;
    lineHeight: number;
    wrap: boolean;
    color: Rgba;
  }[];
  assets: ThemeAsset[];
  surfaces?: { key: string; color: Rgba }[];
}
export interface FontMetrics {
  advances: { code: number; value: number }[];
  pairs?: unknown[];
}
const pathPattern = /^[a-zA-Z0-9_.-]+(?:\/[a-zA-Z0-9_.-]+)*$/;
export function themePath(value: string) {
  if (
    !pathPattern.test(value) ||
    value.split('/').some((part) => part === '.' || part === '..')
  )
    throw Error('Invalid theme asset path');
  return value;
}
// Trusted provider schemas remain bounded; addon overrides cannot replace provider identity.
export function parseTheme(value: unknown): UiTheme {
  if (!value || typeof value !== 'object') throw Error('Missing theme');
  const theme = value as UiTheme;
  if (theme.schemaVersion !== 1 || !/^[a-z0-9.-]{1,64}$/.test(theme.id))
    throw Error('Invalid theme identity');
  for (const key of [
    'window',
    'button',
    'range',
    'select',
    'focus',
    'components',
    'font',
  ] as const) {
    const group = theme[key];
    if (!group || typeof group !== 'object')
      throw Error('Missing theme group: ' + key);
    for (const item of Object.values(group)) {
      if (
        typeof item === 'number' &&
        (!Number.isFinite(item) || Math.abs(item) > 4096)
      )
        throw Error('Theme number bounds');
      if (
        typeof item === 'string' &&
        (!item.length || item.length > 128 || /["'\\;{}<>\r\n]/.test(item))
      )
        throw Error('Theme string bounds');
      if (
        Array.isArray(item) &&
        (item.length !== 4 ||
          item.some(
            (channel) =>
              !Number.isInteger(channel) || channel < 0 || channel > 255,
          ))
      )
        throw Error('Theme color bounds');
    }
  }
  themePath(theme.font.metrics);
  if (
    !Array.isArray(theme.assets) ||
    !theme.assets.length ||
    theme.assets.length > 30 ||
    !Array.isArray(theme.text) ||
    theme.text.length > 16
  )
    throw Error('Theme collection bounds');
  const keys = new Set<string>();
  const surfaces = new Set<string>();
  if ((theme.surfaces?.length ?? 0) > 30) throw Error('Theme surface quota');
  for (const surface of theme.surfaces ?? []) {
    if (
      !/^[a-z0-9-]{1,64}$/.test(surface.key) ||
      surfaces.has(surface.key) ||
      !Array.isArray(surface.color) ||
      surface.color.length !== 4 ||
      !surface.color.every((n) => Number.isInteger(n) && n >= 0 && n <= 255)
    )
      throw Error('Theme surface bounds');
    surfaces.add(surface.key);
  }
  const images = new Map<string, string>();
  for (const asset of theme.assets) {
    if (!/^[a-z0-9-]{1,64}$/.test(asset.key) || keys.has(asset.key))
      throw Error('Theme asset identity');
    keys.add(asset.key);
    if (
      !Number.isFinite(asset.brightness ?? 1) ||
      (asset.brightness ?? 1) < 0 ||
      (asset.brightness ?? 1) > 2
    )
      throw Error('Theme image brightness bounds');
    themePath(asset.path);
    const image = asset.image ?? asset.key;
    if (!/^[a-z0-9-]{1,64}$/.test(image)) throw Error('Theme image identity');
    const source = JSON.stringify([
      asset.path,
      asset.width,
      asset.height,
      asset.alphaAware,
    ]);
    if (images.has(image) && images.get(image) !== source)
      throw Error('Conflicting theme image source');
    images.set(image, source);
    if (
      ![
        asset.width,
        asset.height,
        asset.border,
        asset.edge,
        ...asset.rect,
      ].every((n) => Number.isInteger(n) && n >= 0 && n <= 8192) ||
      asset.rect.length !== 4 ||
      asset.rect[2] < 1 ||
      asset.rect[3] < 1 ||
      asset.rect[0] + asset.rect[2] > asset.width ||
      asset.rect[1] + asset.rect[3] > asset.height
    )
      throw Error('Theme image bounds');
  }
  for (const key of [
    theme.window.frame,
    theme.window.backing,
    theme.window.closeFrame,
    theme.window.closeCross,
    theme.button.frame,
    theme.button.highlight,
  ])
    if (!keys.has(key)) throw Error('Missing theme asset: ' + key);
  for (const text of theme.text)
    if (
      !/^[a-z0-9-]{1,64}$/.test(text.key) ||
      !Number.isFinite(text.size) ||
      text.size <= 0 ||
      text.size > 128 ||
      !Number.isFinite(text.lineHeight) ||
      text.lineHeight < text.size ||
      text.lineHeight > 256 ||
      typeof text.wrap !== 'boolean' ||
      text.color.length !== 4 ||
      text.color.some((n) => !Number.isInteger(n) || n < 0 || n > 255)
    )
      throw Error('Theme text bounds');
  return JSON.parse(JSON.stringify(theme)) as UiTheme;
}

export function scopedTheme(provider: UiTheme, overrides: unknown): UiTheme {
  const result = parseTheme(provider);
  if (!overrides || typeof overrides !== 'object' || Array.isArray(overrides))
    throw Error('Invalid scoped theme');
  const record = result as unknown as Record<string, Record<string, unknown>>;
  for (const [group, values] of Object.entries(overrides)) {
    if (
      !['window', 'button', 'range', 'select', 'focus'].includes(group) ||
      !values ||
      typeof values !== 'object' ||
      Array.isArray(values)
    )
      throw Error('Unknown scoped theme group ' + group);
    for (const [key, value] of Object.entries(values)) {
      const original = record[group][key];
      if (
        original === undefined ||
        typeof original === 'string' ||
        typeof original !== typeof value ||
        Array.isArray(original) !== Array.isArray(value)
      )
        throw Error('Unsupported scoped theme token ' + key);
      if (
        typeof value === 'number' &&
        ((!(
          (group === 'focus' && key === 'inset') ||
          (group === 'window' && key === 'shadowOffset')
        ) &&
          value < 0) ||
          (key.endsWith('Opacity') && value > 1))
      )
        throw Error('Scoped geometry bounds');
      record[group][key] = value;
    }
  }
  return parseTheme(result);
}
