import './scheduler';
import React from 'react';
import Reconciler from 'react-reconciler';
import { pump, clearTasks, nextDelay, wakeHost } from './scheduler';
import { installNativeBridge } from './native-bridge';
import { anchorValues, effectValues } from '@capsule/react/presentation';
import { decodeTextEdit, validateTextInput } from '@capsule/react/text-edit';
// Private bounded scheduler hooks; addon declarations expose no browser timers.
declare global {
  function setTimeout(callback: () => void, delay?: number): number;
  function clearTimeout(id: number): void;
}
const native = installNativeBridge(globalThis as { portableUi?: unknown });
type Node = { type: string; props: any; children: Node[]; handle: bigint };
const root = { children: [] as Node[] };
const hostContext = {};
const append = (parent: any, child: Node) => {
  parent.children = parent.children.filter((n: Node) => n !== child);
  parent.children.push(child);
};
const remove = (parent: any, child: Node) => {
  parent.children = parent.children.filter((n: Node) => n !== child);
};
const insert = (parent: any, child: Node, before: Node) => {
  remove(parent, child);
  parent.children.splice(parent.children.indexOf(before), 0, child);
};
let dirty = false;
(globalThis as any).__portableNextDelayV1 = () => (dirty ? 0 : nextDelay());
const renderer = Reconciler({
  supportsMutation: true,
  supportsPersistence: false,
  supportsHydration: false,
  isPrimaryRenderer: true,
  getRootHostContext: () => hostContext,
  getChildHostContext: (context: unknown) => context,
  getPublicInstance: (n: Node) => n,
  prepareForCommit: () => null,
  resetAfterCommit: () => {
    dirty = true;
    wakeHost();
  },
  createInstance: (type: string, props: any) => ({
    type,
    props,
    children: [],
    handle: native.reserve(),
  }),
  createTextInstance: () => {
    throw Error('Text must be inside Text');
  },
  appendInitialChild: append,
  appendChild: append,
  appendChildToContainer: append,
  insertBefore: insert,
  insertInContainerBefore: insert,
  removeChild: remove,
  removeChildFromContainer: remove,
  finalizeInitialChildren: () => false,
  prepareUpdate: () => true,
  commitUpdate: (
    node: Node,
    _payload: any,
    _type: string,
    _old: any,
    props: any,
  ) => {
    node.props = props;
  },
  shouldSetTextContent: (type: string) => type === 'text',
  resetTextContent: () => {},
  clearContainer: (container: any) => {
    container.children = [];
  },
  scheduleTimeout: setTimeout,
  cancelTimeout: clearTimeout,
  noTimeout: -1,
  getCurrentEventPriority: () => 1,
  preparePortalMount: () => {},
  detachDeletedInstance: () => {},
  hideInstance: (node: Node) => {
    node.props = { ...node.props, visible: false };
  },
  unhideInstance: (node: Node) => {
    node.props = { ...node.props, visible: true };
  },
  beforeActiveInstanceBlur: () => {},
  afterActiveInstanceBlur: () => {},
} as any);
const container = renderer.createContainer(
  root,
  0,
  null,
  false,
  null,
  '',
  (e: Error) => {
    throw e;
  },
  null,
);
// Host primitives are component values directly. A functional wrapper around
// every primitive doubled Fiber work while adding no behavior or interface.
const element = (type: string) => type as unknown as React.ComponentType<any>;
export const Box = element('box'),
  WorldAnchor = element('box'),
  Effect = element('box'),
  Button = element('button'),
  Range = element('range'),
  Select = element('select'),
  Text = element('text'),
  TextInput = element('textinput'),
  Image = element('image'),
  Slot = element('slot'),
  Window = element('window'),
  Progress = element('progress'),
  Portrait = element('portrait'),
  Viewport = element('viewport'),
  Overlay = element('overlay');
