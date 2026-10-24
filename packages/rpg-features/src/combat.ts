import { defineFeature, s, type Value } from '@capsule-sdk/features';

export const CombatSlot = s.record('CombatSlot', {
  action: s.text(128),
  available: s.bool,
  active: s.bool,
  name: s.text(128),
  icon: s.text(128),
  phase: s.text(16),
  durationMs: s.u32,
  revision: s.u32,
  queued: s.bool,
});
export const CombatHit = s.record('CombatHit', {
  entity: s.u32,
  generation: s.u32,
  revision: s.u32,
  damage: s.u32,
  lane: s.u32,
  incoming: s.bool,
  healing: s.bool,
});
export const CombatState = s.record('CombatState', {
  available: s.bool,
  epoch: s.u32,
  slots: s.list(CombatSlot, 64),
  hits: s.list(CombatHit, 64),
  errorRevision: s.u32,
  error: s.text(80),
});
export const ActivateCombat = s.record('ActivateCombat', {
  epoch: s.u32,
  slot: s.u32,
  action: s.text(128),
});
export const CombatActivation = s.record('CombatActivation', {
  accepted: s.bool,
});
export const combat = defineFeature({
  name: '@rpg/combat',
  version: '1.0.0',
  dependencies: {},
  states: {
    State: {
      schema: CombatState,
      permission: 'combat.read',
      delivery: 'ordered',
    },
  },
  requests: {
    Activate: {
      input: ActivateCombat,
      output: CombatActivation,
      permission: 'combat.activate',
    },
  },
});
export type CombatStateValue = Value<typeof CombatState>;
export const unavailableCombat: CombatStateValue = Object.freeze({
  available: false,
  epoch: 0,
  slots: [],
  hits: [],
  errorRevision: 0,
  error: '',
});
