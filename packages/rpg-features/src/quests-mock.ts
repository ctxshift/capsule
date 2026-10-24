import { codec } from '@capsule-sdk/features/codec';
import type { ProviderTransport } from '@capsule-sdk/features/runtime';
import { QuestState, unavailableQuests, type QuestStateValue } from './quests';
export function createQuestMock(initial: QuestStateValue = unavailableQuests) {
  let state = initial;
  const listeners = new Set<(bytes: Uint8Array) => void>();
  const transport: ProviderTransport = {
    subscribe(name, receive) {
      if (name !== 'State') throw Error('Unknown quest state');
      listeners.add(receive);
      receive(codec(QuestState).encode(state));
      return () => {
        listeners.delete(receive);
      };
    },
    async request() {
      throw Error('Quest projection is read-only');
    },
  };
  return {
    transport,
    publish(next: QuestStateValue) {
      state = next;
      for (const receive of listeners) receive(codec(QuestState).encode(state));
    },
  };
}
