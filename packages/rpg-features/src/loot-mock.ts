import { codec } from '@capsule-sdk/features/codec';
import type { ProviderTransport } from '@capsule-sdk/features/runtime';
import {
  LootState,
  LootIntent,
  LootResult,
  OpenLoot,
  closedLoot,
  type LootStateValue,
} from './loot';
/** Browser fixture: acceptance forwards intent; publish supplies the resulting state. */
export function createLootMock(
  initial: LootStateValue = closedLoot,
  interact: (action: number) => boolean = () => true,
  open: (corpseId: string) => boolean = () => true,
) {
  let state = initial,
    consumed = 0;
  let bytes = codec(LootState).encode(state);
  const subscribers = new Set<(bytes: Uint8Array) => void>();
  function publish(next: LootStateValue) {
    const encoded = codec(LootState).encode(next);
    state = next;
    bytes = encoded;
    for (const receive of subscribers) receive(bytes);
  }
  const transport: ProviderTransport = {
    subscribe(name, receive) {
      if (name !== 'State') throw Error('Unknown loot state');
      subscribers.add(receive);
      receive(bytes);
      return () => {
        subscribers.delete(receive);
      };
    },
    async request(operation, payload, signal) {
      if (operation === 'Open') {
        const intent = codec(OpenLoot).decode(payload);
        const accepted =
          !signal.aborted &&
          intent.epoch === state.epoch &&
          state.corpses.some((corpse) => corpse.id === intent.corpseId) &&
          open(intent.corpseId);
        return codec(LootResult).encode({ accepted });
      }
      if (operation !== 'Interact') throw Error('Unknown loot operation');
      const intent = codec(LootIntent).decode(payload);
      let accepted = false;
      if (
        !signal.aborted &&
        intent.sequence !== 0 &&
        intent.sequence === state.sequence &&
        consumed !== intent.sequence &&
        state.state !== 0 &&
        intent.action <= 1 &&
        (intent.action === 0 || state.state === 2)
      ) {
        consumed = intent.sequence;
        accepted = interact(intent.action);
      }
      return codec(LootResult).encode({ accepted });
    },
  };
  return { transport, publish };
}
