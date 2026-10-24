import { defineFeature, s, type Value } from '@capsule-sdk/features';

export const SkillRequirement = s.record('SkillRequirement', {
  id: s.text(128),
  description: s.text(512),
  met: s.bool,
});
export const SkillCost = s.record('SkillCost', {
  resource: s.text(128),
  amount: s.u32,
  affordable: s.bool,
});
export const SkillProgress = s.record('SkillProgress', {
  id: s.text(128),
  kind: s.text(32),
  name: s.text(128),
  description: s.text(2048),
  icon: s.text(128),
  rank: s.u32,
  maximumRank: s.u32,
  experience: s.u32,
  nextRankExperience: s.u32,
  unlocked: s.bool,
  requirements: s.list(SkillRequirement, 32),
});
export const ProgressionResource = s.record('ProgressionResource', {
  id: s.text(128),
  value: s.u32,
  spent: s.u32,
  limit: s.u32,
});
export const SkillProgressionState = s.record('SkillProgressionState', {
  freshness: s.text(16),
  epoch: s.u32,
  ownerGeneration: s.u32,
  sequence: s.u32,
  available: s.bool,
  skills: s.list(SkillProgress, 256),
  resources: s.list(ProgressionResource, 32),
});
export const progression = defineFeature({
  name: '@skill-system/progression',
  version: '1.0.0',
  dependencies: {},
  states: {
    State: {
      schema: SkillProgressionState,
      permission: 'progression.read',
      delivery: 'latest',
    },
  },
  requests: {},
});
export type SkillProgressionStateValue = Value<typeof SkillProgressionState>;
export const unavailableProgression: SkillProgressionStateValue = Object.freeze(
  {
    freshness: 'unavailable',
    epoch: 0,
    ownerGeneration: 0,
    sequence: 0,
    available: false,
    skills: [],
    resources: [],
  },
);
