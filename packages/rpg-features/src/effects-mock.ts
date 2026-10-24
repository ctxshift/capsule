import { codec } from '@capsule-sdk/features/codec';
import type { ProviderTransport } from '@capsule-sdk/features/runtime';
import {
  EffectsState,
  unavailableEffects,
  type EffectsStateValue,
} from './effects';

/** Contract-owned fixture; explicit composition, never a disconnected-game fallback. */
export function createEffectsMock(
  initial: EffectsStateValue = unavailableEffects,
) {
  let bytes = codec(EffectsState).encode(initial);
  const subscribers = new Set<(bytes: Uint8Array) => void>();
  const transport: ProviderTransport = {
    subscribe(name, receive) {
      if (name !== 'State') throw Error('Unknown Effects state');
      subscribers.add(receive);
      receive(bytes);
      return () => {
        subscribers.delete(receive);
      };
    },
    request: () => Promise.reject(Error('Effects feature is read-only')),
  };
  return {
    transport,
    publish(state: EffectsStateValue) {
      bytes = codec(EffectsState).encode(state);
      for (const receive of subscribers) receive(bytes);
    },
  };
}
