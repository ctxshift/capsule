import { expect, test } from 'vitest';
import { readFileSync } from 'node:fs';
import {
  encodeTextEdit,
  decodeTextEdit,
  type TextEdit,
} from '@capsule-sdk/react/text-edit';
const vectors = JSON.parse(
  readFileSync(
    new URL(
      '../../../packages/capsule-react/text-edit-vectors.json',
      import.meta.url,
    ),
    'utf8',
  ),
).vectors as (TextEdit & { hex: string })[];
test('native text records match shared Unicode and selection vectors', () => {
  for (const { hex, ...value } of vectors) {
    expect(Buffer.from(encodeTextEdit(value)).toString('hex')).toBe(hex);
    expect(decodeTextEdit(Buffer.from(hex, 'hex'))).toEqual(value);
  }
});
test('text decoding rejects malformed UTF-8, flags, lengths and split surrogates', () => {
  const bytes = encodeTextEdit(vectors[1]);
  for (const [at, value] of [
    [0, 2],
    [1, 7],
    [2, 4],
    [3, 1],
    [4, 2],
    [8, 255],
    [13, 255],
  ]) {
    const bad = bytes.slice();
    bad[at] = value;
    expect(() => decodeTextEdit(bad)).toThrow();
  }
  expect(() => decodeTextEdit(bytes.slice(0, -1))).toThrow();
  expect(() => encodeTextEdit({ ...vectors[0], text: '\ud800' })).toThrow();
  expect(() => encodeTextEdit({ ...vectors[0], text: '\0' })).toThrow();
});
test('byte limits include Unicode; provisional composition cannot submit or cancel', () => {
  const value: TextEdit = {
    ...decodeTextEdit(encodeTextEdit(vectors[0])),
    text: '😀'.repeat(128),
    anchor: 256,
    focus: 256,
  };
  expect(decodeTextEdit(encodeTextEdit(value))).toEqual(value);
  expect(() => encodeTextEdit({ ...value, text: value.text + 'x' })).toThrow();
  for (const phase of ['submit', 'cancel', 'blur'] as const)
    expect(() => encodeTextEdit({ ...vectors[1], phase })).toThrow();
});
