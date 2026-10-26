# Develop React UI in a running game

The primary Capsule workflow is to start the game and a UI development server,
then edit React/TSX, theme and addon assets by hand or with an agent. Successful
changes must appear in the running game without rebuilding or restarting it.
The UI source can live in its own codebase. Unity is the first native adapter;
other engines, including Godot, should consume the same authoring/compiler
contract through their own adapters. The initial standard Godot/GDScript
development adapter uses that same protocol; see [Godot setup](godot-setup.md)
for its tested platform, current controls and remaining production gates.

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

The workshop also exports `@capsule-sdk/workshop/project`: `initProject`,
`inspectProject` and `startProject`. Its `capsule-workshop` command supports
`init`, `inspect` and `dev` with `--project <external-ui-folder>`. Initialization
creates a neutral React starter only in a new or empty directory; repeating it
on an existing Capsule project preserves source edits. Existing projects can be
connected through `capsule.project.json` without regenerating their source.

The private project's package scripts run the same compiler/workshop path as
other addons. The engine endpoint is `/capsule/dev/v1/package`, with a versioned
envelope, SDK hash, accepted publication hash, theme, metrics and compiler
diagnostics. `/capsule/dev/v1/status` reports readiness without local source paths.
Compilation or theme errors preserve the last-good publication and image set.
The server binds loopback; release players do not use this development endpoint.

Before the npm packages are published, run the source entry from a configured
Capsule checkout:

```powershell
bun packages/capsule-workshop/src/project-cli.ts init --project <external-ui-folder> --packages <local-release-directory>
bun install --cwd <external-ui-folder>
bun packages/capsule-workshop/src/project-cli.ts dev --project <external-ui-folder>
```

`--packages` is an explicit local candidate option: it verifies the eight npm
tarball hashes and copies them into ignored `.capsule/packages`, with relative
development overrides. Normal published initialization uses exact npm versions.
The development server builds the matching SDK from the UI project's declared
tooling and keeps its generated publication outside the source root. `--watch-stdin`
allows the Unity plugin to shut down only the server process it owns by closing
its input stream. CLI scaffolding beyond this starter remains separate work.

From the Capsule repository root:

```powershell
bun install --frozen-lockfile
bun run build:sdk
bun run dev:demo
```

The neutral Aster example supplies a React UI, browser preview and workshop
configuration. `startWorkshop` from `@capsule-sdk/workshop` takes explicit source,
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
demonstration. The Unity plugin now supplies the opt-in development connection
and a rendered native demonstration; see [Unity setup](unity-setup.md#connect-a-react-ui-project)
and [retained evidence](verification/unity-development.json).

## Reload boundaries

TSX, addon theme and addon asset edits belong to the live loop. Changes to native
libraries, carrier, embedded SDK, C# or engine packages can require recompilation,
deployment or restart. Local Capsule source attach/detach supports developing
those libraries; it does not substitute for live authoring of the game's UI.
Keep compatibility checks at both boundaries.

## Complete presentation and latency

Target about **300 ms to a complete, usable UI**, with a separately measured
**under 300 ms from a compiled publication to its replacement frame**. Prepare
the complete surface instead of exposing elements as they finish loading. The host prepares a replacement's theme, images and full
snapshot before activation; the previous generation remains visible and usable
until promotion. The rendered fixture checks text, panel, image and button in
the first replacement frame.

Measure separate boundaries: component enable to first complete snapshot,
replacement preparation after publication has arrived, and source save through
compilation to rendered replacement. The retained Windows Editor sample measured
about **284 ms** for first activation, **31 ms** for warm preparation and **1.67 s**
for TSX save through captured replacement. The last measurement includes compiler,
watcher, transport, rendering and screenshot capture; it is not a native rendering
measurement. These historical single samples are not percentile guarantees and
do not establish the current budgets. Report repeated startup and reload
distributions separately from engine/plugin initialization and full process
launch. Compiler turnaround is a separate part of the edit-to-visible workflow.

Enable **Run In Background** in Unity Player Settings to keep the game updating
while a code editor has focus. Input focus policy remains the game's choice;
the automated fixture uses isolated input settings for synthetic pointer events
and restores the original settings after the run.
