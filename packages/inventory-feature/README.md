# Example inventory feature

Install/import the npm package as `@capsule-sdk/example-inventory`. The runtime
feature names below are protocol identities, independent of the npm namespace.

This example defines two versioned contracts:
`@capsule-example/items@1.0.0` describes items, and
`@capsule-example/inventory@1.2.5` publishes inventory state and accepts
revision-checked transfer requests. It is an example contract family, not a
required Capsule runtime feature.

`src/contracts.ts` is the source of truth. The generator writes local
`generated/contracts.json` and
`generated/InventoryFeature.Generated.cs`. The C# provider interfaces,
bindings and codecs have no engine dependency. They derive state and request
permissions from the same definitions used by the TypeScript codec.

From the repository root:

```powershell
bun packages/inventory-feature/scripts/generate.ts --check
bun run --cwd packages/inventory-feature typecheck
bun run --cwd packages/inventory-feature test
```

The focused native test compiles a provider against the local generated C#
source and checks bidirectional bytes with TypeScript. It also checks
subscription fanout, request dispatch, disposal and truncated payload
rejection. The view and mock are reusable examples; a consuming game
supplies its own catalog, artwork, authority and presentation host.

Outcome codes are 0 succeeded, 1 rejected, 2 unavailable and 3 unknown.
Receipt alone is not success. Item IDs and container names are opaque to
Capsule. The existing 256-byte native request bound leaves 244 bytes after
feature framing; larger requests need an explicit transport extension.
