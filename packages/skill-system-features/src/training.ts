import { defineFeature, s, type Value } from '@capsule-sdk/features';
import {
  SkillCost,
  SkillRequirement,
  ProgressionResource,
} from './progression';

export const TrainingOffer = s.record('TrainingOffer', {
  id: s.text(128),
  skill: s.text(128),
  rank: s.u32,
  currentRank: s.u32,
  available: s.bool,
  reason: s.text(512),
  costs: s.list(SkillCost, 16),
  requirements: s.list(SkillRequirement, 32),
});
export const TrainingState = s.record('TrainingState', {
  freshness: s.text(16),
  epoch: s.u32,
  ownerGeneration: s.u32,
  sequence: s.u32,
  open: s.bool,
  source: s.text(128),
  offers: s.list(TrainingOffer, 128),
  resources: s.list(ProgressionResource, 32),
});
export const TrainingIntent = s.record('TrainingIntent', {
  epoch: s.u32,
  ownerGeneration: s.u32,
  sequence: s.u32,
  offer: s.text(128),
  close: s.bool,
});
export const TrainingResult = s.record('TrainingResult', { accepted: s.bool });
export const training = defineFeature({
  name: '@skill-system/training',
  version: '1.0.0',
  dependencies: {},
  states: {
    State: {
      schema: TrainingState,
      permission: 'training.read',
      delivery: 'latest',
    },
  },
  requests: {
    Interact: {
      input: TrainingIntent,
      output: TrainingResult,
      permission: 'training.interact',
    },
  },
});
export type TrainingStateValue = Value<typeof TrainingState>;
export const unavailableTraining: TrainingStateValue = Object.freeze({
  freshness: 'unavailable',
  epoch: 0,
  ownerGeneration: 0,
  sequence: 0,
  open: false,
  source: '',
  offers: [],
  resources: [],
});
