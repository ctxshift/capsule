# Capsule React SDK

`@capsule/react` is the versioned, game-independent authoring package. It has no
Host theme, artwork or gameplay dependencies. This is a private workspace package;
it is not yet published to a registry.

- `@capsule/react`: declarative primitives, render and observation hooks.
- `@capsule/react/components`: theme-driven composed controls.
- `@capsule/react/window-state`: bounded window placement/state helpers.
- `@capsule/react/scroll-list`: controlled fixed-row scrolling and keyboard offset helpers.

`ScrollList` takes row IDs, a row renderer, viewport/row dimensions, an offset and
callbacks. It mounts only visible rows, preserves keyed rows while they remain
visible, and accepts scrollbar artwork from its caller. Keep each list's offset
and focused-list identity in screen state; route root key events through
`scrollKeyOffset` for the focused list. Wheel input does not change focus.

`onWheel` receives `{ deltaX, deltaY, modifiers }` in design units, positive
right/down. Native wheel steps map to 64 design units; the browser normalizes
pixel, line and page deltas. Events target the nearest subscribed ancestor and
do not move keyboard focus. Hosts bound each axis to ±8192 units. Compose scroll
state and clipped content in the addon; wheel input itself grants no game action.

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