let previous = new Map<bigint, any>();
let callbacks = new Map<bigint, any>();
const kinds: any = {
  box: 8,
  overlay: 8,
  text: 3,
  image: 4,
  portrait: 5,
  slot: 6,
  window: 1,
  progress: 7,
  button: 10,
  range: 11,
  select: 12,
  viewport: 13,
  textinput: 14,
};
const encode = (str: string) => {
  const out: number[] = [];
  for (const c of str) {
    const x = c.codePointAt(0)!;
    if (x < 128) out.push(x);
    else if (x < 2048) out.push(192 | (x >> 6), 128 | (x & 63));
    else if (x < 65536)
      out.push(224 | (x >> 12), 128 | ((x >> 6) & 63), 128 | (x & 63));
    else
      out.push(
        240 | (x >> 18),
        128 | ((x >> 12) & 63),
        128 | ((x >> 6) & 63),
        128 | (x & 63),
      );
  }
  return out;
};
class PacketWriter {
  readonly bytes = new Uint8Array(65536);
  readonly view = new DataView(this.bytes.buffer);
  offset = 16;
  bodyStart = 16;
  commands = 0;
  version = 0;
  packets: Uint8Array[] = [];
  reset(version = 0) {
    this.offset = this.bodyStart = 16;
    this.commands = 0;
    this.version = version;
    this.packets = [];
  }
  seal(copy = true) {
    this.view.setUint32(0, 0x31495550, true);
    this.view.setUint16(4, this.version, true);
    this.view.setUint16(8, this.commands, true);
    this.view.setUint32(12, this.offset, true);
    this.packets.push(
      copy
        ? this.bytes.slice(0, this.offset)
        : this.bytes.subarray(0, this.offset),
    );
    this.offset = this.bodyStart = 16;
    this.commands = 0;
  }
  reserve(size: number) {
    if (this.offset + size > this.bytes.length)
      throw Error('Packet byte quota');
    const at = this.offset;
    this.offset += size;
    return at;
  }
  begin() {
    // Each command is smaller than 2 KiB. Keep the wire's bounded packets
    // while allowing a full core UI to span multiple packets.
    if (this.version && (this.commands === 256 || this.offset > 63488))
      this.seal();
    this.reserve(4);
    this.bodyStart = this.offset;
    return this;
  }
  push(...values: number[]) {
    this.bytes.set(values, this.reserve(values.length));
  }
  finish(opcode: number) {
    if (++this.commands > 256) throw Error('Command quota');
    this.view.setUint8(this.bodyStart - 4, opcode);
    this.view.setUint8(this.bodyStart - 3, 0);
    this.view.setUint16(this.bodyStart - 2, this.offset - this.bodyStart, true);
  }
}
const packetWriter = new PacketWriter();
const u16 = (b: PacketWriter, v: number) =>
  b.view.setUint16(b.reserve(2), v, true);
const u32 = (b: PacketWriter, v: number) =>
  b.view.setUint32(b.reserve(4), v, true);
const u64 = (b: PacketWriter, v: bigint) =>
  b.view.setBigUint64(b.reserve(8), v, true);
