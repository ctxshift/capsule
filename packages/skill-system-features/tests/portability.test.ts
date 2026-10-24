import { expect, test } from 'vitest';
import { codec } from '@capsule/features/codec';
import {
  SkillProgressionState,
  unavailableProgression,
} from '../src/progression';
import {
  TrainingIntent,
  TrainingResult,
  TrainingState,
  unavailableTraining,
} from '../src/training';
import { createProgressionMock, createTrainingMock } from '../src/mocks';

test('unrelated games supply skill and perk catalogs through the same contract', () => {
  for (const [id, kind] of [
    ['navigation', 'skill'],
    ['night-vision', 'perk'],
  ]) {
    const state = {
      ...unavailableProgression,
      available: true,
      freshness: 'fresh',
      skills: [
        {
          id,
          kind,
          name: id,
          description: '',
          icon: '',
          rank: 1,
          maximumRank: 3,
          experience: 40,
          nextRankExperience: 100,
          unlocked: true,
          requirements: [],
        },
      ],
    };
    const mock = createProgressionMock(state);
    let received: unknown;
    mock.transport.subscribe('State', (b) => {
      received = codec(SkillProgressionState).decode(b);
    });
    expect(received).toEqual(state);
  }
});

test('training validates identity and eligibility without inventing unlock confirmation', async () => {
  const state = {
    ...unavailableTraining,
    freshness: 'fresh',
    epoch: 2,
    ownerGeneration: 3,
    sequence: 4,
    open: true,
    source: 'instructor',
    offers: [
      {
        id: 'navigation-2',
        skill: 'navigation',
        rank: 2,
        currentRank: 1,
        available: true,
        reason: '',
        costs: [{ resource: 'knowledge', amount: 5, affordable: true }],
        requirements: [
          { id: 'navigation-1', description: 'Navigation I', met: true },
        ],
      },
    ],
  };
  const calls: string[] = [];
  const mock = createTrainingMock(state, (id) => {
    calls.push(id);
    return true;
  });
  const signal = new AbortController().signal;
  const send = async (offer: string, sequence = 4) =>
    codec(TrainingResult).decode(
      await mock.transport.request(
        'Interact',
        codec(TrainingIntent).encode({
          epoch: 2,
          ownerGeneration: 3,
          sequence,
          offer,
          close: false,
        }),
        signal,
      ),
    );
  expect((await send('missing')).accepted).toBe(false);
  expect((await send('navigation-2', 3)).accepted).toBe(false);
  expect((await send('navigation-2')).accepted).toBe(true);
  expect((await send('navigation-2')).accepted).toBe(false);
  expect(calls).toEqual(['navigation-2']);
  let observed: unknown;
  mock.transport.subscribe('State', (b) => {
    observed = codec(TrainingState).decode(b);
  });
  expect(observed).toEqual(state);
  mock.publish({
    ...state,
    sequence: 5,
    offers: [
      {
        ...state.offers[0],
        costs: [{ resource: 'knowledge', amount: 5, affordable: false }],
      },
    ],
  });
  expect((await send('navigation-2', 5)).accepted).toBe(false);
});

test('contract bounds reject oversized identifiers before provider work', () => {
  expect(() =>
    codec(TrainingIntent).encode({
      epoch: 1,
      ownerGeneration: 1,
      sequence: 1,
      offer: 'x'.repeat(129),
      close: false,
    }),
  ).toThrow();
});
