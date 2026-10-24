import { codec } from '@capsule/features/codec';
import type { ProviderTransport } from '@capsule/features/runtime';
import {
  TradeState,
  TradeIntent,
  TradeResult,
  closedTrade,
  type TradeStateValue,
} from './trade';
export function createTradeMock(
  initial: TradeStateValue = closedTrade,
  interact: (action: number, value: number) => boolean = () => true,
) {
  let state = initial,
    consumed = 0;
  const listeners = new Set<(bytes: Uint8Array) => void>();
  function publish(next: TradeStateValue) {
    state = next;
    for (const receive of listeners) receive(codec(TradeState).encode(state));
  }
  const transport: ProviderTransport = {
    subscribe(name, receive) {
      if (name !== 'State') throw Error('Unknown trade state');
      listeners.add(receive);
      receive(codec(TradeState).encode(state));
      return () => {
        listeners.delete(receive);
      };
    },
    async request(name, payload, signal) {
      const value = codec(TradeIntent).decode(payload);
      let accepted = false;
      if (
        name === 'Interact' &&
        !signal.aborted &&
        state.status !== 'pending' &&
        value.sequence === state.sequence &&
        value.sequence !== consumed &&
        value.action < 8 &&
        value.value <= 2000000000 &&
        (value.action !== 0 || state.canCreate)
      ) {
        consumed = value.sequence;
        accepted = interact(value.action, value.value);
      }
      return codec(TradeResult).encode({ accepted });
    },
  };
  return { transport, publish };
}
