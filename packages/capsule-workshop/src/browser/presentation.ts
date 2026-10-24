import type {
  WorldAnchorOptions,
  PresentationEffectOptions,
} from '@capsule/react/presentation';
import type { UiNode } from './packet';

export interface WorldProjection {
  x: number;
  y: number;
  scale: number;
  visible: boolean;
}
export interface WorldAttachment {
  /** Missing means permanently revoked/despawned; invisible may become visible again. */
  project(anchor: WorldAnchorOptions): WorldProjection | undefined;
}
export interface BrowserWorldSource {
  dispose(): void;
  acquire(
    handle: bigint,
    anchor: WorldAnchorOptions,
  ): WorldAttachment | undefined;
}

/** Deterministic workshop camera looking along +Z; all values are supplied by the host. */
export class SimulatedWorld implements BrowserWorldSource {
  camera = { x: 0, y: 0, z: 0, near: 0.1, far: 1000, fov: 60, aspect: 16 / 9 };
  private grants = new Map<
    string,
    {
      point: () => readonly [number, number, number] | undefined;
      permitted: () => boolean;
      visible: () => boolean;
      revoked: boolean;
    }
  >();
  private disposed = false;
  constructor(
    private owner: number,
    private generation: number,
  ) {
    if (
      !Number.isInteger(owner) ||
      owner < 1 ||
      owner > 0xffffffff ||
      !Number.isInteger(generation) ||
      generation < 1 ||
      generation > 65535
    )
      throw Error('World owner');
  }
  grant(
    entity: string,
    attachment: string,
    point: () => readonly [number, number, number] | undefined,
    permitted: () => boolean,
    visible: () => boolean = () => false,
  ) {
    const key = entity + '/' + attachment;
    if (this.disposed || this.grants.size >= 64 || this.grants.has(key))
      throw Error('World grant quota or duplicate');
    this.grants.set(key, { point, permitted, visible, revoked: false });
  }
  revoke(entity: string, attachment: string) {
    const key = entity + '/' + attachment,
      grant = this.grants.get(key);
    if (grant) grant.revoked = true;
    this.grants.delete(key);
  }
  acquire(
    handle: bigint,
    anchor: WorldAnchorOptions,
  ): WorldAttachment | undefined {
    const grant = this.grants.get(anchor.entity + '/' + anchor.attachment);
    if (
      this.disposed ||
      handle >> 32n !== BigInt(this.owner) ||
      ((handle >> 16n) & 65535n) !== BigInt(this.generation) ||
      !grant ||
      grant.revoked
    )
      return;
    return {
      project: (value) => {
        if (this.disposed || grant.revoked) return;
        let point: readonly [number, number, number] | undefined;
        try {
          if (!grant.permitted()) {
            grant.revoked = true;
            return;
          }
          point = grant.point();
        } catch {
          grant.revoked = true;
          return;
        }
        if (!point || point.some((v) => !Number.isFinite(v))) {
          grant.revoked = true;
          return;
        }
        const c = this.camera,
          offset = value.offset ?? [0, 0, 0];
        const dx = point[0] + offset[0] - c.x,
          dy = point[1] + offset[1] - c.y,
          dz = point[2] + offset[2] - c.z;
        const distance = Math.hypot(dx, dy, dz),
          focal = 1 / Math.tan((c.fov * Math.PI) / 360);
        const x = 0.5 + (dx * focal) / (2 * dz * c.aspect),
          y = 0.5 - (dy * focal) / (2 * dz);
        let visible =
          dz >= c.near &&
          dz <= c.far &&
          distance >= (value.near ?? 0) &&
          distance <= (value.far ?? 100) &&
          x >= 0 &&
          x <= 1 &&
          y >= 0 &&
          y <= 1;
        if (visible && value.occlusion) {
          try {
            visible = grant.visible();
          } catch {
            visible = false;
          }
        }
        const reference = value.referenceDistance ?? 0;
        return {
          x,
          y,
          visible,
          scale: Math.max(
            value.minScale ?? 1,
            Math.min(
              value.maxScale ?? 1,
              reference === 0 ? 1 : reference / Math.max(distance, 0.0001),
            ),
          ),
        };
      },
    };
  }
  dispose() {
    this.disposed = true;
    this.grants.clear();
  }
}

