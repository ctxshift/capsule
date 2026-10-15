import { describe, expect, it } from 'vitest';
import { createAddonFeatures, featureChannels } from '../src/addon';
import { defineFeature, s } from '../src/schema';
import { codec } from '../src/codec';

const value = s.record('Value', { count: s.u32 });
const feature = defineFeature({
  name: '@example/counter',
  version: '1.0.0',
  dependencies: {},
  states: { State: { schema: value, permission: 'read', delivery: 'latest' } },
  requests: { Update: { input: value, output: value, permission: 'write' } },
});
const fingerprint = 'a'.repeat(64);
function fixture() {
  const callbacks = new Map<number, (bytes: Uint8Array) => void>();
  const addon = createAddonFeatures((topic, _schema, receive) => {
    callbacks.set(topic, receive);
    return () => {
      callbacks.delete(topic);
    };
  });
  const connection = addon.connect(feature, '1.0.*', fingerprint);
  const send = (topic: number, bytes: Uint8Array) =>
    callbacks.get(topic)?.(bytes);
  function metadata(epoch = 7, present = true, hash = fingerprint) {
    send(
      featureChannels.metadata,
      new TextEncoder().encode(
        JSON.stringify({
          epoch,
          features: present
            ? [
                {
                  id: 1,
                  name: feature.name,
                  version: feature.version,
                  fingerprint: hash,
                  states: { State: 1 },
                  requests: { Update: 1 },
                },
              ]
            : [],
        }),
      ),
    );
  }
  function state(epoch: number, count: number) {
    const bytes = new Uint8Array(12),
      view = new DataView(bytes.buffer);
    view.setUint32(0, epoch, true);
    view.setUint32(4, 0x10001, true);
    bytes.set(codec(value).encode({ count }), 8);
    send(featureChannels.state, bytes);
  }
  return { addon, connection, callbacks, send, metadata, state };
}
describe('generic addon feature transport', () => {
  it('keeps a stable unavailable connection, caches states and drops retired generations', () => {
    const f = fixture();
    expect(f.connection.snapshot('State').available).toBe(false);
    f.metadata();
    f.state(7, 12);
    expect(f.connection.snapshot('State').value).toEqual({ count: 12 });
    f.metadata(8);
    f.state(7, 99);
    expect(f.connection.snapshot('State').available).toBe(false);
    f.state(8, 13);
    expect(f.connection.snapshot('State').value).toEqual({ count: 13 });
    f.metadata(8, false);
    expect(f.connection.snapshot('State').available).toBe(false);
    f.addon.dispose();
    expect(f.callbacks.size).toBe(0);
  });
  it('frames arbitrary typed operations and decodes their asynchronous result', async () => {
    const f = fixture();
    f.metadata();
    const result = f.connection.request(
      'Update',
      { count: 5 },
      {
        request(capability: number, frame: Uint8Array) {
          expect(capability).toBe(featureChannels.request);
          const input = new DataView(frame.buffer);
          expect(input.getUint32(8, true)).toBe(0x10001);
          expect(codec(value).decode(frame.subarray(12))).toEqual({ count: 5 });
          const reply = new Uint8Array(13);
          reply.set(frame.subarray(0, 8));
          reply.set(codec(value).encode({ count: 6 }), 9);
          f.send(featureChannels.result, reply);
        },
      },
    );
    await expect(result).resolves.toEqual({ count: 6 });
    f.addon.dispose();
  });
  it('reports unknown pending outcomes on retirement and never retries them', async () => {
    const f = fixture();
    f.metadata();
    let calls = 0;
    const result = f.connection.request(
      'Update',
      { count: 5 },
      {
        request() {
          calls++;
        },
      },
    );
    const rejected = expect(result).rejects.toMatchObject({
      outcome: 'unknown',
    });
    await Promise.resolve();
    f.metadata(8, false);
    await rejected;
    f.metadata(9);
    expect(calls).toBe(1);
    f.addon.dispose();
  });
  it('rejects fingerprint conflicts and requires an input action', async () => {
    const f = fixture();
    f.metadata(7, true, 'b'.repeat(64));
    f.state(7, 12);
    expect(f.connection.snapshot('State').available).toBe(false);
    f.addon.dispose();
    const g = fixture();
    g.metadata();
    await expect(
      g.connection.request('Update', { count: 1 }),
    ).rejects.toMatchObject({ outcome: 'denied' });
    g.addon.dispose();
  });
});
