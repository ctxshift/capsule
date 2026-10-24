import { freeze, type Schema, type Value } from './schema';

// No TextEncoder dependency: the same codec runs in browsers and the QuickJS guest.
function textBytes(text: string, write?: (byte: number) => void): number {
  let length = 0;
  const emit = (byte: number) => {
    length++;
    write?.(byte);
  };
  for (let index = 0; index < text.length; index++) {
    let point = text.charCodeAt(index);
    if (point >= 0xd800 && point <= 0xdbff) {
      const low = text.charCodeAt(++index);
      if (!(low >= 0xdc00 && low <= 0xdfff)) throw Error('Invalid UTF-16');
      point = 0x10000 + ((point - 0xd800) << 10) + low - 0xdc00;
    } else if (point >= 0xdc00 && point <= 0xdfff)
      throw Error('Invalid UTF-16');
    if (point < 0x80) emit(point);
    else if (point < 0x800) {
      emit(0xc0 | (point >> 6));
      emit(0x80 | (point & 63));
    } else if (point < 0x10000) {
      emit(0xe0 | (point >> 12));
      emit(0x80 | ((point >> 6) & 63));
      emit(0x80 | (point & 63));
    } else {
      emit(0xf0 | (point >> 18));
      emit(0x80 | ((point >> 12) & 63));
      emit(0x80 | ((point >> 6) & 63));
      emit(0x80 | (point & 63));
    }
  }
  return length;
}
function size(schema: Schema, value: unknown): number {
  switch (schema.kind) {
    case 'bool':
      if (typeof value !== 'boolean') throw Error('Expected bool');
      return 1;
    case 'u32':
      if (
        typeof value !== 'number' ||
        !Number.isInteger(value) ||
        value < 0 ||
        value > 0xffffffff
      )
        throw Error('Expected uint32');
      return 4;
    case 'text': {
      if (typeof value !== 'string') throw Error('Expected string');
      const length = textBytes(value);
      if (length > schema.maxBytes) throw Error('Text bound exceeded');
      return 4 + length;
    }
    case 'list':
      if (!Array.isArray(value) || value.length > schema.maxCount)
        throw Error('List bound exceeded');
      return (
        4 + value.reduce((total, item) => total + size(schema.element, item), 0)
      );
    case 'record':
      if (!value || typeof value !== 'object' || Array.isArray(value))
        throw Error('Expected record');
      return Object.entries(schema.fields).reduce(
        (total, [key, child]) =>
          total + size(child, (value as Record<string, unknown>)[key]),
        0,
      );
  }
}
export function codec<S extends Schema>(schema: S) {
  return {
    encode(value: Value<S>): Uint8Array {
      const bytes = new Uint8Array(size(schema, value));
      const view = new DataView(bytes.buffer);
      let offset = 0;
      const uint = (n: number) => {
        view.setUint32(offset, n, true);
        offset += 4;
      };
      function write(type: Schema, data: unknown): void {
        switch (type.kind) {
          case 'bool':
            bytes[offset++] = data ? 1 : 0;
            break;
          case 'u32':
            uint(data as number);
            break;
          case 'text':
            uint(textBytes(data as string));
            textBytes(data as string, (byte) => {
              bytes[offset++] = byte;
            });
            break;
          case 'list':
            uint((data as unknown[]).length);
            for (const item of data as unknown[]) write(type.element, item);
            break;
          case 'record':
            for (const [key, child] of Object.entries(type.fields))
              write(child, (data as Record<string, unknown>)[key]);
            break;
        }
      }
      write(schema, value);
      return bytes;
    },
    decode(bytes: Uint8Array): Value<S> {
      const view = new DataView(
        bytes.buffer,
        bytes.byteOffset,
        bytes.byteLength,
      );
      let offset = 0;
      const need = (count: number) => {
        if (count > bytes.length - offset)
          throw Error('Truncated feature payload');
      };
      const byte = () => {
        need(1);
        return bytes[offset++];
      };
      const uint = () => {
        need(4);
        const n = view.getUint32(offset, true);
        offset += 4;
        return n;
      };
      function read(type: Schema): unknown {
        switch (type.kind) {
          case 'bool': {
            const b = byte();
            if (b > 1) throw Error('Invalid bool');
            return b === 1;
          }
          case 'u32':
            return uint();
          case 'text': {
            const length = uint();
            if (length > type.maxBytes) throw Error('Text bound exceeded');
            need(length);
            const end = offset + length;
            let result = '';
            while (offset < end) {
              const first = byte();
              let point = first;
              const count =
                first < 0x80
                  ? 0
                  : first >= 0xc2 && first <= 0xdf
                    ? 1
                    : first >= 0xe0 && first <= 0xef
                      ? 2
                      : first >= 0xf0 && first <= 0xf4
                        ? 3
                        : -1;
              if (count < 0 || offset + count > end)
                throw Error('Invalid UTF-8');
              if (count) point &= (1 << (6 - count)) - 1;
              for (let n = 0; n < count; n++) {
                const next = byte();
                if ((next & 0xc0) !== 0x80) throw Error('Invalid UTF-8');
                point = (point << 6) | (next & 63);
              }
              if (
                (count === 1 && point < 0x80) ||
                (count === 2 && point < 0x800) ||
                (count === 3 && point < 0x10000) ||
                point > 0x10ffff ||
                (point >= 0xd800 && point <= 0xdfff)
              )
                throw Error('Invalid UTF-8');
              result += String.fromCodePoint(point);
            }
            return result;
          }
          case 'list': {
            const count = uint();
            if (count > type.maxCount || count > bytes.length - offset)
              throw Error('Invalid list count');
            const values: unknown[] = [];
            for (let n = 0; n < count; n++) values.push(read(type.element));
            return values;
          }
          case 'record':
            return Object.fromEntries(
              Object.entries(type.fields).map(([key, child]) => [
                key,
                read(child),
              ]),
            );
        }
      }
      const value = read(schema);
      if (offset !== bytes.length) throw Error('Trailing feature payload');
      return freeze(value) as Value<S>;
    },
  };
}
