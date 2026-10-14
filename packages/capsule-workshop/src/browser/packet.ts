export interface UiNode {
  id: bigint;
  parent: bigint;
  kind: number;
  rect: number[];
  style: string;
  asset: string;
  text: string;
  events: number;
  value: number;
  visible: boolean;
  brightness: number;
  layout: number[] | null;
}

export interface SurfaceAdmission {
  readonly maxLeases: number;
  allows(key: string): boolean;
}
/** Host-side resource admission, independent of addon assets and theme keys. */
export function admitSurfaces(
  keys: readonly string[],
  maxLeases = 1,
): SurfaceAdmission {
  if (
    !Number.isInteger(maxLeases) ||
    maxLeases < 1 ||
    maxLeases > 4 ||
    keys.length < 1 ||
    keys.length > 4 ||
    new Set(keys).size !== keys.length ||
    keys.some(
      (key) => typeof key !== 'string' || !/^[a-zA-Z0-9._-]{1,64}$/.test(key),
    )
  )
    throw Error('Invalid surface admission');
  const admitted = new Set(keys);
  return Object.freeze({
    maxLeases,
    allows: (key: string) => admitted.has(key),
  });
}

class Reader {
  private at = 0;
  private view: DataView;
  constructor(private bytes: Uint8Array) {
    this.view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  }
  get remaining() {
    return this.bytes.length - this.at;
  }
  take(size: number) {
    if (size < 0 || size > this.remaining) throw Error('Truncated UI packet');
    const at = this.at;
    this.at += size;
    return at;
  }
  u8() {
    return this.view.getUint8(this.take(1));
  }
  u16() {
    return this.view.getUint16(this.take(2), true);
  }
  u32() {
    return this.view.getUint32(this.take(4), true);
  }
  i32() {
    return this.view.getInt32(this.take(4), true);
  }
  u64() {
    return this.view.getBigUint64(this.take(8), true);
  }
  body(size: number) {
    const at = this.take(size);
    return new Reader(this.bytes.slice(at, at + size));
  }
  string(size: number) {
    const at = this.take(size);
    return new TextDecoder('utf-8', { fatal: true }).decode(
      this.bytes.subarray(at, at + size),
    );
  }
  key() {
    const value = this.string(this.u8());
    if (value.length > 64 || !/^[a-zA-Z0-9._-]*$/.test(value))
      throw Error('Invalid UI key');
    return value;
  }
  text() {
    const value = this.string(this.u16());
    if (value.length > 512) throw Error('Text quota');
    return value;
  }
  rect() {
    return Array.from({ length: 4 }, () => this.i32() / 64);
  }
  layout() {
    const values = [
      ...Array.from({ length: 4 }, () => this.u8()),
      ...Array.from({ length: 11 }, () => this.i32()),
    ];
    if (
      values[0] > 1 ||
      values[1] > 1 ||
      values[2] < 1 ||
      values[2] > 4 ||
      values[3] > 3
    )
      throw Error('Layout enum');
    if (
      values.slice(4).some((value) => Math.abs(value) > 8192 * 64) ||
      values.slice(8).some((value) => value < 0)
    )
      throw Error('Layout bounds');
    if (values[4] < -1 || values[5] < -1) throw Error('Layout size');
    return values;
  }
}

// A browser development adapter for the existing PUI1/v2 contract. Every
// transaction is copied/validated before publication; no guest buffer retained.
export function applyPacket(
  previous: ReadonlyMap<bigint, UiNode>,
  bytes: Uint8Array,
  maxNodes = 128,
  surfaces?: SurfaceAdmission,
): Map<bigint, UiNode> {
  if (bytes.length < 16 || bytes.length > 65536)
    throw Error('Packet byte quota');
  const reader = new Reader(bytes.slice());
  if (reader.u32() !== 0x31495550) throw Error('UI magic');
  const version = reader.u16();
  if ((version !== 1 && version !== 2) || reader.u16())
    throw Error('UI version/reserved');
  const count = reader.u16();
  if (count > 256 || reader.u16() || reader.u32() !== bytes.length)
    throw Error('UI header/quota');
  const next = new Map([...previous].map(([id, node]) => [id, { ...node }]));
  function remove(id: bigint, depth = 0) {
    if (depth >= 24) throw Error('Remove depth');
    for (const node of [...next.values()])
      if (node.parent === id) remove(node.id, depth + 1);
    next.delete(id);
  }
  for (let i = 0; i < count; i++) {
    const opcode = reader.u8();
    if (reader.u8()) throw Error('UI command flags');
    const body = reader.body(reader.u16());
    const id = body.u64();
    if (!id || (version === 1 && opcode >= 11))
      throw Error('UI handle/version');
    if (opcode === 1 || opcode === 11) {
      if (next.has(id)) throw Error('Duplicate UI handle');
      const node: UiNode = {
        id,
        parent: body.u64(),
        kind: body.u8(),
        rect: body.rect(),
        style: body.key(),
        asset: body.key(),
        text: body.text(),
        events: body.u16(),
        layout: opcode === 11 ? body.layout() : null,
        value: 0,
        brightness: 10000,
        visible: true,
      };
      if (node.kind < 1 || node.kind > 13) throw Error('Unknown UI kind');
      next.set(id, node);
    } else {
      const node = next.get(id);
      if (!node) throw Error('Unknown UI handle');
      switch (opcode) {
        case 2:
          node.text = body.text();
          break;
        case 3:
          node.asset = body.key();
          break;
        case 4:
          node.value = body.u16();
          break;
        case 5: {
          const value = body.u8();
          if (value > 1) throw Error('Visibility');
          node.visible = !!value;
          break;
        }
        case 6:
          remove(id);
          break;
        case 7:
          node.events = body.u16();
          break;
        case 8:
          node.rect = body.rect();
          break;
        case 9:
          next.delete(id);
          next.set(id, node);
          break;
        case 10:
          node.style = body.key();
          break;
        case 12:
          node.layout = body.layout();
          break;
        case 13:
          node.brightness = body.u16();
          if (node.brightness < 8000 || node.brightness > 12000)
            throw Error('Brightness');
          break;
        default:
          throw Error('Unknown UI opcode');
      }
    }
    if (body.remaining) throw Error('Trailing command bytes');
  }
  if (
    ![128, 256].includes(maxNodes) ||
    reader.remaining ||
    next.size > maxNodes
  )
    throw Error('Packet/node quota');
  let surfaceCount = 0;
  for (const node of next.values()) {
    if (
      node.kind === 13 &&
      (!surfaces?.allows(node.asset) || ++surfaceCount > surfaces.maxLeases)
    )
      throw Error('Live surface admission or quota invalid');
    if (node.value > 10000 || (node.events & ~1023) !== 0)
      throw Error('UI value/event bounds');
    if (
      node.rect.some(
        (value) => !Number.isFinite(value) || Math.abs(value) > 8192,
      )
    )
      throw Error('Rectangle bounds');
    let current = node,
      depth = 0;
    while (current.parent) {
      const parent = next.get(current.parent);
      if (!parent || ++depth >= 24) throw Error('UI parent/depth');
      current = parent;
    }
  }
  return next;
}

export function input(
  id: bigint,
  kind: number,
  payload: readonly number[] = [],
): Uint8Array {
  const bytes = new Uint8Array(16 + payload.length);
  const view = new DataView(bytes.buffer);
  view.setUint16(2, kind, true);
  view.setBigUint64(8, id, true);
  bytes.set(payload, 16);
  return bytes;
}
