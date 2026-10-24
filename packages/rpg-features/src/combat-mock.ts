import { codec } from '@capsule-sdk/features/codec';
import type { ProviderTransport } from '@capsule-sdk/features/runtime';
import {
  CombatState,
  CombatActivation,
  ActivateCombat,
  unavailableCombat,
  type CombatStateValue,
} from './combat';

/** Contract fixture for browser development; the host supplies gameplay behavior. */
export function createCombatMock(
  initial: CombatStateValue = unavailableCombat,
  activate: (slot: number, action: string) => boolean = () => true,
) {
  let state = initial;
  let bytes = codec(CombatState).encode(state);
  const subscribers = new Set<(bytes: Uint8Array) => void>();
  function publish(next: CombatStateValue) {
    const encoded = codec(CombatState).encode(next);
    state = next;
    bytes = encoded;
    for (const receive of subscribers) receive(bytes);
  }
  const transport: ProviderTransport = {
    subscribe(name, receive) {
      if (name !== 'State') throw Error('Unknown combat state');
      subscribers.add(receive);
      receive(bytes);
      return () => {
        subscribers.delete(receive);
      };
    },
    async request(operation, payload, signal) {
      if (operation !== 'Activate') throw Error('Unknown combat operation');
      const intent = codec(ActivateCombat).decode(payload);
      const accepted =
        !signal.aborted &&
        state.available &&
        intent.epoch === state.epoch &&
        intent.action !== '' &&
        state.slots[intent.slot]?.action === intent.action &&
        activate(intent.slot, intent.action);
      return codec(CombatActivation).encode({ accepted });
    },
  };
  return { transport, publish };
}
