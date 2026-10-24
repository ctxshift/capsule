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
function fixture(ordered = false) {
  const callbacks = new Map<number, (bytes: Uint8Array) => void>();
  const recoveries: { capability: number; payload: Uint8Array }[] = [];
  const addon = createAddonFeatures(
    (topic, _schema, receive) => {
      callbacks.set(topic, receive);
      return () => {
        callbacks.delete(topic);
      };
    },
    (capability, payload) => {
      recoveries.push({ capability, payload });
    },
  );
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
                  deliveries: { State: ordered ? 'ordered' : 'latest' },
                  requests: { Update: 1 },
                },
              ]
            : [],
        }),
      ),
    );
  }
  function state(epoch: number, count: number, revision = 1, reset = false) {
    const offset = ordered ? 16 : 8;
    const bytes = new Uint8Array(offset + 4),
      view = new DataView(bytes.buffer);
    view.setUint32(0, epoch, true);
    view.setUint32(4, 0x10001, true);
    if (ordered) {
      view.setUint32(8, revision, true);
      view.setUint32(12, reset ? 1 : 0, true);
    }
    bytes.set(codec(value).encode({ count }), offset);
    send(featureChannels.state, bytes);
  }
  return { addon, connection, callbacks, send, metadata, state, recoveries };
}
describe('generic addon feature transport', () => {
  it('invalidates a sequence gap and requests one read-only snapshot, then resumes on reset', () => {
    const f = fixture(true);
    f.metadata();
    f.state(7, 10, 1, true);
    expect(f.connection.snapshot('State').value).toEqual({ count: 10 });
    f.state(7, 30, 3);
    f.state(7, 40, 4);
    expect(f.connection.snapshot('State').available).toBe(false);
    expect(f.recoveries).toHaveLength(1);
    expect(f.recoveries[0].capability).toBe(featureChannels.recovery);
    const request = new DataView(f.recoveries[0].payload.buffer);
    expect(request.byteLength).toBe(8);
    expect(request.getUint32(0, true)).toBe(7);
    expect(request.getUint32(4, true)).toBe(0x10001);
    f.state(7, 50, 5, true);
    f.state(7, 60, 6);
    expect(f.connection.snapshot('State').value).toEqual({ count: 60 });
    f.state(7, 20, 2, true);
    expect(f.connection.snapshot('State').value).toEqual({ count: 60 });
    const late = f.addon.connect(feature, '1.0.*', fingerprint);
    expect(late.snapshot('State').value).toEqual({ count: 60 });
    f.metadata(8);
    f.state(7, 99, 7, true);
    expect(late.snapshot('State').available).toBe(false);
    f.state(8, 70, 1, true);
    expect(late.snapshot('State').value).toEqual({ count: 70 });
    f.addon.dispose();
  });
  it('allows explicit granted snapshot reads without gameplay input and rejects absent providers', async () => {
    const f = fixture();
    f.metadata();
    f.connection.refreshSnapshot('State');
    expect(f.recoveries).toHaveLength(1);
    await expect(
      f.connection.request('Update', { count: 1 }),
    ).rejects.toMatchObject({ outcome: 'denied' });
    f.metadata(8, false);
    expect(() => f.connection.refreshSnapshot('State')).toThrow(
      'Provider unavailable',
    );
    f.addon.dispose();
    expect(() => f.connection.refreshSnapshot('State')).toThrow(
      'Connection disposed',
    );
  });
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
