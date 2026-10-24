import { expect, test } from 'vitest';
import { FeatureRegistry } from '@capsule/features/runtime';
import { descriptor } from '@capsule/features/codegen';
import { bag } from '@capsule/rpg-features/bag';
import { createBagMock } from '@capsule/rpg-features/equipment-mock';
import { chat } from '../src/chat';
import { createChatMock } from '../src/chat-mock';
test('same bag consumer works with independent game catalogs and no MMO provider', async () => {
  for (const kind of ['example_blade', 'example_tool']) {
    const registry = new FeatureRegistry();
    const mock = createBagMock({
      status: 'ready',
      epoch: 1,
      revision: 2,
      items: [{ token: 7, kind, quantity: 1, canEquip: true }],
    });
    registry.register(bag, descriptor(bag).fingerprint, mock.transport);
    const ui = registry.connect(
      bag,
      '1.0.*',
      new Set(['bag.read', 'bag.equip']),
    );
    expect(ui.snapshot('State').value?.items[0].kind).toBe(kind);
    expect(
      await ui.request('Equip', { epoch: 1, revision: 2, token: 7 }),
    ).toEqual({ accepted: true });
    ui.dispose();
  }
});
test('chat is independently admitted without inventory or trade providers', () => {
  const registry = new FeatureRegistry(),
    mock = createChatMock();
  registry.register(chat, descriptor(chat).fingerprint, mock.transport);
  const ui = registry.connect(chat, '1.0.*', new Set(['chat.read']));
  expect(ui.snapshot('State').available).toBe(true);
  ui.dispose();
});
