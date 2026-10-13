# Capsule React SDK

`@capsule/react` is the versioned, game-independent authoring package. It has no
Host theme, artwork or gameplay dependencies. This is a private workspace package;
it is not yet published to a registry.

- `@capsule/react`: declarative primitives, render and observation hooks.
- `@capsule/react/components`: theme-driven composed controls.
- `@capsule/react/window-state`: bounded window placement/state helpers.

Exports include TypeScript source as the authoritative development types. Run
`bun run declarations` here to emit declaration files from the implementation;
generated `dist/` output is ignored. The current addon compiler bundles the source
and binds React to the host-provided compatible runtime. Importing this package
outside a host does not create a renderer or grant native capabilities.

Install the repository workspace with `bun install --frozen-lockfile` at its root.
Run `bun run typecheck` here or `mise run check-ui` at the root. Addons depend on
this package normally; they do not include ambient declarations from tool paths.
Legacy `@portable-ui/react` declarations and source re-exports remain temporary
compatibility adapters for existing diagnostics.

Storage, service discovery and public asset exports are separate planned slices;
they are not advertised here as implemented APIs.
