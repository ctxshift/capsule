/** Host-issued entity reference includes its lifetime; never a Unity path or object. */
export interface WorldAnchorOptions {
  entity: string;
  attachment: string;
  offset?: readonly [number, number, number];
  near?: number;
  far?: number;
  /** Zero means fixed scale; otherwise referenceDistance / camera distance, clamped. */
  referenceDistance?: number;
  minScale?: number;
  maxScale?: number;
  /** Explicit host occlusion query; missing policy hides the anchor. */
  occlusion?: boolean;
}
export interface PresentationEffectOptions {
  /** Change to restart; ordinary rerenders preserve the original start time. */
  revision: number;
  durationMs: number;
  fadeMs?: number;
  /** Total movement in design pixels, positive y down. */
  offset?: readonly [number, number];
}
export function anchorValues(value: WorldAnchorOptions): number[] {
  if (
    !/^[a-zA-Z0-9._-]{1,64}$/.test(value.entity) ||
    !/^[a-zA-Z0-9._-]{1,64}$/.test(value.attachment)
  )
    throw Error('World reference');
  const a = [
    ...(value.offset ?? [0, 0, 0]),
    value.near ?? 0,
    value.far ?? 100,
    value.referenceDistance ?? 0,
    value.minScale ?? 1,
    value.maxScale ?? 1,
  ].map((v) => Math.round(v * 64));
  if (
    a.length !== 8 ||
    a.some((v) => !Number.isSafeInteger(v)) ||
    a.slice(0, 3).some((v) => Math.abs(v) > 640) ||
    a[3] < 0 ||
    a[4] <= a[3] ||
    a[4] > 64000 ||
    a[5] < 0 ||
    a[5] > 64000 ||
    a[6] < 16 ||
    a[7] < a[6] ||
    a[7] > 256
  )
    throw Error('World anchor bounds');
  return a;
}
export function effectValues(value: PresentationEffectOptions): number[] {
  const a = [
    value.revision,
    value.durationMs,
    value.fadeMs ?? 0,
    ...(value.offset ?? [0, 0]).map((v) => Math.round(v * 64)),
  ];
  if (
    a.length !== 5 ||
    a.some((v) => !Number.isSafeInteger(v)) ||
    a[0] < 0 ||
    a[0] > 0xffffffff ||
    a[1] < 1 ||
    a[1] > 10000 ||
    a[2] < 0 ||
    a[2] > a[1] ||
    Math.abs(a[3]) > 65536 ||
    Math.abs(a[4]) > 65536
  )
    throw Error('Effect bounds');
  return a;
}
