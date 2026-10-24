import { codec } from '@capsule/features/codec';
import type { ProviderTransport } from '@capsule/features/runtime';
import { HudFrameState, unavailableHud, type HudFrameStateValue } from './hud';

/** Contract-owned fixture; explicit composition, never a disconnected-game fallback. */
export function createHudMock(initial: HudFrameStateValue = unavailableHud) {
  let bytes = codec(HudFrameState).encode(initial);
  const subscribers = new Set<(bytes: Uint8Array) => void>();
  const transport: ProviderTransport = {
    subscribe(name, receive) {
      if (name !== 'State') throw Error('Unknown HUD state');
      subscribers.add(receive);
      receive(bytes);
      return () => {
        subscribers.delete(receive);
      };
    },
    request: () => Promise.reject(Error('HUD feature is read-only')),
  };
  return {
    transport,
    publish(state: HudFrameStateValue) {
      bytes = codec(HudFrameState).encode(state);
      for (const receive of subscribers) receive(bytes);
    },
  };
}
