import { expect, test } from 'vitest';
import { createAddonFeatures, featureChannels } from '../src/addon';
import { defineFeature, s } from '../src/schema';
import { codec } from '../src/codec';

test('variable-length requests preserve bytes and enforce contract bounds before dispatch', async () => {
  const input = s.record('Message', { text: s.text(512) });
  const result = s.record('Result', { accepted: s.bool });
  const feature = defineFeature({
    name: '@example/chat',
    version: '1.0.0',
    dependencies: {},
    states: {},
    requests: { Send: { input, output: result, permission: 'send' } },
  });
  const callbacks = new Map<number, (bytes: Uint8Array) => void>();
  const addon = createAddonFeatures((topic, _schema, receive) => {
    callbacks.set(topic, receive);
    return () => callbacks.delete(topic);
  });
  const connection = addon.connect(feature, '1.0.*', 'a'.repeat(64));
  callbacks.get(featureChannels.metadata)!(
    new TextEncoder().encode(
      JSON.stringify({
        epoch: 1,
        features: [
          {
            id: 1,
            name: feature.name,
            version: feature.version,
            fingerprint: 'a'.repeat(64),
            states: {},
            requests: { Send: 1 },
          },
        ],
      }),
    ),
  );
  let calls = 0;
  const action = {
    request(capability: number, frame: Uint8Array) {
      calls++;
      expect(capability).toBe(featureChannels.request);
      expect(codec(input).decode(frame.subarray(12)).text).toBe(
        '界'.repeat(170),
      );
      const reply = new Uint8Array(10);
      reply.set(frame.subarray(0, 8));
      reply[9] = 1;
      callbacks.get(featureChannels.result)!(reply);
    },
  };
  await expect(
    connection.request('Send', { text: '界'.repeat(170) }, action),
  ).resolves.toEqual({ accepted: true });
  expect(() =>
    connection.request('Send', { text: '界'.repeat(171) }, action),
  ).toThrow('Text bound exceeded');
  expect(calls).toBe(1);
  addon.dispose();
});
