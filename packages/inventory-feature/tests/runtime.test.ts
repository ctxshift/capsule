import { describe, expect, it } from 'vitest';
import {
  FeatureRegistry,
  matchesVersion,
  type ProviderTransport,
} from '@capsule/features/runtime';
import { descriptor } from '@capsule/features/codegen';
import {
  inventory,
  items,
  inventoryCodec,
  resultCodec,
} from '../src/contracts';
import { createInventoryMock } from '../src/mock';

function fixture() {
  const mock = createInventoryMock(24),
    registry = new FeatureRegistry();
  const removeItems = registry.register(
    items,
    descriptor(items).fingerprint,
    mock.itemProvider,
  );
  const removeInventory = registry.register(
    inventory,
    descriptor(inventory).fingerprint,
    mock.inventoryProvider,
  );
  const connection = registry.connect(
    inventory,
    '1.2.*',
    new Set(['inventory.read', 'inventory.transfer']),
    descriptor(inventory).fingerprint,
  );
  return { mock, registry, connection, removeItems, removeInventory };
}
describe('inventory feature connections', () => {
  it('resolves dependencies and rejects mismatched versions and fingerprints', () => {
    const { registry, removeItems } = fixture();
    expect(matchesVersion('1.2.5', '1.2.*')).toBe(true);
    expect(() => registry.connect(inventory, '2.0.*', new Set())).toThrow();
    expect(() =>
      registry.connect(inventory, '1.2.*', new Set(), 'a'.repeat(64)),
    ).toThrow('fingerprint');
    expect(() =>
      registry.register(
        items,
        descriptor(items).fingerprint,
        createInventoryMock().itemProvider,
      ),
    ).toThrow('already');
    removeItems();
    expect(() => registry.connect(inventory, '1.2.*', new Set())).toThrow(
      'Missing',
    );
    expect(() =>
      registry.register(
        items,
        'a'.repeat(64),
        createInventoryMock().itemProvider,
      ),
    ).toThrow('version change');
  });
  it('shares one immutable decoded snapshot across subscribers and retires their callbacks', async () => {
    const { connection } = fixture();
    let first = 0,
      second = 0;
    const off = connection.subscribe('State', () => first++);
    connection.subscribe('State', () => second++);
    connection.subscribe('State', () => connection.snapshot('State'));
    const before = connection.snapshot('State');
    expect(connection.snapshot('State')).toBe(before);
    expect(connection.metrics.decodedMessages).toBe(1);
    const result = await connection.request('Transfer', {
      requestId: 1,
      epoch: 1,
      revision: 1,
      item: 'item-0',
      destination: 'mainHand',
    });
    expect(result.outcome).toBe(0);
    expect(connection.snapshot('State').value?.entries[0].container).toBe(
      'mainHand',
    );
    expect(first).toBe(1);
    expect(second).toBe(1);
    expect(connection.metrics.decodedMessages).toBe(2);
    expect(Object.isFrozen(connection.snapshot('State').value?.entries)).toBe(
      true,
    );
    off();
    connection.dispose();
    expect(() => connection.snapshot('State')).toThrow('disposed');
  });
  it('enforces grants independently of compatibility and rejects stale or unsupported moves', async () => {
    const { registry, connection } = fixture();
    const readOnly = registry.connect(
      inventory,
      '1.2.*',
      new Set(['inventory.read']),
    );
    await expect(
      readOnly.request('Transfer', {
        requestId: 1,
        epoch: 1,
        revision: 1,
        item: 'item-0',
        destination: 'mainHand',
      }),
    ).rejects.toMatchObject({ outcome: 'denied' });
    const none = registry.connect(inventory, '1.2.*', new Set());
    expect(() => none.snapshot('State')).toThrow('denied');
    for (const change of [
      { revision: 0, destination: 'mainHand' },
      { revision: 1, destination: 'missing' },
    ]) {
      const result = await connection.request('Transfer', {
        requestId: 1,
        epoch: 1,
        item: 'item-0',
        ...change,
      });
      expect(result.outcome).toBe(1);
    }
    expect(connection.snapshot('State').value?.revision).toBe(1);
  });
  it('retains the connection across dependency/provider replacement without replaying actions', async () => {
    const { registry, connection, removeInventory, removeItems } = fixture();
    connection.snapshot('State');
    removeItems();
    expect(connection.snapshot('State').available).toBe(false);
    await expect(
      connection.request('Transfer', {
        requestId: 1,
        epoch: 1,
        revision: 1,
        item: 'item-0',
        destination: 'mainHand',
      }),
    ).rejects.toMatchObject({ outcome: 'unavailable' });
    registry.register(
      items,
      descriptor(items).fingerprint,
      createInventoryMock().itemProvider,
    );
    expect(connection.snapshot('State').available).toBe(true);
    removeInventory();
    const mock = createInventoryMock(3);
    registry.register(
      inventory,
      descriptor(inventory).fingerprint,
      mock.inventoryProvider,
    );
    expect(connection.snapshot('State').value?.entries).toHaveLength(3);
    expect(connection.snapshot('State').value?.revision).toBe(1);
  });
  it('reports unknown in-flight outcomes and ignores retired publications/results', async () => {
    const { registry, connection, removeInventory } = fixture();
    connection.snapshot('State');
    removeInventory();
    let emit: ((bytes: Uint8Array) => void) | undefined;
    let finish: ((bytes: Uint8Array) => void) | undefined;
    const provider: ProviderTransport = {
      subscribe(_name, receive) {
        emit = receive;
        return () => {};
      },
      request() {
        return new Promise((resolve) => {
          finish = resolve;
        });
      },
    };
    const remove = registry.register(
      inventory,
      descriptor(inventory).fingerprint,
      provider,
    );
    const pending = connection.request('Transfer', {
      requestId: 3,
      epoch: 1,
      revision: 1,
      item: 'item-0',
      destination: 'mainHand',
    });
    await Promise.resolve();
    const assertion = expect(pending).rejects.toMatchObject({
      outcome: 'unknown',
    });
    remove();
    await assertion;
    emit?.(
      inventoryCodec.encode({
        available: true,
        epoch: 1,
        revision: 999,
        entries: [],
      }),
    );
    finish?.(resultCodec.encode({ requestId: 3, outcome: 0, reason: '' }));
    await Promise.resolve();
    expect(connection.snapshot('State').available).toBe(false);
  });
});
