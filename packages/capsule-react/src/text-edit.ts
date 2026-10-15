// Native editors own drafts and selection. These records report semantic changes;
// they are not instructions to rebuild text from keyboard events.
export const textEditPhases = [
  'change',
  'composition',
  'submit',
  'cancel',
  'focus',
  'blur',
] as const;
export interface TextEdit {
  phase: (typeof textEditPhases)[number];
  text: string;
  composition: string;
  anchor: number;
  focus: number;
  composing: boolean;
  userInput: boolean;
}
export const maxTextBytes = 512;
export const maxTextEditBytes = 12 + 2 * maxTextBytes;

// QuickJS has no browser TextEncoder/TextDecoder. URI conversion is strict about
// malformed UTF-8 and unpaired UTF-16 surrogates on both runtimes.
function utf8(text: string): number[] {
  if (text.includes('\0')) throw Error('NUL text');
  const bytes = Array.from(unescape(encodeURIComponent(text)), (c) =>
    c.charCodeAt(0),
  );
  if (bytes.length > maxTextBytes) throw Error('Text byte quota');
  return bytes;
}
function boundary(text: string, at: number): boolean {
  return (
    Number.isInteger(at) &&
    at >= 0 &&
    at <= text.length &&
    !(
      at > 0 &&
      at < text.length &&
      /[\uD800-\uDBFF]/.test(text[at - 1]) &&
      /[\uDC00-\uDFFF]/.test(text[at])
    )
  );
}
function validate(value: TextEdit) {
  if (
    !textEditPhases.includes(value.phase) ||
    !boundary(value.text, value.anchor) ||
    !boundary(value.text, value.focus) ||
    (!value.composing && value.composition !== '') ||
    (value.composing && ['submit', 'cancel', 'blur'].includes(value.phase))
  )
    throw Error('Invalid text edit');
}
export function encodeTextEdit(value: TextEdit): Uint8Array {
  validate(value);
  const text = utf8(value.text),
    composition = utf8(value.composition);
  const bytes = new Uint8Array(12 + text.length + composition.length);
  const view = new DataView(bytes.buffer);
  bytes[0] = 1;
  bytes[1] = textEditPhases.indexOf(value.phase) + 1;
  bytes[2] = (value.composing ? 1 : 0) | (value.userInput ? 2 : 0);
  view.setUint16(4, value.anchor, true);
  view.setUint16(6, value.focus, true);
  view.setUint16(8, text.length, true);
  view.setUint16(10, composition.length, true);
  bytes.set(text, 12);
  bytes.set(composition, 12 + text.length);
  return bytes;
}
export function decodeTextEdit(bytes: Uint8Array): TextEdit {
  if (
    bytes.length < 12 ||
    bytes.length > maxTextEditBytes ||
    bytes[0] !== 1 ||
    bytes[1] < 1 ||
    bytes[1] > 6 ||
    bytes[2] > 3 ||
    bytes[3]
  )
    throw Error('Invalid text edit');
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const textLength = view.getUint16(8, true),
    compositionLength = view.getUint16(10, true);
  if (
    textLength > maxTextBytes ||
    compositionLength > maxTextBytes ||
    bytes.length !== 12 + textLength + compositionLength
  )
    throw Error('Text byte quota');
  const decode = (start: number, length: number) =>
    decodeURIComponent(
      Array.from(
        bytes.subarray(start, start + length),
        (b) => '%' + b.toString(16).padStart(2, '0'),
      ).join(''),
    );
  const value: TextEdit = {
    phase: textEditPhases[bytes[1] - 1],
    text: decode(12, textLength),
    composition: decode(12 + textLength, compositionLength),
    anchor: view.getUint16(4, true),
    focus: view.getUint16(6, true),
    composing: !!(bytes[2] & 1),
    userInput: !!(bytes[2] & 2),
  };
  if (value.text.includes('\0') || value.composition.includes('\0'))
    throw Error('NUL text');
  validate(value);
  return value;
}
