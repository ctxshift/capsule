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

Run `bun run --cwd packages/capsule-features typecheck` and
`bun run --cwd packages/capsule-features test` from the repository root.
