# Capsule feature contracts

Engine-independent TypeScript schema definitions and deterministic native codegen.
This package imports no Host code, network transport or engine APIs.

`defineFeature` describes versioned dependencies, latest-state observations and
request/result operations with separate permissions. `s` defines explicit wire
types; `Value<typeof schema>` supplies readonly TypeScript types. `codec(schema)`
provides the compatible JavaScript codec, including QuickJS without TextEncoder.
`generateCSharp` emits strongly typed provider interfaces, records and codecs.

Strings use strict UTF-8 prefixed by a uint32 byte count; arrays use a uint32 element
count; integers are little endian. Field order is definition order. Booleans are one
byte, 0 or 1. Invalid UTF-8, bounds, truncation and trailing bytes are rejected.
The descriptor fingerprints canonical schema JSON with SHA-256. This identifies a
definition, not its publisher's authenticity or behavioral conformance.

Encoding validates/measures then allocates one exact-size buffer. Native text writes
directly into it; decode borrows its input synchronously and constructs typed values.
JavaScript decoded values are deeply frozen; native decoded lists are read-only.
Native providers must retain immutable ownership of lists supplied to DTO constructors.
No public native/guest shared memory or cross-addon sharing is introduced.

The initial subset supports records, booleans, uint32, bounded text and bounded lists.
Transient-event generation and non-C# engine targets are not implemented. Host
registration, grant enforcement and lifecycle are separate from code generation.

`./runtime` supplies an explicit host registry and stable typed connections for mock
or bridge transports. Initially version ranges are exact versions or `major.minor.*`;
unsupported range syntax fails explicitly. Dependencies must resolve before connect.
The host retains the registry; addons receive grant-scoped connections. Native
bindings must independently enforce grants and authority. The JS registry is not a
sandbox security boundary.

Each connection decodes a state publication once and reuses its immutable snapshot
for subscribers. Provider or dependency loss invalidates availability. Replacement
reattaches the same connection; old callbacks are ignored and in-flight requests
reject with an unknown outcome without automatic retry. Dispose connections at addon
teardown. State delivery is implemented; transient event streams remain future work.

Run `bun run --cwd packages/capsule-features typecheck` and
`bun run --cwd packages/capsule-features test` from the repository root.
