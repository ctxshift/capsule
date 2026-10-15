import { codec } from './codec';
import type { Feature, Schema, Value } from './schema';

export type Outcome = 'unavailable' | 'denied' | 'unknown';
export class FeatureError extends Error {
  constructor(
    readonly outcome: Outcome,
    message: string,
  ) {
    super(message);
  }
}
/** Host/private transport. Payload ownership transfers to the receiver; never mutate after publish. */
export interface RequestCancellation {
  readonly aborted: boolean;
}
export interface ProviderTransport {
  subscribe(
    state: string,
    receive: (bytes: Uint8Array) => void,
    invalidate?: () => void,
  ): () => void;
  refresh?(state: string): void;
  request(
    operation: string,
    bytes: Uint8Array,
    signal: RequestCancellation,
    context?: unknown,
  ): Promise<Uint8Array>;
}
type StateKey<F extends Feature> = keyof F['states'] & string;
type RequestKey<F extends Feature> = keyof F['requests'] & string;
type StateValue<F extends Feature, K extends StateKey<F>> = Value<
  F['states'][K]['schema']
>;
export type Snapshot<T> = Readonly<{ available: boolean; value?: T }>;
type Registration = {
  feature: Feature;
  fingerprint: string;
  transport: ProviderTransport;
  generation: number;
};
export function matchesVersion(version: string, range: string): boolean {
  if (!/^\d+\.\d+\.\d+$/.test(version) || !/^\d+\.\d+\.(\d+|\*)$/.test(range))
    throw Error('Unsupported feature version/range');
  const parts = range.split('.');
  return parts[2] === '*'
    ? version.startsWith(`${parts[0]}.${parts[1]}.`)
    : version === range;
}

/** Explicit host composition only. Addons receive connections, never this mutable registry. */
export class FeatureRegistry {
  private providers = new Map<string, Registration>();
  private identities = new Map<string, string>();
  private listeners = new Set<() => void>();
  private sequence = 0;
  register(
    feature: Feature,
    fingerprint: string,
    transport: ProviderTransport,
  ): () => void {
    if (!/^[a-f0-9]{64}$/.test(fingerprint))
      throw Error('Invalid feature fingerprint');
    const identity = `${feature.name}@${feature.version}`;
    const previous = this.identities.get(identity);
    if (previous && previous !== fingerprint)
      throw Error('Feature definition changed without a version change');
    if (this.providers.has(feature.name))
      throw Error('Provider already selected; unregister before replacement');
    this.identities.set(identity, fingerprint);
    const registration = {
      feature,
      fingerprint,
      transport,
      generation: ++this.sequence,
    };
    this.providers.set(feature.name, registration);
    this.changed();
    return () => {
      if (this.providers.get(feature.name) !== registration) return;
      this.providers.delete(feature.name);
      this.changed();
    };
  }
  private changed() {
    for (const listener of [...this.listeners]) listener();
  }
  watch(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  }
  resolve(
    name: string,
    range: string,
    visiting = new Set<string>(),
  ): Registration {
    if (visiting.has(name))
      throw new FeatureError(
        'unavailable',
        `Cyclic feature dependency: ${name}`,
      );
    const registration = this.providers.get(name);
    if (!registration || !matchesVersion(registration.feature.version, range))
      throw new FeatureError(
        'unavailable',
        `Missing compatible feature: ${name}@${range}`,
      );
    const next = new Set(visiting).add(name);
    for (const [dependency, version] of Object.entries(
      registration.feature.dependencies,
    ))
      this.resolve(dependency, version, next);
    return registration;
  }
  connect<F extends Feature>(
    feature: F,
    range: string,
    grants: ReadonlySet<string>,
    fingerprint?: string,
  ): FeatureConnection<F> {
    const registration = this.resolve(feature.name, range);
    if (
      fingerprint &&
      registration.feature.version === feature.version &&
      registration.fingerprint !== fingerprint
    )
      throw Error('Feature schema fingerprint mismatch');
    return new FeatureConnection(
      this,
      feature,
      range,
      new Set(grants),
      fingerprint,
    );
  }
}

