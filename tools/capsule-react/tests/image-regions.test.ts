import { readFileSync } from 'node:fs';
import { expect, test } from 'vitest';
import { createBrowserTheme } from '@capsule/workshop/browser-theme';
import { DomTarget } from '@capsule/workshop/dom';
import { image, tiledImage } from '@capsule/workshop/raster';
import type { UiNode } from '@capsule/workshop/packet';
import { parseTheme, type FontMetrics } from '@capsule/workshop/theme';
import { repositoryPath } from '../scripts/paths.mjs';

// Only the DOM surface used by Image nodes; no browser or image decoder required.
class ElementStub {
  readonly attributes = new Map<string, string>();
  readonly style: Record<string, string> = {};
  readonly dataset: Record<string, string> = {};
  readonly children: ElementStub[] = [];
  readonly listeners = new Map<string, ((event?: unknown) => void)[]>();
  readonly offsetWidth = 1440;
  readonly offsetHeight = 900;
  getBoundingClientRect() {
    return { left: 0, top: 0, width: 1440, height: 900 };
  }
  setPointerCapture() {}
  contains(element: ElementStub): boolean {
    return (
      element === this || this.children.some((child) => child.contains(element))
    );
  }
  focus() {
    this.ownerDocument.activeElement = this;
    this.dispatch('focus');
  }
  parentElement: ElementStub | null = null;
  constructor(
    readonly ownerDocument: DocumentStub,
    readonly tagName: string,
    readonly namespaceURI: string | null = null,
  ) {}
  get nextElementSibling(): ElementStub | null {
    const siblings = this.parentElement?.children;
    return siblings?.[siblings.indexOf(this) + 1] ?? null;
  }
  setAttribute(key: string, value: string) {
    this.attributes.set(key, value);
  }
  removeAttribute(key: string) {
    this.attributes.delete(key);
  }
  append(...elements: ElementStub[]) {
    for (const element of elements) {
      element.remove();
      element.parentElement = this;
      this.children.push(element);
    }
  }
  remove() {
    if (!this.parentElement) return;
    if (this.contains(this.ownerDocument.activeElement))
      this.ownerDocument.activeElement = this.ownerDocument.body;
    const siblings = this.parentElement.children;
    siblings.splice(siblings.indexOf(this), 1);
    this.parentElement = null;
  }
  replaceWith(element: ElementStub) {
    const parent = this.parentElement!;
    const index = parent.children.indexOf(this);
    element.remove();
    parent.children[index] = element;
    element.parentElement = parent;
    this.parentElement = null;
  }
  replaceChildren(...elements: ElementStub[]) {
    for (const child of [...this.children]) child.remove();
    this.append(...elements);
  }
  insertBefore(element: ElementStub, next: ElementStub | null) {
    element.remove();
    const index = next ? this.children.indexOf(next) : this.children.length;
    this.children.splice(index, 0, element);
    element.parentElement = this;
  }
  addEventListener(name: string, listener: (event?: unknown) => void) {
    this.listeners.set(name, [...(this.listeners.get(name) ?? []), listener]);
  }
  dispatch(name: string, event?: unknown) {
    for (const listener of this.listeners.get(name) ?? []) listener(event);
  }
}
class DocumentStub {
  readonly listeners = new Map<string, (event: unknown) => void>();
  readonly defaultView = { addEventListener() {} };
  readonly body = new ElementStub(this, 'body');
  activeElement = this.body;
  createElement(tag: string) {
    return new ElementStub(this, tag);
  }
  createElementNS(namespace: string | null, tag: string) {
    return new ElementStub(this, tag, namespace);
  }
  addEventListener(name: string, listener: (event: unknown) => void) {
    this.listeners.set(name, listener);
  }
}

