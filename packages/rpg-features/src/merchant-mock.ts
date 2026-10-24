import { codec } from '@capsule-sdk/features/codec';
import type { ProviderTransport } from '@capsule-sdk/features/runtime';
import {
  MerchantState,
  MerchantIntent,
  MerchantResult,
  closedMerchant,
  type MerchantStateValue,
} from './merchant';
export function createMerchantMock(
  initial: MerchantStateValue = closedMerchant,
  interact: (action: number, index: number) => boolean = () => true,
) {
  let state = initial,
    consumed = 0;
  const listeners = new Set<(bytes: Uint8Array) => void>();
  function publish(next: MerchantStateValue) {
    state = next;
    for (const receive of listeners)
      receive(codec(MerchantState).encode(state));
  }
  const transport: ProviderTransport = {
    subscribe(name, receive) {
      if (name !== 'State') throw Error('Unknown merchant state');
      listeners.add(receive);
      receive(codec(MerchantState).encode(state));
      return () => {
        listeners.delete(receive);
      };
    },
    async request(name, payload, signal) {
      const value = codec(MerchantIntent).decode(payload);
      let accepted = false;
      if (
        name === 'Interact' &&
        !signal.aborted &&
        state.available &&
        value.sequence === state.sequence &&
        value.sequence !== consumed &&
        value.index < 64 &&
        value.action < 4 &&
        (!(value.action === 1 || value.action === 2) || state.ready)
      ) {
        consumed = value.sequence;
        accepted = interact(value.action, value.index);
      }
      return codec(MerchantResult).encode({ accepted });
    },
  };
  return { transport, publish };
}
