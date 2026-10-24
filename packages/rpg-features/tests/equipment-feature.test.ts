import { expect, test } from 'vitest';
import { FeatureRegistry } from '@capsule/features/runtime';
import { descriptor } from '@capsule/features/codegen';
import { bag } from '@capsule/rpg-features/bag';
import { equipment } from '@capsule/rpg-features/equipment';
import {
  createBagMock,
  createEquipmentMock,
} from '@capsule/rpg-features/equipment-mock';
import { unavailableBag as UNAVAILABLE_INVENTORY_BAG } from '../src/bag';
import { unavailableEquipment as UNAVAILABLE_CHARACTER } from '../src/equipment';
test('bag and equipment fixtures require current owner state and do not mutate on acceptance', async () => {
  const registry = new FeatureRegistry();
  const bagMock = createBagMock({
    status: 'ready',
    epoch: 7,
    revision: 4,
    items: [
      { token: 3, quantity: 37, kind: 'arrows', canEquip: false },
      { token: 4, quantity: 1, kind: 'iron_sword', canEquip: true },
    ],
  });
  const equipmentMock = createEquipmentMock({
    status: 'ready',
    epoch: 7,
    generation: 2,
    revision: 5,
    health: 20,
    surface: true,
    items: [{ slot: 'main_hand', kind: 'training_sword', canUnequip: true }],
  });
  const removeBag = registry.register(
    bag,
    descriptor(bag).fingerprint,
    bagMock.transport,
  );
  const removeEquipment = registry.register(
    equipment,
    descriptor(equipment).fingerprint,
    equipmentMock.transport,
  );
  const bagConnection = registry.connect(
    bag,
    '1.0.*',
    new Set(['bag.read', 'bag.equip']),
  );
  const equipmentConnection = registry.connect(
    equipment,
    '1.0.*',
    new Set(['equipment.read', 'equipment.unequip']),
  );
  for (const intent of [
    { epoch: 6, revision: 4, token: 4 },
    { epoch: 7, revision: 3, token: 4 },
    { epoch: 7, revision: 4, token: 3 },
  ])
    expect(await bagConnection.request('Equip', intent)).toEqual({
      accepted: false,
    });
  expect(
    await bagConnection.request('Equip', { epoch: 7, revision: 4, token: 4 }),
  ).toEqual({ accepted: true });
  expect(
    await bagConnection.request('Equip', { epoch: 7, revision: 4, token: 4 }),
  ).toEqual({ accepted: false });
  expect(bagConnection.snapshot('State').value?.items).toHaveLength(2);
  for (const intent of [
    { epoch: 6, generation: 2, revision: 5, slot: 'main_hand' },
    { epoch: 7, generation: 1, revision: 5, slot: 'main_hand' },
    { epoch: 7, generation: 2, revision: 4, slot: 'main_hand' },
    { epoch: 7, generation: 2, revision: 5, slot: 'head' },
  ])
    expect(await equipmentConnection.request('Unequip', intent)).toEqual({
      accepted: false,
    });
  expect(
    await equipmentConnection.request('Unequip', {
      epoch: 7,
      generation: 2,
      revision: 5,
      slot: 'main_hand',
    }),
  ).toEqual({ accepted: true });
  expect(
    await equipmentConnection.request('Unequip', {
      epoch: 7,
      generation: 2,
      revision: 5,
      slot: 'main_hand',
    }),
  ).toEqual({ accepted: false });
  expect(equipmentConnection.snapshot('State').value?.items).toHaveLength(1);
  bagMock.publish(UNAVAILABLE_INVENTORY_BAG);
  equipmentMock.publish(UNAVAILABLE_CHARACTER);
  expect(bagConnection.snapshot('State').value?.items).toHaveLength(0);
  expect(equipmentConnection.snapshot('State').value?.surface).toBe(false);
  removeBag();
  removeEquipment();
  expect(bagConnection.snapshot('State').available).toBe(false);
  expect(equipmentConnection.snapshot('State').available).toBe(false);
  bagConnection.dispose();
  equipmentConnection.dispose();
});

test('a rejected equip can retry the same observation without permitting duplicate accepted intent', async () => {
  let attempts = 0;
  const mock = createBagMock(
    {
      status: 'ready',
      epoch: 7,
      revision: 4,
      items: [{ token: 4, quantity: 1, kind: 'iron_sword', canEquip: true }],
    },
    () => ++attempts > 1,
  );
  const registry = new FeatureRegistry();
  const remove = registry.register(
    bag,
    descriptor(bag).fingerprint,
    mock.transport,
  );
  const connection = registry.connect(
    bag,
    '1.0.*',
    new Set(['bag.read', 'bag.equip']),
  );
  const intent = { epoch: 7, revision: 4, token: 4 };
  expect(await connection.request('Equip', intent)).toEqual({
    accepted: false,
  });
  expect(await connection.request('Equip', intent)).toEqual({ accepted: true });
  expect(await connection.request('Equip', intent)).toEqual({
    accepted: false,
  });
  expect(attempts).toBe(2);
  remove();
  connection.dispose();
});
