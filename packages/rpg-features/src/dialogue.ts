import { defineFeature, s, type Value } from '@capsule-sdk/features';
export const DialogueState = s.record('DialogueState', {
  revision: s.u32,
  open: s.bool,
  title: s.text(128),
  body: s.text(2048),
  prompt: s.text(256),
  primary: s.text(64),
  primaryEnabled: s.bool,
  secondary: s.text(64),
  secondaryEnabled: s.bool,
});
export const DialogueIntent = s.record('DialogueIntent', {
  revision: s.u32,
  action: s.u32,
});
export const DialogueResult = s.record('DialogueResult', { accepted: s.bool });
export const dialogue = defineFeature({
  name: '@rpg/dialogue',
  version: '1.0.0',
  dependencies: {},
  states: {
    State: {
      schema: DialogueState,
      permission: 'dialogue.read',
      delivery: 'latest',
    },
  },
  requests: {
    Act: {
      input: DialogueIntent,
      output: DialogueResult,
      permission: 'dialogue.act',
    },
  },
});
export type DialogueStateValue = Value<typeof DialogueState>;

export const DialogueAction = Object.freeze({
  Close: 0,
  Primary: 1,
  Secondary: 2,
});
