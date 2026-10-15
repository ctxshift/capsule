import { createHash } from 'node:crypto';
import { identifier, type Feature, type Schema } from './schema';

export function descriptor(feature: Feature) {
  // Field order is wire order. Other object insertion order is normalized.
  function normalize(value: unknown): unknown {
    if (!value || typeof value !== 'object') return value;
    if (Array.isArray(value)) return value.map(normalize);
    const object = value as Record<string, unknown>;
    if (object.kind === 'record')
      return {
        kind: 'record',
        name: object.name,
        fields: Object.entries(object.fields as object).map(
          ([name, schema]) => ({ name, schema: normalize(schema) }),
        ),
      };
    return Object.fromEntries(
      Object.keys(object)
        .sort()
        .map((key) => [key, normalize(object[key])]),
    );
  }
  const schema = normalize(feature);
  return {
    ...feature,
    fingerprint: createHash('sha256')
      .update(JSON.stringify(schema))
      .digest('hex'),
  };
}

export function generateCSharp(
  namespace: string,
  contracts: Record<string, Feature>,
): string {
  namespace.split('.').forEach(identifier);
  const records = new Map<string, Extract<Schema, { kind: 'record' }>>();
  function visit(schema: Schema): void {
    if (schema.kind === 'record') {
      const previous = records.get(schema.name);
      if (previous && JSON.stringify(previous) !== JSON.stringify(schema))
        throw Error(`Conflicting record ${schema.name}`);
      records.set(schema.name, schema);
      Object.values(schema.fields).forEach(visit);
    } else if (schema.kind === 'list') visit(schema.element);
    else if (!['bool', 'u32', 'text'].includes(schema.kind))
      throw Error('Unsupported schema');
  }
  for (const [name, feature] of Object.entries(contracts)) {
    identifier(name);
    Object.values(feature.states).forEach((state) => visit(state.schema));
    Object.values(feature.requests).forEach((request) => {
      visit(request.input);
      visit(request.output);
    });
  }
  const type = (schema: Schema): string =>
    schema.kind === 'bool'
      ? 'bool'
      : schema.kind === 'u32'
        ? 'uint'
        : schema.kind === 'text'
          ? 'string'
          : schema.kind === 'list'
            ? `IReadOnlyList<${type(schema.element)}>`
            : `T${schema.name}`;
  const read = (schema: Schema): string =>
    schema.kind === 'bool'
      ? 'r.Bool()'
      : schema.kind === 'u32'
        ? 'r.UInt()'
        : schema.kind === 'text'
          ? `r.Text(${schema.maxBytes})`
          : schema.kind === 'list'
            ? `r.List(${schema.maxCount}, () => ${read(schema.element)})`
            : `Read${schema.name}(r)`;
  const measure = (schema: Schema, value: string, depth = 0): string =>
    schema.kind === 'bool'
      ? '1'
      : schema.kind === 'u32'
        ? '4'
        : schema.kind === 'text'
          ? `Wire.TextSize(${value}, ${schema.maxBytes})`
          : schema.kind === 'list'
            ? `Wire.ListSize(${value}, ${schema.maxCount}, item${depth} => ${measure(schema.element, `item${depth}`, depth + 1)})`
            : `Size${schema.name}(${value})`;
  const write = (schema: Schema, value: string, depth = 0): string =>
    schema.kind === 'bool'
      ? `w.Bool(${value});`
      : schema.kind === 'u32'
        ? `w.UInt(${value});`
        : schema.kind === 'text'
          ? `w.Text(${value});`
          : schema.kind === 'list'
            ? `w.UInt((uint)${value}.Count); foreach (var item${depth} in ${value}) { ${write(schema.element, `item${depth}`, depth + 1)} }`
            : `Write${schema.name}(w, ${value});`;
  const classes = [...records.values()]
    .map((record) => {
      const fields = Object.entries(record.fields);
      return `public sealed class T${record.name} {
${fields.map(([name, schema]) => `public ${type(schema)} @${name} { get; }`).join('\n')}
public T${record.name}(${fields.map(([name, schema]) => `${type(schema)} @${name}`).join(', ')}) {
${fields.map(([name]) => `this.@${name} = @${name};`).join('\n')}
}
}`;
    })
    .join('\n');
  const providers = Object.entries(contracts)
    .map(
      ([name, feature]) => `public interface I${name}Provider {
${Object.entries(feature.states)
  .map(
    ([key, state]) =>
      `${type(state.schema)} Get${key}();\nevent Action<${type(state.schema)}> ${key}Changed;`,
  )
  .join('\n')}
${Object.entries(feature.requests)
  .map(
    ([key, request]) =>
      `Task<${type(request.output)}> ${key}(${type(request.input)} request, CancellationToken cancellationToken);`,
  )
  .join('\n')}
}
public static class ${name}Contract {
public const string Name = ${JSON.stringify(feature.name)}, Version = ${JSON.stringify(feature.version)}, Fingerprint = ${JSON.stringify(descriptor(feature).fingerprint)};
}`,
    )
    .join('\n');
  const codecs = [...records.values()]
    .map((record) => {
      const fields = Object.entries(record.fields);
      return `public static byte[] Encode(T${record.name} value) { var w = new Wire(Size${record.name}(value)); Write${record.name}(w, value); return w.Bytes; }
public static T${record.name} Decode${record.name}(byte[] bytes) { var r = new Wire(bytes); var value = Read${record.name}(r); r.End(); return value; }
static int Size${record.name}(T${record.name} value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(${fields.map(([name, schema]) => measure(schema, `value.@${name}`)).join(' + ')}); }
static void Write${record.name}(Wire w, T${record.name} value) { ${fields.map(([name, schema]) => write(schema, `value.@${name}`)).join('\n')} }
static T${record.name} Read${record.name}(Wire r) => new T${record.name}(${fields.map(([, schema]) => read(schema)).join(', ')});`;
    })
    .join('\n');
  return `// Generated from TypeScript feature definitions. Do not edit.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace ${namespace} {
${classes}
${providers}
public static class FeatureCodec {
${codecs}
}
// Borrow input only during synchronous decode. Encode allocates one exact-size buffer.
internal sealed class Wire {
static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
public readonly byte[] Bytes;
int offset;
public Wire(int length) { Bytes = new byte[length]; }
public Wire(byte[] bytes) { Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes)); }
void Need(int length) { if (length < 0 || length > Bytes.Length - offset) throw new FormatException("Truncated feature payload"); }
public void End() { if (offset != Bytes.Length) throw new FormatException("Trailing feature payload"); }
public void UInt(uint value) { Need(4); for (int n = 0; n < 4; n++) Bytes[offset++] = (byte)(value >> (n * 8)); }
public uint UInt() { Need(4); uint value = 0; for (int n = 0; n < 4; n++) value |= (uint)Bytes[offset++] << (n * 8); return value; }
public void Bool(bool value) { Need(1); Bytes[offset++] = value ? (byte)1 : (byte)0; }
public bool Bool() { Need(1); byte value = Bytes[offset++]; if (value > 1) throw new FormatException("Invalid bool"); return value == 1; }
public static int TextSize(string value, int max) { if (value == null) throw new ArgumentNullException(nameof(value)); int count = Utf8.GetByteCount(value); if (count > max) throw new FormatException("Text bound exceeded"); return checked(4 + count); }
public static int ListSize<T>(IReadOnlyList<T> values, int max, Func<T, int> measure) { if (values == null || values.Count > max) throw new FormatException("List bound exceeded"); int size = 4; foreach (var value in values) size = checked(size + measure(value)); return size; }
public void Text(string value) { int size = Utf8.GetByteCount(value); UInt((uint)size); Need(size); offset += Utf8.GetBytes(value, 0, value.Length, Bytes, offset); }
public string Text(int max) { uint count = UInt(); if (count > max) throw new FormatException("Text bound exceeded"); int size = (int)count; Need(size); string value = Utf8.GetString(Bytes, offset, size); offset += size; return value; }
public IReadOnlyList<T> List<T>(int max, Func<T> read) { uint count = UInt(); if (count > max || count > Bytes.Length - offset) throw new FormatException("Invalid list count"); var result = new T[(int)count]; for (int n = 0; n < result.Length; n++) result[n] = read(); return Array.AsReadOnly(result); }
}
}
`;
}
