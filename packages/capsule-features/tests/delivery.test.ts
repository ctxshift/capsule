import { expect, it } from 'vitest';
import { defineFeature, s, type Feature } from '../src/schema';
import { descriptor, generateCSharp } from '../src/codegen';

const contract = (delivery: 'latest' | 'ordered') =>
  defineFeature({
    name: '@example/sequence',
    version: '1.0.0',
    dependencies: {},
    states: {
      Values: {
        schema: s.record('Value', { value: s.u32 }),
        permission: 'read',
        delivery,
      },
    },
    requests: {},
  });

it('makes ordered delivery part of identity and generated native binding', () => {
  const ordered = contract('ordered');
  expect(descriptor(ordered).fingerprint).not.toBe(
    descriptor(contract('latest')).fingerprint,
  );
  expect(generateCSharp('Example', { Sequence: ordered })).toContain(
    'PortableUi.UiFeatureDelivery.Ordered',
  );
  expect(
    generateCSharp('Example', { Sequence: contract('latest') }),
  ).not.toContain('PortableUi.UiFeatureDelivery.Ordered');
});

it('rejects unsupported delivery before generation', () => {
  const invalid = {
    ...contract('latest'),
    states: {
      Values: { ...contract('latest').states.Values, delivery: 'lossy' },
    },
  };
  expect(() => defineFeature(invalid as unknown as Feature)).toThrow(
    'Invalid state delivery',
  );
});
