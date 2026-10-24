import { defineFeature, s, type Value } from '@capsule-sdk/features';

export const NameplateActor = s.record('NameplateActor', {
  entity: s.u32,
  generation: s.u32,
  current: s.u32,
  maximum: s.u32,
  kind: s.u32,
  relation: s.text(16),
  questMarker: s.text(16),
  name: s.text(80),
});
export const NameplateState = s.record('NameplateState', {
  available: s.bool,
  epoch: s.u32,
  selected: s.u32,
  actors: s.list(NameplateActor, 64),
});
export const SelectNameplate = s.record('SelectNameplate', {
  epoch: s.u32,
  entity: s.u32,
  generation: s.u32,
});
export const NameplateSelection = s.record('NameplateSelection', {
  selected: s.bool,
});
export const nameplates = defineFeature({
  name: '@rpg/nameplates',
  version: '1.0.0',
  dependencies: {},
  states: {
    State: {
      schema: NameplateState,
      permission: 'nameplates.read',
      delivery: 'latest',
    },
  },
  requests: {
    Select: {
      input: SelectNameplate,
      output: NameplateSelection,
      permission: 'nameplates.select',
    },
  },
});
export type NameplateStateValue = Value<typeof NameplateState>;
export const unavailableNameplates: NameplateStateValue = Object.freeze({
  available: false,
  epoch: 0,
  selected: 0,
  actors: [],
});
