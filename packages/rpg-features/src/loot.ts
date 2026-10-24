import { ItemKind } from './items';
import { defineFeature, s, type Value } from '@capsule-sdk/features';
export const LootState = s.record('LootState', {
  sequence: s.u32,
  state: s.u32,
  entries: s.list(
    s.record('LootEntry', {
      kind: ItemKind,
      name: s.text(128),
      quantity: s.u32,
    }),
    24,
  ),
  epoch: s.u32,
  corpses: s.list(
    s.record('LootableCorpse', { id: s.text(128), reference: s.text(64) }),
    64,
  ),
});
export const LootIntent = s.record('LootIntent', {
  sequence: s.u32,
  action: s.u32,
});
export const LootResult = s.record('LootResult', { accepted: s.bool });
export const OpenLoot = s.record('OpenLoot', {
  epoch: s.u32,
  corpseId: s.text(128),
});
export const loot = defineFeature({
  name: '@rpg/loot',
  version: '1.0.0',
  dependencies: {},
  states: {
    State: { schema: LootState, permission: 'loot.read', delivery: 'latest' },
  },
  requests: {
    Interact: {
      input: LootIntent,
      output: LootResult,
      permission: 'loot.interact',
    },
    Open: {
      input: OpenLoot,
      output: LootResult,
      permission: 'loot.interact',
    },
  },
});
export type LootStateValue = Value<typeof LootState>;
export const closedLoot: LootStateValue = Object.freeze({
  sequence: 0,
  state: 0,
  entries: [],
  epoch: 0,
  corpses: [],
});

export const LootAction = Object.freeze({ Close: 0, Take: 1 });
