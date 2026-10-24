import { expect, test } from 'vitest';
import { FeatureRegistry } from '@capsule/features/runtime';
import { descriptor } from '@capsule/features/codegen';
import { loot, closedLoot } from '@capsule/rpg-features/loot';
import { createLootMock } from '@capsule/rpg-features/loot-mock';

test('loot forwards current intent once and waits for authoritative state', async () => {
  const actions: number[] = [];
  const mock = createLootMock(
    {
      ...closedLoot,
      sequence: 42,
      state: 2,
      entries: [
        { kind: 'bear_meat', name: 'Bear Meat', quantity: 1 },
        { kind: 'iron_sword', name: 'Server Sword', quantity: 3 },
      ],
    },
    (action) => {
      actions.push(action);
      return true;
    },
  );
  const registry = new FeatureRegistry();
  const remove = registry.register(
    loot,
    descriptor(loot).fingerprint,
    mock.transport,
  );
  const connection = registry.connect(
    loot,
    '1.0.*',
    new Set(['loot.read', 'loot.interact']),
  );
  expect(
    connection.snapshot('State').value?.entries.map((item) => item.kind),
  ).toEqual(['bear_meat', 'iron_sword']);
  expect(
    await connection.request('Interact', { sequence: 41, action: 1 }),
  ).toEqual({ accepted: false });
  expect(
    await connection.request('Interact', { sequence: 42, action: 9 }),
  ).toEqual({ accepted: false });
  expect(
    await connection.request('Interact', { sequence: 42, action: 1 }),
  ).toEqual({ accepted: true });
  expect(
    await connection.request('Interact', { sequence: 42, action: 1 }),
  ).toEqual({ accepted: false });
  expect(connection.snapshot('State').value?.entries[1].quantity).toBe(3);
  mock.publish({ ...closedLoot, sequence: 43, state: 3 });
  expect(
    await connection.request('Interact', { sequence: 43, action: 1 }),
  ).toEqual({ accepted: false });
  expect(
    await connection.request('Interact', { sequence: 43, action: 0 }),
  ).toEqual({ accepted: true });
  mock.publish({ ...closedLoot, sequence: 44 });
  expect(
    await connection.request('Interact', { sequence: 44, action: 0 }),
  ).toEqual({ accepted: false });
  expect(actions).toEqual([1, 0]);
  remove();
  expect(connection.snapshot('State').available).toBe(false);
  connection.dispose();
});