test('tiled browser images repeat a centered cropped source with unique pattern identity', () => {
  const document = new DocumentStub();
  const asset = {
    url: 'atlas.png',
    size: [320, 320] as const,
    region: [40, 60, 240, 200] as const,
  };
  const first = tiledImage(
    document as unknown as Document,
    asset,
    [240, 240],
  ) as unknown as ElementStub;
  const second = tiledImage(
    document as unknown as Document,
    asset,
    [240, 240],
  ) as unknown as ElementStub;
  const pattern = first.children[0].children[0];
  expect(pattern.attributes.get('patternUnits')).toBe('userSpaceOnUse');
  expect(pattern.attributes.get('x')).toBe('50%');
  expect(pattern.attributes.get('patternTransform')).toBe(
    'translate(-120 -120)',
  );
  expect(pattern.children[0].attributes.get('viewBox')).toBe('40 60 240 200');
  expect(pattern.attributes.get('id')).not.toBe(
    second.children[0].children[0].attributes.get('id'),
  );
});

function fixture() {
  const theme = parseTheme(
    JSON.parse(
      readFileSync(
        repositoryPath('examples/portable-demo-game/theme.json'),
        'utf8',
      ),
    ),
  );
  theme.assets.push(
    {
      key: 'sample-left',
      brightness: 1.45,
      opacity: 0.18,
      image: 'sample-atlas',
      path: 'assets/sample.png',
      width: 120,
      height: 80,
      rect: [10, 20, 30, 40],
      border: 0,
      edge: 0,
      alphaAware: false,
    },
    {
      key: 'sample-right',
      stretch: true,
      image: 'sample-atlas',
      path: 'assets/sample.png',
      width: 120,
      height: 80,
      rect: [60, 5, 50, 70],
      border: 0,
      edge: 0,
      alphaAware: false,
    },
  );
  const metrics: FontMetrics = { advances: [] };
  theme.surfaces = [
    {
      key: 'sample-slot',
      color: [12, 16, 12, 204],
      shadow: { blur: 8, color: [213, 163, 75, 255] },
    },
  ];
  const browser = createBrowserTheme(theme, metrics, (key) => `/assets/${key}`);
  const document = new DocumentStub();
  const stage = document.createElement('div');
  const events: Uint8Array[] = [];
  const target = new DomTarget(
    stage as unknown as HTMLElement,
    browser,
    (bytes) => events.push(bytes),
    () => {},
  );
  const node: UiNode = {
    id: 1n,
    parent: 0n,
    kind: 4,
    rect: [9, 9, 46, 46],
    style: '',
    asset: 'sample-left',
    text: '',
    events: 0,
    value: 0,
    visible: true,
    brightness: 10000,
    layout: [0, 1, 1, 0, 46 * 64, 46 * 64, 9 * 64, 9 * 64, 0, 0, 0, 0, 0, 0, 0],
  };
  const apply = (changes: Partial<UiNode> = {}) =>
    target.apply(new Map([[node.id, { ...node, ...changes }]]));
  return { browser, stage, target, apply, events, node };
}

test('browser J/K keys retain native codes and respect typing and browser modifiers', () => {
  const { stage, target, apply, events } = fixture();
  try {
    apply({ events: 128 });
    const key = (value: string, ctrlKey = false) =>
      stage.ownerDocument.listeners.get('keydown')!({
        key: value,
        ctrlKey,
        preventDefault() {},
      });
    for (const value of ['j', 'J', 'k', 'K']) key(value);
    expect(events.map((event) => event[17])).toEqual([16, 16, 17, 17]);
    key('j', true);
    expect(events).toHaveLength(4);
    stage.ownerDocument.activeElement =
      stage.ownerDocument.createElement('INPUT');
    key('k');
    expect(events).toHaveLength(4);
  } finally {
    target.dispose();
  }
});

test('dismissed popup restores nearest surviving focusable ancestor without stealing external focus', () => {
  const { stage, target, node } = fixture();
  const parent = { ...node, kind: 8, asset: '', events: 33 };
  const child = { ...parent, id: 2n, parent: 1n };
  try {
    target.apply(
      new Map([
        [1n, parent],
        [2n, child],
      ]),
    );
    const container = stage.children[0],
      popup = container.children[0];
    popup.focus();
    target.apply(new Map([[1n, parent]]));
    expect(stage.ownerDocument.activeElement).toBe(container);
    const external = stage.ownerDocument.createElement('button');
    external.focus();
    target.apply(new Map([[1n, parent]]));
    expect(stage.ownerDocument.activeElement).toBe(external);
    target.apply(
      new Map([
        [1n, parent],
        [2n, child],
      ]),
    );
    container.children[0].focus();
    target.apply(new Map([[1n, { ...parent, visible: false }]]));
    expect(stage.ownerDocument.activeElement).toBe(stage.ownerDocument.body);
  } finally {
    target.dispose();
  }
});

