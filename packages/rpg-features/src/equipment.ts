import { ItemKind } from './items';
import { defineFeature, s } from '@capsule-sdk/features';
export const EquippedItem = s.record('EquippedItem', {
  slot: s.text(16),
  kind: ItemKind,
  canUnequip: s.bool,
});
export const EquipmentState = s.record('EquipmentState', {
  status: s.text(16),
  epoch: s.u32,
  generation: s.u32,
  revision: s.u32,
  health: s.u32,
  surface: s.bool,
  items: s.list(EquippedItem, 8),
});
export const UnequipItem = s.record('UnequipItem', {
  epoch: s.u32,
  generation: s.u32,
  revision: s.u32,
  slot: s.text(16),
});
export const UnequipResult = s.record('UnequipResult', { accepted: s.bool });
export const equipment = defineFeature({
  name: '@rpg/equipment',
  version: '1.0.0',
  dependencies: {},
  states: {
    State: {
      schema: EquipmentState,
      permission: 'equipment.read',
      delivery: 'latest',
    },
  },
  requests: {
    Unequip: {
      input: UnequipItem,
      output: UnequipResult,
      permission: 'equipment.unequip',
    },
  },
});

export const unavailableEquipment = Object.freeze({
  status: 'unavailable',
  epoch: 0,
  generation: 0,
  revision: 0,
  health: 0,
  surface: false,
  items: [],
});
