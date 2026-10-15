/** Declarative contract source. Native objects and executable functions are not schema values. */
export type Schema =
  | { readonly kind: 'bool' }
  | { readonly kind: 'u32' }
  | { readonly kind: 'text'; readonly maxBytes: number }
  | {
      readonly kind: 'list';
      readonly element: Schema;
      readonly maxCount: number;
    }
  | {
      readonly kind: 'record';
      readonly name: string;
      readonly fields: Readonly<Record<string, Schema>>;
    };
export type Value<S extends Schema> = S extends { kind: 'bool' }
  ? boolean
  : S extends { kind: 'u32' }
    ? number
    : S extends { kind: 'text' }
      ? string
      : S extends { kind: 'list'; element: infer E extends Schema }
        ? readonly Value<E>[]
        : S extends {
              kind: 'record';
              fields: infer F extends Record<string, Schema>;
            }
          ? { readonly [K in keyof F]: Value<F[K]> }
          : never;
function bound(value: number): number {
  if (!Number.isSafeInteger(value) || value < 1 || value > 0x1000000)
    throw Error('Invalid schema bound');
  return value;
}
export function identifier(value: string): string {
  // Prefix generated identifiers to avoid collisions with language keywords.
  if (!/^[A-Za-z][A-Za-z0-9]*$/.test(value))
    throw Error(`Invalid schema identifier: ${value}`);
  return value;
}
export const s = {
  bool: Object.freeze({ kind: 'bool' } as const),
  u32: Object.freeze({ kind: 'u32' } as const),
  text: (maxBytes: number) =>
    Object.freeze({ kind: 'text', maxBytes: bound(maxBytes) } as const),
  list: <S extends Schema>(element: S, maxCount: number) =>
    Object.freeze({
      kind: 'list',
      element,
      maxCount: bound(maxCount),
    } as const),
  record: <F extends Record<string, Schema>>(name: string, fields: F) => {
    identifier(name);
    if (!Object.keys(fields).length) throw Error('Empty record');
    for (const key of Object.keys(fields)) identifier(key);
    return Object.freeze({
      kind: 'record',
      name,
      fields: Object.freeze({ ...fields }),
    } as const);
  },
};
export type Feature = {
  readonly name: string;
  readonly version: string;
  readonly dependencies: Readonly<Record<string, string>>;
  readonly states: Readonly<
    Record<
      string,
      {
        readonly schema: Schema;
        readonly permission: string;
        readonly delivery: 'latest';
      }
    >
  >;
  readonly requests: Readonly<
    Record<
      string,
      {
        readonly input: Schema;
        readonly output: Schema;
        readonly permission: string;
      }
    >
  >;
};
export function defineFeature<const F extends Feature>(feature: F): F {
  if (
    !/^@[a-z0-9-]+\/[a-z0-9-]+$/.test(feature.name) ||
    !/^\d+\.\d+\.\d+$/.test(feature.version)
  )
    throw Error('Invalid feature identity');
  for (const name of [
    ...Object.keys(feature.states),
    ...Object.keys(feature.requests),
  ])
    identifier(name);
  for (const operation of [
    ...Object.values(feature.states),
    ...Object.values(feature.requests),
  ]) {
    if (!operation.permission) throw Error('Missing feature permission');
  }
  return freeze(feature);
}
export function freeze<T>(value: T): T {
  if (value && typeof value === 'object' && !Object.isFrozen(value)) {
    for (const child of Object.values(value)) freeze(child);
    Object.freeze(value);
  }
  return value;
}
