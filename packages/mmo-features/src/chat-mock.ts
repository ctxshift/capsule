import { codec } from '@capsule/features/codec';
import type { ProviderTransport } from '@capsule/features/runtime';
import {
  ChatState,
  ChatSend,
  ChatResult,
  unavailableChat,
  type ChatStateValue,
} from './chat';
export function createChatMock(initial: ChatStateValue = unavailableChat) {
  let state = initial,
    consumed = 0;
  const subscribers = new Set<(bytes: Uint8Array) => void>();
  function publish(next: ChatStateValue) {
    if (next.epoch !== state.epoch) consumed = 0;
    state = next;
    for (const receive of subscribers) receive(codec(ChatState).encode(state));
  }
  const transport: ProviderTransport = {
    subscribe(name, receive) {
      if (name !== 'State') throw Error('Unknown chat state');
      subscribers.add(receive);
      receive(codec(ChatState).encode(state));
      return () => {
        subscribers.delete(receive);
      };
    },
    async request(name, payload, signal) {
      const value = codec(ChatSend).decode(payload);
      const accepted =
        name === 'Send' &&
        !signal.aborted &&
        state.ready &&
        !state.pending &&
        value.epoch === state.epoch &&
        value.sequence === state.sequence &&
        value.sequence !== consumed &&
        ['local', 'general', 'party', 'whisper'].includes(value.channel) &&
        !!value.text.trim() &&
        value.text.length <= 240 &&
        !Array.from(value.text).some(
          (c) => c.charCodeAt(0) < 32 || c.charCodeAt(0) === 127,
        ) &&
        value.recipient.length <= 20;
      if (accepted) {
        consumed = value.sequence;
        publish({
          ...state,
          sequence: state.sequence + 1,
          accepted: value.sequence,
        });
      }
      return codec(ChatResult).encode({ accepted });
    },
  };
  return { transport, publish };
}
