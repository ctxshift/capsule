import { expect, test } from 'vitest';
import { FeatureRegistry } from '@capsule/features/runtime';
import { descriptor } from '@capsule/features/codegen';
import { trade, closedTrade } from '@capsule/mmo-features/trade';
import { createTradeMock } from '@capsule/mmo-features/trade-mock';
test('trade acceptance is an intent; revision replacement and retirement invalidate old gestures', async () => {
  const calls: number[] = [];
  const mock = createTradeMock(
    { ...closedTrade, status: 'open', sequence: 1, revision: 3, coins: 50 },
    (a) => {
      calls.push(a);
      return true;
    },
  );
  const registry = new FeatureRegistry();
  const remove = registry.register(
    trade,
    descriptor(trade).fingerprint,
    mock.transport,
  );
  const connection = registry.connect(
    trade,
    '1.0.*',
    new Set(['trade.read', 'trade.interact']),
  );
  expect(
    await connection.request('Interact', { sequence: 1, action: 4, value: 0 }),
  ).toEqual({ accepted: true });
  expect(connection.snapshot('State').value).toMatchObject({
    status: 'open',
    accepted: false,
    coins: 50,
  });
  expect(
    await connection.request('Interact', { sequence: 1, action: 4, value: 0 }),
  ).toEqual({ accepted: false });
  mock.publish({ ...closedTrade, status: 'open', sequence: 2, revision: 4 });
  expect(
    await connection.request('Interact', { sequence: 1, action: 4, value: 0 }),
  ).toEqual({ accepted: false });
  expect(
    await connection.request('Interact', { sequence: 2, action: 6, value: 0 }),
  ).toEqual({ accepted: true });
  expect(calls).toEqual([4, 6]);
  remove();
  expect(connection.snapshot('State').available).toBe(false);
  connection.dispose();
});