export class FeatureConnection<F extends Feature> {
  private registration?: Registration;
  private states = new Map<
    string,
    {
      snapshot: Snapshot<unknown>;
      listeners: Set<() => void>;
      stop?: () => void;
    }
  >();
  private pending = new Set<{
    signal: { aborted: boolean };
    reject: (reason: FeatureError) => void;
  }>();
  private stopWatch: () => void;
  private disposed = false;
  readonly metrics = { decodedMessages: 0, decodedBytes: 0 };
  constructor(
    private registry: FeatureRegistry,
    readonly feature: F,
    private range: string,
    private grants: ReadonlySet<string>,
    private fingerprint?: string,
  ) {
    this.stopWatch = registry.watch(() => this.refresh());
    this.refresh();
  }
  private refresh(): void {
    let next: Registration | undefined;
    if (!this.disposed)
      try {
        next = this.registry.resolve(this.feature.name, this.range);
        if (
          this.fingerprint &&
          next.feature.version === this.feature.version &&
          next.fingerprint !== this.fingerprint
        )
          next = undefined;
      } catch (error) {
        if (!(error instanceof FeatureError)) throw error;
      }
    if (next === this.registration) return;
    this.registration = next;
    for (const request of this.pending) {
      request.signal.aborted = true;
      request.reject(
        new FeatureError(
          'unknown',
          'Provider retired; reconcile before retrying',
        ),
      );
    }
    this.pending.clear();
    for (const [name, state] of this.states) {
      state.stop?.();
      state.stop = undefined;
      state.snapshot = Object.freeze({ available: false });
      if (next) this.attach(name, state, next);
      for (const listener of [...state.listeners]) listener();
    }
  }
  private attach(
    name: string,
    state: {
      snapshot: Snapshot<unknown>;
      listeners: Set<() => void>;
      stop?: () => void;
    },
    registration: Registration,
  ) {
    state.stop = registration.transport.subscribe(
      name,
      (bytes) => {
        if (this.registration !== registration || this.disposed) return;
        const decoded = codec(this.feature.states[name].schema).decode(bytes);
        this.metrics.decodedMessages++;
        this.metrics.decodedBytes += bytes.length;
        state.snapshot = Object.freeze({ available: true, value: decoded });
        for (const listener of [...state.listeners]) listener();
      },
      () => {
        if (this.registration !== registration || this.disposed) return;
        state.snapshot = Object.freeze({ available: false });
        for (const listener of [...state.listeners]) listener();
      },
    );
  }
  private state(name: string) {
    if (this.disposed)
      throw new FeatureError('unavailable', 'Connection disposed');
    const definition = this.feature.states[name];
    if (!definition || !this.grants.has(definition.permission))
      throw new FeatureError('denied', `Observation denied: ${name}`);
    let state = this.states.get(name);
    if (!state) {
      state = {
        snapshot: Object.freeze({ available: false }),
        listeners: new Set<() => void>(),
      };
      this.states.set(name, state);
      if (this.registration) this.attach(name, state, this.registration);
    }
    return state;
  }
  snapshot<K extends StateKey<F>>(name: K): Snapshot<StateValue<F, K>> {
    return this.state(name).snapshot as Snapshot<StateValue<F, K>>;
  }
  /** Re-read granted state; never executes a gameplay operation. */
  refreshSnapshot<K extends StateKey<F>>(name: K): void {
    const state = this.state(name);
    const registration = this.registration;
    if (!registration)
      throw new FeatureError('unavailable', 'Provider unavailable');
    if (registration.transport.refresh) registration.transport.refresh(name);
    else {
      state.stop?.();
      this.attach(name, state, registration);
    }
  }
  subscribe<K extends StateKey<F>>(name: K, listener: () => void): () => void {
    const state = this.state(name);
    state.listeners.add(listener);
    return () => {
      state.listeners.delete(listener);
    };
  }
  request<K extends RequestKey<F>>(
    name: K,
    input: Value<F['requests'][K]['input']>,
    context?: unknown,
  ): Promise<Value<F['requests'][K]['output']>> {
    const operation = this.feature.requests[name];
    if (!operation || !this.grants.has(operation.permission))
      return Promise.reject(
        new FeatureError('denied', `Request denied: ${name}`),
      );
    const registration = this.registration;
    if (!registration || this.disposed)
      return Promise.reject(
        new FeatureError('unavailable', 'Provider unavailable'),
      );
    const bytes = codec(operation.input).encode(input as Value<Schema>);
    return new Promise((resolve, reject) => {
      const pending = { signal: { aborted: false }, reject };
      this.pending.add(pending);
      Promise.resolve()
        .then(() => {
          if (!this.pending.has(pending))
            throw new FeatureError('unknown', 'Provider retired');
          return registration.transport.request(
            name,
            bytes,
            pending.signal,
            context,
          );
        })
        .then((result) => {
          if (
            !this.pending.delete(pending) ||
            registration !== this.registration
          )
            return;
          resolve(
            codec(operation.output).decode(result) as Value<
              F['requests'][K]['output']
            >,
          );
        })
        .catch((error) => {
          this.pending.delete(pending);
          reject(error);
        });
    });
  }
  dispose(): void {
    if (this.disposed) return;
    this.disposed = true;
    this.stopWatch();
    // Teardown retires callbacks before invalidating state; a subscriber may read its snapshot.
    for (const state of this.states.values()) state.listeners.clear();
    this.refresh();
    this.states.clear();
  }
}
