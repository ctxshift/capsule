import { defineFeature, s, type Value } from '@capsule-sdk/features';
import { codec } from '@capsule-sdk/features/codec';

export const Item = s.record('Item', {
  id: s.text(128),
  name: s.text(128),
  asset: s.text(128),
});
export const ItemState = s.record('ItemState', {
  revision: s.u32,
  items: s.list(Item, 4096),
});
export const InventoryEntry = s.record('InventoryEntry', {
  item: s.text(128),
  container: s.text(64),
  quantity: s.u32,
  transferable: s.bool,
});
export const InventoryState = s.record('InventoryState', {
  available: s.bool,
  epoch: s.u32,
  revision: s.u32,
  entries: s.list(InventoryEntry, 4096),
});
export const Transfer = s.record('Transfer', {
  requestId: s.u32,
  epoch: s.u32,
  revision: s.u32,
  item: s.text(128),
  destination: s.text(64),
});
// 0 succeeded; 1 rejected; 2 unavailable; 3 outcome unknown. Never use receipt as success.
export const TransferResult = s.record('TransferResult', {
  requestId: s.u32,
  outcome: s.u32,
  reason: s.text(256),
});
export const items = defineFeature({
  name: '@capsule-example/items',
  version: '1.0.0',
  dependencies: {},
  states: {
    State: { schema: ItemState, permission: 'items.read', delivery: 'latest' },
  },
  requests: {},
});
export const inventory = defineFeature({
  name: '@capsule-example/inventory',
  version: '1.2.5',
  dependencies: { '@capsule-example/items': '1.0.*' },
  states: {
    State: {
      schema: InventoryState,
      permission: 'inventory.read',
      delivery: 'latest',
    },
  },
  requests: {
    Transfer: {
      input: Transfer,
      output: TransferResult,
      permission: 'inventory.transfer',
    },
  },
});
export type ItemStateValue = Value<typeof ItemState>;
export type InventoryStateValue = Value<typeof InventoryState>;
export type TransferValue = Value<typeof Transfer>;
export type TransferResultValue = Value<typeof TransferResult>;
export const itemCodec = codec(ItemState);
export const inventoryCodec = codec(InventoryState);
export const transferCodec = codec(Transfer);
export const resultCodec = codec(TransferResult);
