import { defineFeature, s, type Value } from '@capsule/features';

export const HudUnitState = s.record('HudUnitState', {
  present: s.bool,
  name: s.text(80),
  current: s.u32,
  maximum: s.u32,
  portrait: s.text(16),
});
export const HudFrameState = s.record('HudFrameState', {
  available: s.bool,
  epoch: s.u32,
  player: HudUnitState,
  target: HudUnitState,
});
export const hudFrames = defineFeature({
  name: '@rpg/hud-frames',
  version: '1.0.0',
  dependencies: {},
  states: {
    State: {
      schema: HudFrameState,
      permission: 'hud.read',
      delivery: 'latest',
    },
  },
  requests: {},
});
export type HudFrameStateValue = Value<typeof HudFrameState>;
export const absentHudUnit: Value<typeof HudUnitState> = Object.freeze({
  present: false,
  name: '',
  current: 0,
  maximum: 0,
  portrait: 'player',
});
export const unavailableHud: HudFrameStateValue = Object.freeze({
  available: false,
  epoch: 0,
  player: absentHudUnit,
  target: absentHudUnit,
});
