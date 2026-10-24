import { expect, test } from 'vitest';
import { FeatureRegistry } from '@capsule-sdk/features/runtime';
import { descriptor } from '@capsule-sdk/features/codegen';
import { merchant, closedMerchant } from '@capsule-sdk/rpg-features/merchant';
import { createMerchantMock } from '@capsule-sdk/rpg-features/merchant-mock';
test('merchant requests carry observation identity without calculating local prices', async () => {
  const calls: number[][] = [];
  const mock = createMerchantMock(
    { ...closedMerchant, available: true, ready: true, sequence: 7, coins: 10 },
    (a, i) => {
      calls.push([a, i]);
      return true;
    },
  );
  const registry = new FeatureRegistry();
  const remove = registry.register(
    merchant,
    descriptor(merchant).fingerprint,
    mock.transport,
  );
  const connection = registry.connect(
    merchant,
    '1.0.*',
    new Set(['merchant.read', 'merchant.interact']),
  );
  expect(
    await connection.request('Interact', { sequence: 6, action: 1, index: 0 }),
  ).toEqual({ accepted: false });
  expect(
    await connection.request('Interact', { sequence: 7, action: 1, index: 0 }),
  ).toEqual({ accepted: true });
  expect(
    await connection.request('Interact', { sequence: 7, action: 1, index: 0 }),
  ).toEqual({ accepted: false });
  expect(calls).toEqual([[1, 0]]);
  expect(connection.snapshot('State').value?.coins).toBe(10);
  mock.publish({
    ...closedMerchant,
    available: true,
    pending: true,
    sequence: 8,
  });
  expect(
    await connection.request('Interact', { sequence: 8, action: 2, index: 0 }),
  ).toEqual({ accepted: false });
  remove();
  expect(connection.snapshot('State').available).toBe(false);
  connection.dispose();
});
