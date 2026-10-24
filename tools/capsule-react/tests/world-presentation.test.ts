import { expect, test } from 'vitest';
import { build } from 'esbuild';
import { chromium } from 'playwright-core';
import { fileURLToPath } from 'node:url';
import type { DomTarget } from '@capsule/workshop/dom';
import type { UiNode } from '@capsule/workshop/packet';
import {
  SimulatedWorld,
  EffectClock,
} from '../../../packages/capsule-workshop/src/browser/presentation';

type WorldDomFixture = Window & {
  WorldDomProof: {
    DomTarget: typeof DomTarget;
    SimulatedWorld: typeof SimulatedWorld;
  };
  target: DomTarget;
  world: SimulatedWorld;
  nodes: Map<bigint, UiNode>;
  field: HTMLElement;
};

test('DOM target schedules native presentation frames and retires them on unload', async () => {
  const compiled = await build({
    stdin: {
      contents: `import {DomTarget} from '../../../packages/capsule-workshop/src/browser/dom'; import {SimulatedWorld} from '../../../packages/capsule-workshop/src/browser/presentation'; globalThis.WorldDomProof={DomTarget,SimulatedWorld};`,
      resolveDir: fileURLToPath(new URL('.', import.meta.url)),
    },
    bundle: true,
    format: 'esm',
    write: false,
  });
  const browser = await chromium.launch({
    headless: true,
    ...(process.env.PORTABLE_UI_BROWSER
      ? { executablePath: process.env.PORTABLE_UI_BROWSER }
      : { channel: 'chrome' }),
  });
  try {
    const page = await browser.newPage({
      viewport: { width: 800, height: 600 },
    });
    await page.setContent(
      '<div id="stage" style="position:relative;width:800px;height:600px"></div>',
    );
    await page.addScriptTag({
      content: compiled.outputFiles[0].text,
      type: 'module',
    });
    await page.waitForFunction(
      () => !!(window as unknown as WorldDomFixture).WorldDomProof,
    );
    const initial = await page.evaluate(() => {
      const w = window as unknown as WorldDomFixture,
        stage = document.getElementById('stage')!;
      w.world = new w.WorldDomProof.SimulatedWorld(7, 1);
      w.world.grant(
        'actor.life1',
        'head',
        () => [0, 0, 10],
        () => true,
      );
      w.target = new w.WorldDomProof.DomTarget(
        stage,
        {
          css: '',
          fonts: [],
          images: [],
          metrics: () => ({ lineHeight: 16, advance: () => 8 }),
          decorate: () => {},
        },
        () => {},
        () => {},
        w.world,
      );
      const id = (7n << 32n) | (1n << 16n) | 1n;
      w.nodes = new Map([
        [
          id,
          {
            id,
            parent: 0n,
            kind: 8,
            rect: [0, 0, 80, 20],
            style: '',
            asset: '',
            text: '',
            events: 0,
            value: 0,
            visible: true,
            brightness: 10000,
            layout: [0, 1, 1, 0, 80 * 64, 20 * 64, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            worldAnchor: { entity: 'actor.life1', attachment: 'head' },
          },
        ],
      ]);
      w.target.apply(w.nodes);
      w.field = stage.firstElementChild as HTMLElement;
      return w.field.getBoundingClientRect().x;
    });
    await page.evaluate(() => {
      (window as unknown as WorldDomFixture).world.camera.x = 1;
    });
    await page.waitForFunction(
      (x) =>
        (window as unknown as WorldDomFixture).field.getBoundingClientRect().x <
        x,
      initial,
    );
    const final = await page.evaluate(() => {
      const w = window as unknown as WorldDomFixture;
      w.target.apply(new Map());
      const removed = !w.field.isConnected;
      w.target.dispose();
      const denied =
        w.world.acquire((7n << 32n) | (1n << 16n) | 1n, {
          entity: 'actor.life1',
          attachment: 'head',
        }) === undefined;
      return { removed, denied };
    });
    expect(final).toEqual({ removed: true, denied: true });
  } finally {
    await browser.close();
  }
});

test('host grants isolate generations and retire original leases on permission loss or regrant', () => {
  const world = new SimulatedWorld(7, 1),
    id = (7n << 32n) | (1n << 16n) | 1n;
  const anchor = {
    entity: 'actor.life1',
    attachment: 'head',
    referenceDistance: 10,
    minScale: 0.5,
    maxScale: 2,
    occlusion: true,
  };
  let point: readonly [number, number, number] | undefined = [0, 0, 10],
    permitted = true,
    visible = true;
  world.grant(
    anchor.entity,
    anchor.attachment,
    () => point,
    () => permitted,
    () => visible,
  );
  expect(world.acquire(id + (1n << 16n), anchor)).toBeUndefined();
  const lease = world.acquire(id, anchor)!;
  expect(lease.project(anchor)).toMatchObject({
    x: 0.5,
    y: 0.5,
    scale: 1,
    visible: true,
  });
  world.camera.x = 1;
  expect(lease.project(anchor)!.x).toBeLessThan(0.5);
  world.camera.x = 0;
  point = [0, 0, 1];
  expect(lease.project(anchor)!.scale).toBe(2);
  point = [0, 0, 50];
  expect(lease.project(anchor)!.scale).toBe(0.5);
  for (const p of [
    [0, 0, -1],
    [0, 0, 0.01],
    [0, 0, 101],
    [100, 0, 10],
  ] as const) {
    point = p;
    expect(lease.project(anchor)!.visible).toBe(false);
  }
  point = [0, 0, 10];
  visible = false;
  expect(lease.project(anchor)!.visible).toBe(false);
  visible = true;
  expect(lease.project(anchor)!.visible).toBe(true);
  permitted = false;
  expect(lease.project(anchor)).toBeUndefined();
  permitted = true;
  world.revoke(anchor.entity, anchor.attachment);
  world.grant(
    anchor.entity,
    anchor.attachment,
    () => point,
    () => true,
    () => true,
  );
  expect(lease.project(anchor)).toBeUndefined();
  const fresh = world.acquire(id, anchor)!;
  expect(fresh.project(anchor)!.visible).toBe(true);
  point = undefined;
  expect(fresh.project(anchor)).toBeUndefined();
  point = [0, 0, 10];
  expect(fresh.project(anchor)).toBeUndefined();
  world.dispose();
  expect(world.acquire(id, anchor)).toBeUndefined();
});

test('effect clocks preserve start, fade, expire, reject time rollback and restart only on revision', () => {
  const clock = new EffectClock(),
    effect = {
      revision: 1,
      durationMs: 1000,
      fadeMs: 500,
      offset: [10, -20] as const,
    };
  clock.configure(effect, 100);
  expect(clock.sample(effect, 850)).toEqual({
    x: 7.5,
    y: -15,
    alpha: 0.5,
    visible: true,
  });
  clock.configure({ ...effect }, 900);
  expect(clock.sample(effect, 1100).visible).toBe(false);
  expect(clock.sample(effect, 100).visible).toBe(false);
  const restarted = { ...effect, revision: 2 };
  clock.configure(restarted, 1100);
  expect(clock.sample(restarted, 1100).visible).toBe(true);
  clock.configure(undefined, 1200);
  expect(clock.sample(undefined, 1200)).toEqual({
    x: 0,
    y: 0,
    alpha: 1,
    visible: true,
  });
});

test('actual browser presentation tracks host camera and disables expired or revoked content', async () => {
  const compiled = await build({
    stdin: {
      contents: `export {bindPresentation,SimulatedWorld} from '../../../packages/capsule-workshop/src/browser/presentation';`,
      resolveDir: fileURLToPath(new URL('.', import.meta.url)),
    },
    bundle: true,
    format: 'iife',
    globalName: 'WorldProof',
    write: false,
  });
  const browser = await chromium.launch({
    headless: true,
    ...(process.env.PORTABLE_UI_BROWSER
      ? { executablePath: process.env.PORTABLE_UI_BROWSER }
      : { channel: 'chrome' }),
  });
  try {
    const page = await browser.newPage({
      viewport: { width: 800, height: 600 },
    });
    await page.setContent(
      '<div id="stage" style="position:relative;width:800px;height:600px"><div id="field" style="position:absolute;width:80px;height:20px"><button>Target</button></div></div>',
    );
    await page.addScriptTag({ content: compiled.outputFiles[0].text });
    const result = await page.evaluate(() => {
      const api = (
        window as unknown as {
          WorldProof: typeof import('../../../packages/capsule-workshop/src/browser/presentation');
        }
      ).WorldProof;
      const stage = document.getElementById('stage')!,
        field = document.getElementById('field')!;
      const world = new api.SimulatedWorld(7, 1);
      world.grant(
        'actor.life1',
        'head',
        () => [0, 0, 10],
        () => true,
      );
      const node = {
        id: (7n << 32n) | (1n << 16n) | 1n,
        parent: 0n,
        kind: 8,
        rect: [0, 0, 80, 20],
        style: '',
        asset: '',
        text: '',
        events: 1,
        value: 0,
        visible: true,
        brightness: 10000,
        layout: null,
        worldAnchor: { entity: 'actor.life1', attachment: 'head' },
        effect: {
          revision: 1,
          durationMs: 1000,
          fadeMs: 500,
          offset: [0, -40] as const,
        },
      };
      const view = api.bindPresentation(field, stage, world);
      view.apply(node, node.rect, 0);
      view.tick(0);
      const initial = field.getBoundingClientRect().x;
      world.camera.x = 1;
      view.tick(500);
      const moved = field.getBoundingClientRect().x < initial;
      view.apply({ ...node }, node.rect, 750);
      view.tick(750);
      const faded = field.style.opacity === '0.5';
      field.querySelector('button')!.focus();
      view.tick(1000);
      const expired =
        field.inert &&
        field.style.opacity === '0' &&
        document.activeElement !== field.querySelector('button');
      view.apply({ ...node, effect: undefined }, node.rect, 1000);
      view.tick(1000);
      const canceled = !field.inert && field.style.opacity === '1';
      world.revoke('actor.life1', 'head');
      world.grant(
        'actor.life1',
        'head',
        () => [0, 0, 10],
        () => true,
      );
      view.tick(1001);
      const revoked = field.inert;
      view.reset();
      return { moved, faded, expired, canceled, revoked };
    });
    expect(result).toEqual({
      moved: true,
      faded: true,
      expired: true,
      canceled: true,
      revoked: true,
    });
  } finally {
    await browser.close();
  }
});
