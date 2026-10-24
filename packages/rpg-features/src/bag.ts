import { ItemKind } from './items';
import { defineFeature, s } from '@capsule-sdk/features';
export const BagItem = s.record('BagItem', {
  token: s.u32,
  quantity: s.u32,
  kind: ItemKind,
  canEquip: s.bool,
});
export const BagState = s.record('BagState', {
  status: s.text(16),
  epoch: s.u32,
  revision: s.u32,
  items: s.list(BagItem, 24),
});
export const EquipBagItem = s.record('EquipBagItem', {
  epoch: s.u32,
  revision: s.u32,
  token: s.u32,
});
export const BagEquipResult = s.record('BagEquipResult', { accepted: s.bool });
export const bag = defineFeature({
  name: '@rpg/bag',
  version: '1.0.0',
  dependencies: {},
  states: {
    State: { schema: BagState, permission: 'bag.read', delivery: 'latest' },
  },
  requests: {
    Equip: {
      input: EquipBagItem,
      output: BagEquipResult,
      permission: 'bag.equip',
    },
  },
});

export const unavailableBag = Object.freeze({
  status: 'unavailable',
  epoch: 0,
  revision: 0,
  items: [],
});
