# Develop React UI in a running game

The primary Capsule workflow is to start the game and a UI development server,
then edit React/TSX, theme and addon assets by hand or with an agent. Successful
changes must appear in the running game without rebuilding or restarting it.
The UI source can live in its own codebase. Unity is the first native adapter;
other engines, including Godot, should consume the same authoring/compiler
contract through their own adapters. A Godot adapter is not implemented here.

## Development loop

1. Configure the game's development host to connect to an explicit local workshop
   endpoint and admit the intended addon and feature providers. Start the game
   once, in the Editor or as a development executable.
2. Start Capsule's workshop against the external UI project's entry, manifest,
   theme, assets and compatible SDK. Watch linked authoring dependencies too.
3. Edit the UI. The server compiles and validates a candidate, publishes it
   atomically and exposes its revision and diagnostics.
4. The running host detects a new publication, admits a replacement generation
   and presents it. Gameplay and its authoritative services continue running.
5. If compilation, theme validation or runtime admission fails, keep the last
   working UI active and expose the failure to the developer. Fixing the edit
   resumes the same loop without restarting either process.

The integration owns the endpoint connection and polling/notification mechanism;
the portable runtime owns safe generation activation and retirement. Release
builds consume pinned packages; the development connection is explicit and
optional. Neither an external checkout path nor a server address is hardcoded.

## Current authoring entry point

From the Capsule repository root:

```powershell
bun install --frozen-lockfile
bun run build:sdk
bun run dev:demo
```

The neutral Aster example supplies a React UI, browser preview and workshop
configuration. `startWorkshop` from `@capsule/workshop` takes explicit source,
output, addon/browser roots, theme and SDK paths; `watchRoots` adds linked source
dependencies. The browser preview is useful for authoring, but does not prove
that an engine host consumes a changed publication.

## Required end-to-end demonstration

Automate a neutral engine fixture, retaining process identities and publication
revisions as evidence:

- Start the game and workshop once, with the UI outside the game checkout.
- Change visible TSX text and layout; assert the new revision is rendered in the
  same running game process. Change a theme value and an admitted image too.
- Exercise a control after replacement and verify it still reaches the game's
  provider. Verify observations continue reaching the new UI.
- Submit invalid TSX and an invalid theme. Assert diagnostics and the unchanged
  last-good visible UI, then repair and assert recovery in the same game process.
- Disconnect/reconnect the workshop and check bounded recovery and clean
  retirement of focus, pointer capture, subscriptions and image leases.

Record reload latency and define reload state behavior explicitly. Ordinary
React re-renders and replacement of a guest generation are different operations;
do not promise React Fast Refresh or preservation of component-local state across
replacements without implementing and testing that policy.

Compiler tests, browser reload tests and standalone native lifecycle tests cover
parts of this chain. They must not be reported as the complete in-game live reload
demonstration. The extracted neutral Unity consumer still needs the development
host connection and an automated rendered demonstration.

## Reload boundaries

TSX, addon theme and addon asset edits belong to the live loop. Changes to native
libraries, carrier, embedded SDK, C# or engine packages can require recompilation,
deployment or restart. Local Capsule source attach/detach supports developing
those libraries; it does not substitute for live authoring of the game's UI.
Keep compatibility checks at both boundaries.
