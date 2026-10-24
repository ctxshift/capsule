import type { ProviderTransport } from '@capsule-sdk/features/runtime';
import {
  inventoryCodec,
  itemCodec,
  resultCodec,
  transferCodec,
  type InventoryStateValue,
  type ItemStateValue,
} from './contracts';

/** Contract-owned workshop simulation. It has no authority in a running game. */
export function createInventoryMock(count = 24, catalog?: ItemStateValue) {
  if (!Number.isInteger(count) || count < 1 || count > 4096)
    throw Error('Invalid mock inventory count');
  let state: InventoryStateValue = {
    available: true,
    epoch: 1,
    revision: 1,
    entries: Array.from({ length: count }, (_, index) => ({
      item: `item-${index}`,
      container: 'bag',
      quantity: 1,
      transferable: true,
    })),
  };
  const items: ItemStateValue = catalog ?? {
    revision: 1,
    items: state.entries.map((entry, index) => ({
      id: entry.item,
      name: `Item ${index + 1}`,
      asset: 'item',
    })),
  };
  const listeners = new Set<(bytes: Uint8Array) => void>();
  let snapshot = inventoryCodec.encode(state);
  const descriptions = itemCodec.encode(items);
  const publish = () => {
    snapshot = inventoryCodec.encode(state);
    for (const listener of [...listeners]) listener(snapshot);
  };
  const itemProvider: ProviderTransport = {
    subscribe(_name, receive) {
      receive(descriptions);
      return () => {};
    },
    request() {
      return Promise.reject(Error('Items are read-only'));
    },
  };
  const inventoryProvider: ProviderTransport = {
    subscribe(_name, receive) {
      listeners.add(receive);
      receive(snapshot);
      return () => {
        listeners.delete(receive);
      };
    },
    async request(name, bytes, signal) {
      if (name !== 'Transfer') throw Error('Unknown inventory operation');
      const request = transferCodec.decode(bytes);
      if (signal.aborted) throw Error('Request cancelled before dispatch');
      const item = state.entries.find((entry) => entry.item === request.item);
      let outcome = 0,
        reason = '';
      if (!state.available) {
        outcome = 2;
        reason = 'disconnected';
      } else if (
        request.epoch !== state.epoch ||
        request.revision !== state.revision
      ) {
        outcome = 1;
        reason = 'stale';
      } else if (
        !item?.transferable ||
        !['bag', 'mainHand'].includes(request.destination)
      ) {
        outcome = 1;
        reason = 'unsupported';
      } else {
        state = {
          ...state,
          revision: state.revision + 1,
          entries: state.entries.map((entry) => ({
            ...entry,
            container:
              entry.item === request.item
                ? request.destination
                : request.destination === 'mainHand' &&
                    entry.container === 'mainHand'
                  ? 'bag'
                  : entry.container,
          })),
        };
        publish();
      }
      return resultCodec.encode({
        requestId: request.requestId,
        outcome,
        reason,
      });
    },
  };
  return {
    itemProvider,
    inventoryProvider,
    disconnect() {
      state = { ...state, available: false };
      publish();
    },
    reconnect() {
      state = { ...state, available: true, epoch: state.epoch + 1 };
      publish();
    },
  };
}
