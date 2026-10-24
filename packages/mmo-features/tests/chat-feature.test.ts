import { expect, test } from 'vitest';
import { FeatureRegistry } from '@capsule-sdk/features/runtime';
import { descriptor } from '@capsule-sdk/features/codegen';
import { chat, unavailableChat } from '@capsule-sdk/mmo-features/chat';
import { createChatMock } from '@capsule-sdk/mmo-features/chat-mock';
test('chat feature preserves long messages, session guards and acknowledgements without local echo', async () => {
  const mock = createChatMock({
    ...unavailableChat,
    ready: true,
    epoch: 1,
    sequence: 1,
  });
  const registry = new FeatureRegistry();
  const remove = registry.register(
    chat,
    descriptor(chat).fingerprint,
    mock.transport,
  );
  const connection = registry.connect(
    chat,
    '1.0.*',
    new Set(['chat.read', 'chat.send']),
  );
  const send = {
    epoch: 1,
    sequence: 1,
    channel: 'general',
    text: '界'.repeat(170),
    recipient: '',
  };
  expect(await connection.request('Send', send)).toEqual({ accepted: true });
  expect(connection.snapshot('State').value).toMatchObject({
    accepted: 1,
    messages: [],
  });
  expect(await connection.request('Send', send)).toEqual({ accepted: false });
  mock.publish({ ...unavailableChat, ready: true, epoch: 2, sequence: 1 });
  expect(await connection.request('Send', send)).toEqual({ accepted: false });
  expect(() =>
    connection.request('Send', { ...send, text: '界'.repeat(171) }),
  ).toThrow();
  remove();
  expect(connection.snapshot('State').available).toBe(false);
  connection.dispose();
});