const key = (b: PacketWriter, v: string) => {
  v = v || '';
  if (v.length > 96) throw Error('Key quota');
  const at = b.reserve(1 + v.length);
  b.view.setUint8(at, v.length);
  for (let i = 0; i < v.length; i++) {
    const code = v.charCodeAt(i);
    if (code > 127) throw Error('Theme keys must be ASCII');
    b.view.setUint8(at + 1 + i, code);
  }
};
const text = (b: PacketWriter, v: string) => {
  v = v || '';
  if (v.length > 512) throw Error('Text quota');
  const start = b.offset,
    at = b.reserve(2 + v.length);
  b.view.setUint16(at, v.length, true);
  for (let i = 0; i < v.length; i++) {
    const code = v.charCodeAt(i);
    if (code > 127) {
      b.offset = start;
      const bytes = encode(v);
      if (bytes.length > 512) throw Error('Text quota');
      u16(b, bytes.length);
      b.push(...bytes);
      return;
    }
    b.view.setUint8(at + 2 + i, code);
  }
};
const rect = (b: PacketWriter, r: number[]) => {
  for (const x of r) {
    if (!Number.isFinite(x)) throw Error('Invalid layout');
    u32(b, Math.round(x * 64));
  }
};
let viewport = { width: 1920, height: 1080 };
const viewportListeners = new Set<() => void>();
const readViewport = () => viewport;
const subscribeViewport = (notify: () => void) => {
  viewportListeners.add(notify);
  return () => {
    viewportListeners.delete(notify);
  };
};
export function useViewport() {
  return React.useSyncExternalStore(subscribeViewport, readViewport);
}
let yogaLayout = false;
const fixed = (value: number | undefined, fallback: number) => {
  if (value === undefined) return fallback;
  if (!Number.isFinite(value)) throw Error('Nonfinite layout value');
  return Math.round(value * 64);
};
const align = { start: 1, center: 2, end: 3, stretch: 4 };
const justify = { start: 0, center: 1, end: 2, 'space-between': 3 };
const layoutIntent = (p: any) => {
  if (
    (p.direction !== undefined &&
      p.direction !== 'row' &&
      p.direction !== 'column') ||
    (p.position !== undefined && p.position !== 'absolute')
  )
    throw Error('Invalid layout enum');
  if (
    !(String(p.align ?? 'stretch') in align) ||
    !(String(p.justify ?? 'start') in justify)
  )
    throw Error('Invalid layout enum');
  return [
    p.direction === 'row' ? 1 : 0,
    p.position === 'absolute' ? 1 : 0,
    align[(p.align ?? 'stretch') as keyof typeof align],
    justify[(p.justify ?? 'start') as keyof typeof justify],
    fixed(p.width, -1),
    fixed(p.height, -1),
    fixed(p.x, 0),
    fixed(p.y, 0),
    fixed(p.grow, 0),
    fixed(p.shrink, 64),
    fixed(p.gap, 0),
    fixed(p.paddingLeft ?? p.padding, 0),
    fixed(p.paddingTop ?? p.padding, 0),
    fixed(p.paddingRight ?? p.padding, 0),
    fixed(p.paddingBottom ?? p.padding, 0),
  ];
};
const writeLayout = (writer: PacketWriter, values: number[]) => {
  const at = writer.reserve(48);
  for (let i = 0; i < 4; i++) writer.view.setUint8(at + i, values[i]);
  for (let i = 4; i < 15; i++)
    writer.view.setInt32(at + 4 + (i - 4) * 4, values[i], true);
};
const nodeUpdates: readonly (readonly [
  string,
  number,
  (b: PacketWriter, value: any) => void,
])[] = [
  ['text', 2, text],
  ['asset', 3, key],
  ['rect', 8, rect],
  ['events', 7, u16],
  ['style', 10, key],
  ['layout', 12, writeLayout],
];
// Scalar fields and the fixed-size layout/rect arrays need no JSON allocation.
const sameField = (a: unknown, b: unknown): boolean => {
  if (a === b) return true;
  if (!Array.isArray(a) || !Array.isArray(b) || a.length !== b.length)
    return false;
  for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false;
  return true;
};
function commit() {
  if (!dirty) return;
  const next = new Map<bigint, any>();
  const handlers = new Map<bigint, any>();
  function walk(
    nodes: Node[],
    parent: bigint,
    width: number,
    height: number,
    depth: number,
    originX = 0,
    originY = 0,
  ) {
    if (depth > 24) throw Error('Layout depth quota');
    for (const node of nodes) {
      // Native admission enforces the host-owned community/core policy.
      if (next.size >= 1024) throw Error('Layout node quota');
      const p = node.props,
        w = p.width ?? (yogaLayout ? 1 : width),
        h = p.height ?? (yogaLayout ? 1 : height);
      const brightness = p.brightness ?? 1;
      if (
        !Number.isFinite(brightness) ||
        brightness < 0.8 ||
        brightness > 1.2 ||
        (!yogaLayout && brightness !== 1)
      )
        throw Error('Brightness requires packet v2 and .8..1.2');
      let x = p.x ?? 0,
        y = p.y ?? 0;
      if (p.anchor === 'top-right' || p.anchor === 'bottom-right')
        x = width - w - x;
      if (p.anchor === 'bottom-left' || p.anchor === 'bottom-right')
        y = height - h - y;
      const isOverlay = node.type === 'overlay';
      if (yogaLayout && (isOverlay || (p.anchor && p.anchor !== 'top-left')))
        throw Error(
          'Yoga prototype uses explicit absolute positioning, not legacy overlay/anchor layout',
        );
      if (
        node.type === 'select' &&
        (!Array.isArray(p.options) ||
          p.options.length < 1 ||
          p.options.length > 16 ||
          p.options.some(
            (label: unknown) =>
              typeof label !== 'string' ||
              label.length > 64 ||
              label.includes('\n'),
          ))
      )
        throw Error('Select option quota');
      if (
        node.type === 'viewport' &&
        (typeof p.surface !== 'string' ||
          !/^[a-zA-Z0-9._-]{1,64}$/.test(p.surface) ||
          p.asset)
      )
        throw Error('Viewport requires a live surface key, not an image asset');
      if (node.type !== 'viewport' && p.surface !== undefined)
        throw Error('Only Viewport accepts a live surface key');
      const n = {
        handle: node.handle,
        parent: isOverlay ? 0n : parent,
        kind: node.type === 'box' && p.clip ? 9 : kinds[node.type],
        rect: [isOverlay ? x + originX : x, isOverlay ? y + originY : y, w, h],
        style: p.style || '',
        asset: node.type === 'viewport' ? p.surface : p.asset || '',
        text:
          node.type === 'text'
            ? String(p.children ?? '')
            : node.type === 'range'
              ? String(Math.round((p.step ?? 0.01) * 10000))
              : node.type === 'select'
                ? p.options.join('\n')
                : node.type === 'textinput'
                  ? (p.text ?? '')
                  : p.title || '',
        events:
          (p.onActivate ? 1 : 0) |
          (p.onClose ? 2 : 0) |
          (p.onHover || p.onHoverChange ? 4 : 0) |
          (p.onFocus || p.onFocusChange ? 32 : 0) |
          (p.onPointer ? 64 : 0) |
          (p.onKey ? 128 : 0) |
          (p.onValueChange || p.onSelectionChange ? 256 : 0) |
          (p.onWheel ? 512 : 0) |
          (node.type === 'textinput' ? 1024 : 0),
        progress:
          node.type === 'select'
            ? (p.selectedIndex ?? 0)
            : Math.round((p.value ?? 0) * 10000),
        visible: p.visible !== false,
        layout: yogaLayout ? layoutIntent(p) : null,
        brightness: Math.round(brightness * 10000),
        worldAnchor: p.worldAnchor
          ? [
              p.worldAnchor.entity,
              p.worldAnchor.attachment,
              ...anchorValues(p.worldAnchor),
              p.worldAnchor.occlusion ? 1 : 0,
            ]
          : null,
        effect: p.effect ? effectValues(p.effect) : null,
        textInput: [
          p.maxLength ?? 512,
          p.maxBytes ?? 512,
          (p.readOnly ? 1 : 0) | (p.numeric ? 2 : 0),
          p.textRevision ?? 0,
        ],
      };
      if ((n.worldAnchor || n.effect) && node.type !== 'box')
        throw Error('Presentation requires Box');
      if (node.type === 'textinput')
        validateTextInput(n.text, {
          maxLength: n.textInput[0],
          maxBytes: n.textInput[1],
          readOnly: !!p.readOnly,
          numeric: !!p.numeric,
          revision: n.textInput[3],
        });
      next.set(node.handle, n);
      handlers.set(node.handle, p);
      let flow = 0;
      const children =
        !yogaLayout && p.direction
          ? node.children.map((child) => {
              if (child.props.position !== 'absolute') {
                const q = { ...child.props };
                if (p.direction === 'row') {
                  q.x = flow;
                  flow += (q.width ?? 0) + (p.gap ?? 0);
                } else {
                  q.y = flow;
                  flow += (q.height ?? 0) + (p.gap ?? 0);
                }
                return { ...child, props: q };
              }
              return child;
            })
          : node.children;
      walk(children, node.handle, w, h, depth + 1, originX + x, originY + y);
    }
  }
  walk(root.children, 0n, viewport.width, viewport.height, 0);
  const writer = packetWriter;
  writer.reset(yogaLayout ? 2 : 1);
  const add = (opcode: number, body: PacketWriter) => body.finish(opcode);
  const siblings = (nodes: Map<bigint, any>) => {
    const result = new Map<bigint, bigint[]>();
    for (const [id, n] of nodes) {
      let list = result.get(n.parent);
      if (!list) {
        list = [];
        result.set(n.parent, list);
      }
      list.push(id);
    }
    return result;
  };
  const before = siblings(previous),
    after = siblings(next),
    reorder = new Set<bigint>();
  for (const [parent, ids] of after) {
    const old = before.get(parent);
    if (old && !sameField(old, ids)) reorder.add(parent);
  }
  for (const [id, n] of previous)
    if (!next.has(id) && (!n.parent || next.has(n.parent))) {
      const b = writer.begin();
      u64(b, id);
      add(6, b);
    }
  for (const [id, n] of next) {
    const old = previous.get(id);
    if (!old) {
      const b = writer.begin();
      u64(b, id);
      u64(b, n.parent);
      b.push(n.kind);
      rect(b, n.rect);
      key(b, n.style);
      key(b, n.asset);
      text(b, n.text);
      u16(b, n.events);
      if (yogaLayout) writeLayout(b, n.layout);
      add(yogaLayout ? 11 : 1, b);
    } else {
      if (old.parent !== n.parent || old.kind !== n.kind)
        throw Error('Changing native kind/parent requires a new React key');
      for (const [field, op, write] of nodeUpdates) {
        if (!sameField(n[field], old[field])) {
          const b = writer.begin();
          u64(b, id);
          write(b, n[field]);
          add(op, b);
        }
      }
    }
    if (!sameField(old?.worldAnchor ?? null, n.worldAnchor)) {
      const b = writer.begin();
      u64(b, id);
      b.push(n.worldAnchor ? 1 : 0);
      if (n.worldAnchor) {
        key(b, n.worldAnchor[0]);
        key(b, n.worldAnchor[1]);
        for (const v of n.worldAnchor.slice(2, 10)) u32(b, v);
        b.push(n.worldAnchor[10]);
      }
      add(15, b);
    }
    if (!sameField(old?.effect ?? null, n.effect)) {
      const b = writer.begin();
      u64(b, id);
      b.push(n.effect ? 1 : 0);
      if (n.effect) for (const v of n.effect) u32(b, v);
      add(16, b);
    }
    if (n.kind === 14 && (!old || !sameField(old.textInput, n.textInput))) {
      const b = writer.begin();
      u64(b, id);
      u16(b, n.textInput[0]);
      u16(b, n.textInput[1]);
      b.push(n.textInput[2], 0);
      u32(b, n.textInput[3]);
      add(14, b);
    }
    if (
      (n.kind === 7 || n.kind === 11 || n.kind === 12) &&
      (!old || n.progress !== old.progress)
    ) {
      const b = writer.begin();
      u64(b, id);
      u16(b, n.progress);
      add(4, b);
    }
    if (
      (!old && n.brightness !== 10000) ||
      (old && old.brightness !== n.brightness)
    ) {
      const b = writer.begin();
      u64(b, id);
      u16(b, n.brightness);
      add(13, b);
    }
    if (!n.visible || (old && !old.visible)) {
      const b = writer.begin();
      u64(b, id);
      b.push(n.visible ? 1 : 0);
      add(5, b);
    }
    // Creation already appends in order. Raise only groups whose sibling order
    // changed; stable retained nodes need no ordering commands or native churn.
    if (reorder.has(n.parent)) {
      const b = writer.begin();
      u64(b, id);
      add(9, b);
    }
  }
  writer.seal(false);
  for (const bytes of writer.packets) native.submit(bytes);
  previous = next;
  callbacks = handlers;
  dirty = false;
}
function events() {
  for (let i = 0; i < 16; i++) {
    const bytes = native.nextEvent();
    if (!bytes.length) break;
    const v = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength),
      p = callbacks.get(v.getBigUint64(8, true));
    if (p) {
      const kind = v.getUint16(2, true);
      renderer.flushSync(() => {
        if (kind === 1)
          p.onActivate?.({
            request: (capability: number, payload = new Uint8Array(0)) =>
              native.requestAction(
                v.getBigUint64(8, true),
                capability,
                payload,
              ),
          });
        else if (kind === 2)
          p.onClose?.({
            request: (capability: number, payload = new Uint8Array(0)) =>
              native.requestAction(
                v.getBigUint64(8, true),
                capability,
                payload,
              ),
          });
        else if (kind === 1024) {
          const edit = decodeTextEdit(bytes.subarray(16));
          const event = {
            ...edit,
            request: (capability: number, payload = new Uint8Array(0)) =>
              native.requestAction(
                v.getBigUint64(8, true),
                capability,
                payload,
              ),
          };
          if (edit.phase === 'change') p.onTextChange?.(event);
          else if (edit.phase === 'composition') p.onCompositionChange?.(event);
          else if (edit.phase === 'submit') p.onSubmit?.(event);
          else if (edit.phase === 'cancel') p.onCancel?.(event);
          else p.onTextFocusChange?.(event);
        } else if (kind === 4) {
          if (
            bytes.length !== 16 &&
            (bytes.length !== 18 || bytes[16] !== 1 || bytes[17] > 1)
          )
            throw Error('Invalid hover event');
          const hovered = bytes.length === 16 || bytes[17] === 1;
          if (hovered) p.onHover?.();
          p.onHoverChange?.(hovered);
        } else if (kind === 32) {
          if (
            bytes.length !== 16 &&
            (bytes.length !== 18 || bytes[16] !== 1 || bytes[17] > 1)
          )
            throw Error('Invalid focus event');
          const focused = bytes.length === 16 || bytes[17] === 1;
          if (focused) p.onFocus?.();
          p.onFocusChange?.(focused);
        } else if (kind === 64) {
          if (bytes.length !== 32 || bytes[16] !== 1)
            throw Error('Invalid pointer event');
          p.onPointer?.({
            request: (capability: number, payload = new Uint8Array(0)) =>
              native.requestAction(
                v.getBigUint64(8, true),
                capability,
                payload,
              ),
            phase: ['down', 'move', 'up', 'cancel'][bytes[17] - 1],
            pointerId: v.getInt32(20, true),
            x: v.getInt32(24, true) / 64,
            y: v.getInt32(28, true) / 64,
            button: bytes[18],
            modifiers: bytes[19],
          });
        } else if (kind === 128) {
          if (
            bytes.length !== 21 ||
            bytes[16] !== 1 ||
            bytes[17] < 1 ||
            bytes[17] > 17 ||
            bytes[18] > 1 ||
            bytes[19] > 15 ||
            bytes[20] > 1
          )
            throw Error('Invalid key event');
          p.onKey?.({
            request: (capability: number, payload = new Uint8Array(0)) =>
              native.requestAction(
                v.getBigUint64(8, true),
                capability,
                payload,
              ),
            key: [
              'Escape',
              'Tab',
              'Enter',
              'Space',
              'Left',
              'Right',
              'Up',
              'Down',
              'E',
              'I',
              'C',
              'Home',
              'End',
              'PageUp',
              'PageDown',
              'J',
              'K',
            ][bytes[17] - 1],
            down: bytes[18] === 1,
            modifiers: bytes[19],
            repeat: bytes[20] === 1,
          });
        } else if (kind === 512) {
          if (
            bytes.length !== 28 ||
            bytes[16] !== 1 ||
            bytes[17] > 15 ||
            bytes[18] ||
            bytes[19] ||
            Math.abs(v.getInt32(20, true)) > 8192 * 64 ||
            Math.abs(v.getInt32(24, true)) > 8192 * 64
          )
            throw Error('Invalid wheel event');
          p.onWheel?.({
            deltaX: v.getInt32(20, true) / 64,
            deltaY: v.getInt32(24, true) / 64,
            modifiers: bytes[17],
          });
        } else if (kind === 256) {
          if (bytes.length !== 19 || bytes[16] !== 1)
            throw Error('Invalid value event');
          const event = {
            request: (capability: number, payload = new Uint8Array(0)) =>
              native.requestAction(
                v.getBigUint64(8, true),
                capability,
                payload,
              ),
          };
          if (p.options) p.onSelectionChange?.(v.getUint16(17, true), event);
          else p.onValueChange?.(v.getUint16(17, true) / 10000, event);
        }
      });
    }
  }
}
const observations = new Map<string, Uint8Array>();
const listeners = new Map<string, Set<() => void>>();
// Generated feature bindings subscribe once, decode once and fan out immutable state.
export function observe(
  topic: number,
  schema: number,
  receive: (bytes: Uint8Array) => void,
): () => void {
  const key = `${topic}:${schema}`;
  const notify = () => {
    const bytes = observations.get(key);
    if (bytes) receive(bytes);
  };
  let subscribers = listeners.get(key);
  if (!subscribers) {
    subscribers = new Set();
    listeners.set(key, subscribers);
  }
  subscribers.add(notify);
  notify();
  return () => {
    subscribers!.delete(notify);
    if (!subscribers!.size) listeners.delete(key);
  };
}
export function useObservation<T>(
  topic: number,
  schema: number,
  decode: (bytes: Uint8Array) => T,
  fallback: T,
): T {
  const [, refresh] = React.useState(0);
  const key = `${topic}:${schema}`;
  const bytes = observations.get(key);
  const value = bytes ? decode(bytes) : fallback;
  const current = React.useRef(value);
  current.current = value;
  React.useEffect(() => {
    const notify = () => {
      const nextBytes = observations.get(key);
      if (!nextBytes) return;
      const next = decode(nextBytes);
      if (Object.is(current.current, next)) return;
      current.current = next;
      refresh((revision) => revision + 1);
    };
    let subscribers = listeners.get(key);
    if (!subscribers) {
      subscribers = new Set();
      listeners.set(key, subscribers);
    }
    subscribers.add(notify);
    return () => {
      subscribers!.delete(notify);
      if (!subscribers!.size) listeners.delete(key);
    };
  }, [key, decode]);
  return value;
}
function receiveObservations() {
  for (let i = 0; i < 16; i++) {
    const bytes = native.nextObservation();
    if (!bytes.length) break;
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const key = `${view.getUint16(0, true)}:${view.getUint16(2, true)}`;
    if (bytes.length < 8 || view.getUint32(4, true) !== bytes.length - 8)
      throw Error('Invalid observation envelope');
    observations.set(key, bytes.subarray(8));
    renderer.flushSync(() => {
      for (const notify of [...(listeners.get(key) ?? [])]) notify();
    });
  }
}
(globalThis as any).__portableSetViewport = (width: number, height: number) => {
  if (width === viewport.width && height === viewport.height) {
    dirty = true;
    return;
  }
  viewport = { width, height };
  renderer.flushSync(() => {
    for (const notify of viewportListeners) notify();
  });
  dirty = true;
};
export function render(node: React.ReactNode, options?: { layout: 'yoga' }) {
  const requested = options?.layout === 'yoga';
  if (previous.size && requested !== yogaLayout)
    throw Error('Layout mode requires a new addon instance');
  yogaLayout = requested;
  renderer.flushSync(() =>
    renderer.updateContainer(node, container, null, () => {}),
  );
}

(globalThis as any).portableUiUpdate = () => {
  pump();
  renderer.flushPassiveEffects();
  receiveObservations();
  events();
  renderer.flushPassiveEffects();
  commit();
};
(globalThis as any).portableUiDispose = () => {
  renderer.flushSync(() =>
    renderer.updateContainer(null, container, null, () => {}),
  );
  renderer.flushPassiveEffects();
  clearTasks();
  callbacks.clear();
  listeners.clear();
  observations.clear();
  viewportListeners.clear();
};
(globalThis as any).__portableReact = Object.freeze({
  React,
  Box,
  WorldAnchor,
  Effect,
  Button,
  Range,
  Select,
  Text,
  TextInput,
  Image,
  Slot,
  Window,
  Progress,
  Portrait,
  Viewport,
  Overlay,
  render,
  useObservation,
  observe,
  requestReadOnly: (capability: number, payload: Uint8Array) =>
    native.requestAction(0n, capability, payload),
  useViewport,
});
