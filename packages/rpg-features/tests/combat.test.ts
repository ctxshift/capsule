import { expect, test } from 'vitest';
import { codec } from '@capsule/features/codec';
import {
  CombatState,
  ActivateCombat,
  CombatActivation,
  unavailableCombat,
} from '../src/combat';
import { createCombatMock } from '../src/combat-mock';

test('unrelated abilities keep separate cast/cooldown timers and observed request identities', async () => {
  const state = {
    ...unavailableCombat,
    available: true,
    epoch: 1,
    slots: [
      {
        action: 'fireball',
        name: 'Fireball',
        icon: 'fire',
        available: false,
        active: false,
        phase: 'casting',
        durationMs: 1500,
        revision: 2,
        queued: false,
      },
      {
        action: 'heal',
        name: 'Heal',
        icon: 'leaf',
        available: true,
        active: false,
        phase: 'cooldown',
        durationMs: 5000,
        revision: 9,
        queued: true,
      },
    ],
  };
  const calls: string[] = [];
  const mock = createCombatMock(state, (_slot, action) => {
    calls.push(action);
    return true;
  });
  let observed: unknown;
  mock.transport.subscribe('State', (b) => {
    observed = codec(CombatState).decode(b);
  });
  expect(observed).toEqual(state);
  const request = async (action: string) =>
    codec(CombatActivation).decode(
      await mock.transport.request(
        'Activate',
        codec(ActivateCombat).encode({ epoch: 1, slot: 1, action }),
        new AbortController().signal,
      ),
    );
  expect(await request('fireball')).toEqual({ accepted: false });
  expect(await request('heal')).toEqual({ accepted: true });
  expect(calls).toEqual(['heal']);
  expect(observed).toEqual(state);
});
