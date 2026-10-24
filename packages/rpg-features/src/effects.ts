import { defineFeature, s, type Value } from '@capsule-sdk/features';

export const StatusEffect = s.record('StatusEffect', {
  instance: s.text(128),
  id: s.text(128),
  name: s.text(128),
  icon: s.text(128),
  kind: s.text(32),
  stacks: s.u32,
  timed: s.bool,
  remainingMs: s.u32,
});
export const EffectActor = s.record('EffectActor', {
  entity: s.u32,
  generation: s.u32,
  effects: s.list(StatusEffect, 64),
});
export const EffectsState = s.record('EffectsState', {
  available: s.bool,
  epoch: s.u32,
  owner: EffectActor,
  target: EffectActor,
});
export const effects = defineFeature({
  name: '@rpg/status-effects',
  version: '1.0.0',
  dependencies: {},
  states: {
    State: {
      schema: EffectsState,
      permission: 'effects.read',
      delivery: 'latest',
    },
  },
  requests: {},
});
export type EffectsStateValue = Value<typeof EffectsState>;
const absent = Object.freeze({ entity: 0, generation: 0, effects: [] });
export const unavailableEffects: EffectsStateValue = Object.freeze({
  available: false,
  epoch: 0,
  owner: absent,
  target: absent,
});
