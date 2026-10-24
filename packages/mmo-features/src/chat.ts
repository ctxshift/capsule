import { defineFeature, s, type Value } from '@capsule-sdk/features';
export const ChatMessage = s.record('ChatMessage', {
  id: s.text(24),
  channel: s.text(16),
  sender: s.text(160),
  text: s.text(960),
});
export const ChatState = s.record('ChatState', {
  sequence: s.u32,
  epoch: s.u32,
  ready: s.bool,
  pending: s.bool,
  accepted: s.u32,
  error: s.text(512),
  wake: s.u32,
  messages: s.list(ChatMessage, 4),
});
export const ChatSend = s.record('ChatSend', {
  epoch: s.u32,
  sequence: s.u32,
  channel: s.text(16),
  text: s.text(512),
  recipient: s.text(80),
});
export const ChatResult = s.record('ChatResult', { accepted: s.bool });
export const chat = defineFeature({
  name: '@mmo/chat',
  version: '1.0.0',
  dependencies: {},
  states: {
    State: { schema: ChatState, permission: 'chat.read', delivery: 'ordered' },
  },
  requests: {
    Send: { input: ChatSend, output: ChatResult, permission: 'chat.send' },
  },
});
export type ChatStateValue = Value<typeof ChatState>;
export const unavailableChat: ChatStateValue = Object.freeze({
  sequence: 0,
  epoch: 0,
  ready: false,
  pending: false,
  accepted: 0,
  error: '',
  wake: 0,
  messages: [],
});
