import { describe, expect, it } from 'vitest';
import { codec } from '../src/codec';
import { defineFeature, s } from '../src/schema';
import { descriptor, generateCSharp } from '../src/codegen';

describe('feature contract codecs', () => {
  const schema = s.record('Example', {
    number: s.u32,
    active: s.bool,
    name: s.text(32),
    values: s.list(s.u32, 4),
  });
  const wire = codec(schema);
  it('uses exact-size little endian payloads and supports non-ASCII text', () => {
    const value = {
      number: 0x12345678,
      active: true,
      name: 'Aé🗡',
      values: [0, 0xffffffff],
    };
    const bytes = wire.encode(value);
    expect([...bytes.subarray(0, 5)]).toEqual([0x78, 0x56, 0x34, 0x12, 1]);
    expect(bytes.length).toBe(28);
    const decoded = wire.decode(bytes);
    expect(decoded).toEqual(value);
    expect(Object.isFrozen(decoded.values)).toBe(true);
    expect(() => wire.encode({ ...value, number: -1 })).toThrow();
    expect(() => wire.encode({ ...value, name: '\ud800' })).toThrow();
  });
  it('rejects truncation, trailing bytes, invalid bool, UTF-8 and oversized counts', () => {
    const bytes = wire.encode({
      number: 1,
      active: false,
      name: 'a',
      values: [],
    });
    for (let n = 0; n < bytes.length; n++)
      expect(() => wire.decode(bytes.subarray(0, n))).toThrow();
    expect(() => wire.decode(new Uint8Array([...bytes, 0]))).toThrow();
    const badBool = bytes.slice();
    badBool[4] = 2;
    expect(() => wire.decode(badBool)).toThrow();
    const badUtf8 = bytes.slice();
    badUtf8[9] = 0xff;
    expect(() => wire.decode(badUtf8)).toThrow();
    expect(() =>
      codec(s.list(s.u32, 1)).decode(new Uint8Array([255, 255, 255, 255])),
    ).toThrow();
  });
  it('fingerprints field order and generates independent native provider interfaces', () => {
    const feature = defineFeature({
      name: '@example/sample',
      version: '1.0.0',
      dependencies: {},
      states: { State: { schema, permission: 'read', delivery: 'latest' } },
      requests: {},
    });
    expect(descriptor(feature).fingerprint).toHaveLength(64);
    const reordered = {
      ...feature,
      states: {
        State: {
          ...feature.states.State,
          schema: s.record('Example', {
            active: s.bool,
            number: s.u32,
            name: s.text(32),
            values: s.list(s.u32, 4),
          }),
        },
      },
    };
    expect(descriptor(reordered).fingerprint).not.toBe(
      descriptor(feature).fingerprint,
    );
    const code = generateCSharp('Example.Contracts', { Example: feature });
    expect(code).toContain('public interface IExampleProvider');
    expect(code).not.toContain('UnityEngine');
    expect(() =>
      generateCSharp('Bad;Namespace', { Example: feature }),
    ).toThrow();
  });
});