test('focusable containers activate by click or keyboard and retire subscriptions', () => {
  const { stage, target, apply, events } = fixture();
  try {
    apply({ kind: 8, asset: '', events: 33 });
    const element = stage.children[0];
    const event = {
      target: element,
      key: 'Enter',
      repeat: false,
      preventDefault() {},
      stopPropagation() {},
    };
    expect(element.style.pointerEvents).toBe('auto');
    expect((element as unknown as HTMLElement).tabIndex).toBe(0);
    element.dispatch('focus');
    element.dispatch('click', event);
    element.dispatch('keydown', event);
    element.dispatch('keydown', { ...event, repeat: true });
    element.dispatch('keydown', { ...event, target: {} });
    expect(
      events.map((bytes) => new DataView(bytes.buffer).getUint16(2, true)),
    ).toEqual([32, 1, 1]);
    apply({ kind: 8, asset: '', events: 0 });
    expect((element as unknown as HTMLElement).tabIndex).toBe(-1);
    element.dispatch('focus');
    element.dispatch('click', event);
    element.dispatch('keydown', event);
    expect(events).toHaveLength(3);
  } finally {
    target.dispose();
  }
});

test('browser secondary pointer retains button2 through move and release', () => {
  const { stage, target, apply, events } = fixture();
  try {
    apply({ kind: 8, asset: '', events: 64 });
    const element = stage.children[0];
    const event = (button: number) => ({
      button,
      pointerId: 9,
      clientX: 10,
      clientY: 20,
      currentTarget: element,
      preventDefault() {},
    });
    element.dispatch('pointerdown', event(2));
    element.dispatch('pointermove', event(-1));
    element.dispatch('pointerup', event(2));
    expect(events.map((bytes) => [bytes[17], bytes[18]])).toEqual([
      [1, 2],
      [2, 2],
      [3, 2],
    ]);
    events.length = 0;
    element.dispatch('pointerdown', event(0));
    element.dispatch('pointerup', event(0));
    expect(events.map((bytes) => bytes[18])).toEqual([0, 0]);
  } finally {
    target.dispose();
  }
});

test('browser hover delivers enter/leave and stops after a retained subscription is removed', () => {
  const { stage, target, apply, events } = fixture();
  try {
    apply({ events: 4 });
    const element = stage.children[0];
    expect(element.style.pointerEvents).toBe('auto');
    element.dispatch('pointerenter');
    element.dispatch('pointerleave');
    expect(events.map((bytes) => [...bytes.subarray(16)])).toEqual([
      [1, 1],
      [1, 0],
    ]);
    apply({ events: 0 });
    expect(element.style.pointerEvents).toBe('none');
    element.dispatch('pointerenter');
    expect(events).toHaveLength(2);
  } finally {
    target.dispose();
  }
});

test('Image renders the original atlas region with aspect fit and one preload URL', () => {
  const { browser, stage, target, apply } = fixture();
  try {
    apply();
    const element = stage.children[0];
    const svg = element.children[0];
    expect(svg.tagName).toBe('svg');
    expect(svg.attributes.get('viewBox')).toBe('10 20 30 40');
    expect(svg.attributes.get('preserveAspectRatio')).toBe('xMidYMid meet');
    expect(svg.style.overflow).toBe('hidden');
    expect(svg.style.pointerEvents).toBe('none');
    const crop = svg.children[0];
    expect(crop.attributes.get('viewBox')).toBe('10 20 30 40');
    expect(crop.attributes.get('width')).toBe('30');
    expect(crop.attributes.get('height')).toBe('40');
    expect(crop.style.overflow).toBe('hidden');
    expect(crop.children[0].attributes.get('href')).toBe(
      '/assets/sample-atlas',
    );
    expect(crop.children[0].attributes.get('width')).toBe('120');
    expect(crop.children[0].attributes.get('height')).toBe('80');
    expect(element.style.filter).toBe('brightness(1)');
    expect(svg.style.filter).toBe('brightness(1.45)');
    expect(svg.style.opacity).toBe('0.18');
    expect(
      browser.images.filter((url) => url === '/assets/sample-atlas'),
    ).toHaveLength(1);
    expect(browser.images).not.toContain('/assets/sample-left');
    expect(browser.images).not.toContain('/assets/sample-right');
  } finally {
    target.dispose();
  }
});

