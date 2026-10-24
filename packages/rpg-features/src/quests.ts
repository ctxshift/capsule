import { defineFeature, s, type Value } from '@capsule-sdk/features';

export const QuestObjective = s.record('QuestObjective', {
  id: s.text(128),
  description: s.text(1024),
  current: s.u32,
  required: s.u32,
});
export const QuestReward = s.record('QuestReward', {
  kind: s.text(32),
  id: s.text(128),
  name: s.text(128),
  quantity: s.u32,
});
export const QuestEntry = s.record('QuestEntry', {
  id: s.text(128),
  status: s.text(16),
  contentVersion: s.u32,
  title: s.text(512),
  description: s.text(4096),
  npc: s.text(512),
  objectives: s.list(QuestObjective, 8),
  rewards: s.list(QuestReward, 16),
});
export const QuestState = s.record('QuestState', {
  freshness: s.text(16),
  epoch: s.u32,
  ownerGeneration: s.u32,
  sequence: s.u32,
  quests: s.list(QuestEntry, 64),
});
export const quests = defineFeature({
  name: '@rpg/quests',
  version: '1.0.0',
  dependencies: {},
  states: {
    State: {
      schema: QuestState,
      permission: 'quests.read',
      delivery: 'latest',
    },
  },
  requests: {},
});
export type QuestStateValue = Value<typeof QuestState>;
export const unavailableQuests: QuestStateValue = Object.freeze({
  freshness: 'unavailable',
  epoch: 0,
  ownerGeneration: 0,
  sequence: 0,
  quests: [],
});