export class EffectClock {
  private revision: number | undefined;
  private start = 0;
  private latest = 0;
  private expired = false;
  private time(now: number) {
    if (Number.isFinite(now)) this.latest = Math.max(this.latest, now);
    return this.latest;
  }
  configure(effect: PresentationEffectOptions | undefined, now: number) {
    now = this.time(now);
    if (!effect) {
      this.revision = undefined;
      this.expired = false;
    } else if (this.revision !== effect.revision) {
      this.revision = effect.revision;
      this.start = now;
      this.expired = false;
    }
  }
  sample(effect: PresentationEffectOptions | undefined, now: number) {
    now = this.time(now);
    if (!effect) return { x: 0, y: 0, alpha: 1, visible: true };
    const elapsed = Math.max(0, now - this.start),
      t = Math.min(1, elapsed / effect.durationMs);
    if (elapsed >= effect.durationMs) this.expired = true;
    return {
      x: (effect.offset?.[0] ?? 0) * t,
      y: (effect.offset?.[1] ?? 0) * t,
      alpha: effect.fadeMs
        ? Math.max(
            0,
            Math.min(1, (effect.durationMs - elapsed) / effect.fadeMs),
          )
        : 1,
      visible: !this.expired,
    };
  }
}

export function bindPresentation(
  element: HTMLElement,
  stage: HTMLElement,
  world?: BrowserWorldSource,
) {
  const clock = new EffectClock();
  let node: UiNode,
    attachment: WorldAttachment | undefined,
    identity = '',
    retired = false,
    base = [0, 0];
  return {
    apply(value: UiNode, rectangle: readonly number[], now: number) {
      const next = value.worldAnchor
        ? value.worldAnchor.entity + '/' + value.worldAnchor.attachment
        : '';
      if (next !== identity) {
        identity = next;
        retired = false;
        attachment = value.worldAnchor
          ? world?.acquire(value.id, value.worldAnchor)
          : undefined;
      }
      node = value;
      base = [rectangle[0], rectangle[1]];
      clock.configure(node.effect, now);
    },
    tick(now: number) {
      const effect = clock.sample(node.effect, now);
      let x = base[0] + effect.x,
        y = base[1] + effect.y,
        scale = 1,
        visible = node.visible && effect.visible && !retired;
      if (visible && node.worldAnchor) {
        const projected = attachment?.project(node.worldAnchor);
        if (!projected) {
          retired = true;
          visible = false;
        } else {
          visible = projected.visible;
          scale = projected.scale;
          const parent = element.parentElement!,
            parentRect = parent.getBoundingClientRect(),
            stageRect = stage.getBoundingClientRect();
          const factor = stage.clientWidth / stageRect.width;
          x +=
            projected.x * stage.clientWidth -
            (parentRect.left - stageRect.left) * factor;
          y +=
            projected.y * stage.clientHeight -
            (parentRect.top - stageRect.top) * factor;
        }
      }
      element.style.left = `${x}px`;
      element.style.top = `${y}px`;
      element.style.transformOrigin = '0 0';
      element.style.transform = `scale(${scale})`;
      element.style.opacity = visible ? String(effect.alpha) : '0';
      element.style.visibility = visible ? '' : 'hidden';
      element.inert = !visible || effect.alpha <= 0;
      if (
        element.inert &&
        element.contains(element.ownerDocument.activeElement)
      )
        (element.ownerDocument.activeElement as HTMLElement)?.blur();
    },
    reset() {
      element.style.transform = '';
      element.style.opacity = '';
      element.style.visibility = '';
      element.inert = false;
    },
  };
}
