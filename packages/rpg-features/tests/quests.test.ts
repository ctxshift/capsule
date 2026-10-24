import { expect, test } from 'vitest';
import { codec } from '@capsule-sdk/features/codec';
import { QuestState, unavailableQuests, quests } from '../src/quests';
import { createQuestMock } from '../src/quests-mock';

test('journal supports independent quests, multiple objectives and game-owned reward kinds', () => {
  const state = {
    ...unavailableQuests,
    freshness: 'fresh',
    epoch: 1,
    quests: [
      {
        id: 'explore',
        status: 'active',
        contentVersion: 1,
        title: 'Explore',
        description: '',
        npc: '',
        objectives: [
          { id: 'north', description: 'North tower', current: 1, required: 1 },
          { id: 'south', description: 'South tower', current: 0, required: 1 },
        ],
        rewards: [
          {
            kind: 'experience',
            id: 'navigation',
            name: 'Navigation',
            quantity: 100,
          },
        ],
      },
      {
        id: 'delivery',
        status: 'completed',
        contentVersion: 2,
        title: 'Delivery',
        description: '',
        npc: 'Quartermaster',
        objectives: [],
        rewards: [
          { kind: 'item', id: 'compass', name: 'Compass', quantity: 1 },
        ],
      },
    ],
  };
  const mock = createQuestMock(state);
  let received: unknown;
  const remove = mock.transport.subscribe('State', (bytes) => {
    received = codec(QuestState).decode(bytes);
  });
  expect(received).toEqual(state);
  expect(Object.keys(quests.requests)).toEqual([]);
  remove();
  mock.publish(unavailableQuests);
  expect(received).toEqual(state);
});
