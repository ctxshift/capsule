import { codec } from '@capsule-sdk/features/codec';
import type { ProviderTransport } from '@capsule-sdk/features/runtime';
import type { Value } from '@capsule-sdk/features';
import { BagState, EquipBagItem, BagEquipResult } from './bag';
import { EquipmentState, UnequipItem, UnequipResult } from './equipment';
import { unavailableBag as UNAVAILABLE_INVENTORY_BAG } from './bag';
import { unavailableEquipment as UNAVAILABLE_CHARACTER } from './equipment';
/** Explicit browser fixtures. Intent acceptance never manufactures an owner observation. */
export function createBagMock(
  initial: Value<typeof BagState> = UNAVAILABLE_INVENTORY_BAG,
  equip: (token: number) => boolean = () => true,
) {
  let state = initial,
    bytes = codec(BagState).encode(state),
    consumed = '';
  const subscribers = new Set<(bytes: Uint8Array) => void>();
  const transport: ProviderTransport = {
    subscribe(name, receive) {
      if (name !== 'State') throw Error('Unknown bag state');
      subscribers.add(receive);
      receive(bytes);
      return () => {
        subscribers.delete(receive);
      };
    },
    async request(operation, payload, signal) {
      if (operation !== 'Equip') throw Error('Unknown bag operation');
      const intent = codec(EquipBagItem).decode(payload),
        key = intent.epoch + ':' + intent.revision;
      let accepted = false;
      if (
        !signal.aborted &&
        state.status === 'ready' &&
        intent.epoch === state.epoch &&
        intent.revision === state.revision &&
        consumed !== key &&
        state.items.some((item) => item.token === intent.token && item.canEquip)
      ) {
        accepted = equip(intent.token);
        if (accepted) consumed = key;
      }
      return codec(BagEquipResult).encode({ accepted });
    },
  };
  return {
    transport,
    publish(next: Value<typeof BagState>) {
      const encoded = codec(BagState).encode(next);
      state = next;
      bytes = encoded;
      for (const receive of subscribers) receive(bytes);
    },
  };
}
export function createEquipmentMock(
  initial: Value<typeof EquipmentState> = UNAVAILABLE_CHARACTER,
  unequip: (slot: string) => boolean = () => true,
) {
  let state = initial,
    bytes = codec(EquipmentState).encode(state),
    consumed = '';
  const subscribers = new Set<(bytes: Uint8Array) => void>();
  const transport: ProviderTransport = {
    subscribe(name, receive) {
      if (name !== 'State') throw Error('Unknown equipment state');
      subscribers.add(receive);
      receive(bytes);
      return () => {
        subscribers.delete(receive);
      };
    },
    async request(operation, payload, signal) {
      if (operation !== 'Unequip') throw Error('Unknown equipment operation');
      const intent = codec(UnequipItem).decode(payload),
        key = intent.epoch + ':' + intent.generation + ':' + intent.revision;
      let accepted = false;
      if (
        !signal.aborted &&
        state.status === 'ready' &&
        intent.epoch === state.epoch &&
        intent.generation === state.generation &&
        intent.revision === state.revision &&
        consumed !== key &&
        state.items.some((item) => item.slot === intent.slot && item.canUnequip)
      ) {
        consumed = key;
        accepted = unequip(intent.slot);
      }
      return codec(UnequipResult).encode({ accepted });
    },
  };
  return {
    transport,
    publish(next: Value<typeof EquipmentState>) {
      const encoded = codec(EquipmentState).encode(next);
      state = next;
      bytes = encoded;
      for (const receive of subscribers) receive(bytes);
    },
  };
}
