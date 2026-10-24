import { expect, test } from 'vitest';
import { codec } from '@capsule-sdk/features/codec';
import { EffectsState, unavailableEffects } from '../src/effects';
import { createEffectsMock } from '../src/effects-mock';

test('effects represent independent instances, stacks, permanent buffs and timed debuffs', () => {
  const state = {
    ...unavailableEffects,
    available: true,
    epoch: 2,
    owner: {
      entity: 1,
      generation: 4,
      effects: [
        {
          instance: 'aura-1',
          id: 'night-vision',
          name: 'Night vision',
          icon: 'moon',
          kind: 'buff',
          stacks: 1,
          timed: false,
          remainingMs: 0,
        },
        {
          instance: 'poison-2',
          id: 'poison',
          name: 'Poison',
          icon: 'poison',
          kind: 'debuff',
          stacks: 3,
          timed: true,
          remainingMs: 2500,
        },
      ],
    },
  };
  const mock = createEffectsMock(state);
  let latest: unknown;
  mock.transport.subscribe('State', (bytes) => {
    latest = codec(EffectsState).decode(bytes);
  });
  expect(latest).toEqual(state);
  const next = {
    ...state,
    owner: { ...state.owner, effects: state.owner.effects.slice(0, 1) },
  };
  mock.publish(next);
  expect(latest).toEqual(next);
});
