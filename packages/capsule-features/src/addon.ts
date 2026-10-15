import {
  FeatureConnection,
  FeatureError,
  FeatureRegistry,
  type ProviderTransport,
} from './runtime';
import type { Feature } from './schema';

export const featureChannels = {
  metadata: 65000,
  state: 65001,
  result: 65002,
  request: 65000,
} as const;
export type FeatureObserver = (
  topic: number,
  schema: number,
  receive: (bytes: Uint8Array) => void,
) => () => void;
type Descriptor = {
  id: number;
  name: string;
  version: string;
  fingerprint: string;
  states: Record<string, number>;
  requests: Record<string, number>;
};
type Action = { request(capability: number, payload: Uint8Array): void };

/** Private registry fed exclusively by loader admission. No game/engine identities here. */
export function createAddonFeatures(observe: FeatureObserver) {
  const registry = new FeatureRegistry();
  const removals: (() => void)[] = [];
  const connections: { dispose(): void }[] = [];
  const latest = new Map<number, Uint8Array>();
  const listeners = new Map<number, Set<(bytes: Uint8Array) => void>>();
  const pending = new Map<
    number,
    { resolve(bytes: Uint8Array): void; reject(error: Error): void }
  >();
  let epoch = 0,
    sequence = 0,
    disposed = false;
  const key = (id: number, member: number) => ((id << 16) | member) >>> 0;
  function retire() {
    for (const remove of removals.splice(0)) remove();
    for (const request of pending.values())
      request.reject(
        new FeatureError(
          'unknown',
          'Provider retired; reconcile before retrying',
        ),
      );
    pending.clear();
    latest.clear();
    listeners.clear();
  }
  const stopMetadata = observe(featureChannels.metadata, 1, (bytes) => {
    // Metadata contains only validated ASCII identities, permissions and identifiers.
    const metadata = JSON.parse(
      Array.from(bytes, (byte) => String.fromCharCode(byte)).join(''),
    ) as { epoch: number; features: Descriptor[] };
    retire();
    epoch = metadata.epoch;
    sequence = 0;
    for (const descriptor of metadata.features) {
      const transport: ProviderTransport = {
        subscribe(name, receive) {
          const member = descriptor.states[name];
          if (member === undefined) return () => {};
          const id = key(descriptor.id, member);
          let set = listeners.get(id);
          if (!set) listeners.set(id, (set = new Set()));
          set.add(receive);
          const bytes = latest.get(id);
          if (bytes) receive(bytes);
          return () => {
            set.delete(receive);
          };
        },
        request(name, bytes, signal, context) {
          const member = descriptor.requests[name];
          if (member === undefined)
            return Promise.reject(
              new FeatureError('denied', 'Operation not granted'),
            );
          if (disposed || signal.aborted)
            return Promise.reject(
              new FeatureError('unavailable', 'Provider retired'),
            );
          const action = context as Action | undefined;
          if (!action || typeof action.request !== 'function')
            return Promise.reject(
              new FeatureError('denied', 'Live UI action required'),
            );
          if (bytes.length > 244)
            return Promise.reject(
              new FeatureError(
                'unavailable',
                'Feature request exceeds transport capacity',
              ),
            );
          if (sequence === 0xffffffff || pending.size >= 64)
            return Promise.reject(
              new FeatureError(
                'unavailable',
                'Feature request capacity exhausted',
              ),
            );
          const call = ++sequence,
            frame = new Uint8Array(12 + bytes.length),
            view = new DataView(frame.buffer);
          view.setUint32(0, epoch, true);
          view.setUint32(4, call, true);
          view.setUint32(8, key(descriptor.id, member), true);
          frame.set(bytes, 12);
          return new Promise((resolve, reject) => {
            pending.set(call, { resolve, reject });
            try {
              action.request(featureChannels.request, frame);
            } catch (error) {
              pending.delete(call);
              reject(error);
            }
          });
        },
      };
      // The native loader resolves the complete dependency closure before admission.
      // Only direct, granted features cross this boundary; dependencies grant no access.
      removals.push(
        registry.register(
          {
            name: descriptor.name,
            version: descriptor.version,
            dependencies: {},
            states: {},
            requests: {},
          },
          descriptor.fingerprint,
          transport,
        ),
      );
    }
  });
  const stopState = observe(featureChannels.state, 1, (bytes) => {
    if (bytes.length < 8) return;
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    if (view.getUint32(0, true) !== epoch) return;
    const id = view.getUint32(4, true),
      payload = bytes.subarray(8);
    latest.set(id, payload);
    for (const receive of listeners.get(id) ?? []) receive(payload);
  });
  const stopResult = observe(featureChannels.result, 1, (bytes) => {
    if (bytes.length < 9) return;
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    if (view.getUint32(0, true) !== epoch) return;
    const call = view.getUint32(4, true),
      request = pending.get(call);
    pending.delete(call);
    if (bytes[8] === 0) request?.resolve(bytes.subarray(9));
    else
      request?.reject(
        new FeatureError(
          bytes[8] === 2 ? 'denied' : 'unknown',
          'Feature request did not complete',
        ),
      );
  });
  return {
    connect<F extends Feature>(feature: F, range: string, fingerprint: string) {
      if (disposed)
        throw new FeatureError('unavailable', 'Addon binding disposed');
      const permissions = new Set(
        [
          ...Object.values(feature.states),
          ...Object.values(feature.requests),
        ].map((member) => member.permission),
      );
      const connection = new FeatureConnection(
        registry,
        feature,
        range,
        permissions,
        fingerprint,
      );
      connections.push(connection);
      return connection;
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      stopMetadata();
      stopState();
      stopResult();
      retire();
      for (const connection of connections) connection.dispose();
    },
  };
}