test('retained Image updates region, brightness and opacity, reuses unchanged SVG, and rejects unknown keys', () => {
  const { stage, target, apply } = fixture();
  try {
    apply();
    const element = stage.children[0];
    const first = element.children[0];
    apply({ brightness: 14500 });
    expect(stage.children[0]).toBe(element);
    expect(element.children[0]).toBe(first);
    expect(element.style.filter).toBe('brightness(1.45)');
    apply({ asset: 'sample-right' });
    const second = element.children[0];
    expect(second).not.toBe(first);
    expect(second.style.filter).toBe('brightness(1)');
    expect(second.style.opacity).toBe('1');
    expect(second.attributes.get('viewBox')).toBe('60 5 50 70');
    expect(second.attributes.get('preserveAspectRatio')).toBe('none');
    expect(second.style.overflow).toBe('hidden');
    expect(second.children[0].attributes.get('href')).toBe(
      '/assets/sample-atlas',
    );
    expect(first.parentElement).toBeNull();
    expect(() => apply({ asset: 'missing-image' })).toThrow(
      'Missing theme image missing-image',
    );
    expect(element.children).toEqual([second]);
    apply({ asset: 'sample-right' });
    expect(element.children).toEqual([second]);
    apply({ asset: 'sample-left' });
    expect(element.children[0].attributes.get('preserveAspectRatio')).toBe(
      'xMidYMid meet',
    );
  } finally {
    target.dispose();
  }
});

test('surface styling resets on retained boxes without changing node brightness', () => {
  const { stage, target, apply } = fixture();
  try {
    apply({ kind: 8, style: 'sample-slot', asset: '' });
    const element = stage.children[0];
    expect(element.style.backgroundColor).toBe('rgba(12,16,12,0.8)');
    expect(element.style.filter).toBe(
      'brightness(1) drop-shadow(0 0 4px rgba(213,163,75,1))',
    );
    apply({ kind: 8, style: '', asset: '' });
    expect(stage.children[0]).toBe(element);
    expect(element.style.backgroundColor).toBe('rgba(0,0,0,0)');
    expect(element.style.filter).toBe('brightness(1)');
  } finally {
    target.dispose();
  }
});

test('unknown initial Image and unsupported Portrait fail closed', () => {
  const { stage, target, apply } = fixture();
  try {
    expect(() => apply({ asset: 'missing-image' })).toThrow(
      'Missing theme image',
    );
    expect(stage.children).toHaveLength(0);
    expect(() => apply({ kind: 5 })).toThrow('does not support primitive 5');
    expect(stage.children).toHaveLength(0);
  } finally {
    target.dispose();
  }
});

test('quarter-turn regions retain original pixels with clipped source bounds', () => {
  const document = new DocumentStub();
  for (const rotation of [90, 180, 270] as const) {
    const svg = image(
      document as unknown as Document,
      { url: '/atlas', size: [120, 80], region: [10, 20, 30, 40] },
      undefined,
      false,
      rotation,
    ) as unknown as ElementStub;
    expect(svg.attributes.get('viewBox')).toBe(
      rotation === 180 ? '0 0 30 40' : '0 0 40 30',
    );
    expect(svg.attributes.get('preserveAspectRatio')).toBe('xMidYMid meet');
    const crop = svg.children[0].children[0];
    expect(crop.attributes.get('viewBox')).toBe('10 20 30 40');
    expect(crop.style.overflow).toBe('hidden');
    expect(crop.children[0].attributes.get('href')).toBe('/atlas');
  }
});
