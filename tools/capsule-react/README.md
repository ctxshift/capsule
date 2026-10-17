# Portable React workshop

Author addons in TypeScript/TSX. From this directory:

```powershell
bun install --frozen-lockfile
bun run dev
```

Vite prints a loopback browser URL and the explicitly configured development
Player command. The default is the actual `addons/host-ui` System/Settings TSX,
generic shared components, exact artwork and the same theme as the native host.
Browser layout uses Yoga3.2.1 and native-compatible PUI1/v2 packets rather than
an independent CSS flex screen. Browser and game work independently or together.

Saving sources strictly typechecks, transforms TS/JSX internally, publishes
atomically to `.local/capsule-react/host-ui-workshop` and replaces the browser
generation after successful initialization. The configured native host observes
the same package and reloads successful changes without restarting. Compile/init
failures retain the previous UI; React local state resets on successful reload.
The command does not launch Unity or a Player. QuickJS executes generated JS;
no TypeScript compiler ships in game. Browser execution does not reproduce Wasm
isolation or fuel.

Generated bundles compact whitespace, syntax and identifiers while retaining
function names and source maps
with the original authored source. The 64 KiB bundle limit applies after this
compaction; oversized output is rejected without replacing the last good package.

The browser defaults to the 128-node community layout profile. Add `?coreLayout`
to its URL to preview Host's 256-node core screen compositions. This preview
flag grants no native capability: the native host selects the trusted core
profile after package admission.

From the repository root, the same command is:

```powershell
bun run --cwd tools/capsule-react dev
```

Override `--source`, `--output`, `--runtime-status`, or repeat `--watch-root` for
bounded shared roots. Vite also accepts `--port` and `--addon-root` for an external
source manifest. `dev:native` is the compiler-only watcher, supports `--assets`
and `--once`, and exits unsuccessfully on a rejected one-shot candidate. Invalid or obsolete candidates
retain the last good package. Watch traversal remains bounded to512 entries and
rejects symlinks; relative imports must remain within watched roots. The output
must be outside those roots. Assets remain within the entry's source root.

Existing watchers and outputs owned by an old entry path are not adopted. Stop
and restart your own watcher after this layout change, using a fresh output path;
do not delete its retained package. Source/output identity still prevents one
compiler from overwriting another's package.

## Layout and checks

- `src/`: generic native React host, scheduler and temporary compatibility exports;
  public components, window state and authoritative API types live in the
  `@capsule/react` workspace package.
- `browser/`: Host development shell, mock scenarios and browser/native status.
- `packages/capsule-workshop/`: portable compiler, browser renderer and explicit
  host-configured server. `examples/portable-demo-game/` proves a separate host.
- `addons/host-ui/`: canonical product TSX, theme, exact art and provenance;
  HUD/layout diagnostics remain under its `dev/` directory.
- `dev/addon-workshop.json`: trusted host registration and repository defaults.
- `tests/`: typed Vitest cases and small fixture helpers, with isolated VM and
  temporary package state. No custom test launcher.
- `scripts/`: SDK build, safe development compiler, phase profiling, actual Player
  proof and reference capture/comparison. `package-paths.mjs` contains only
  package-local paths; SDK builds do not read game configuration. `paths.mjs`
  supplies only Host adapter defaults; the portable workshop does not import it.
- `benchmarks/`: two retained experimental source specimens; see
  [audit and reproduction](docs/benchmarks.md).
- `docs/` and `licenses/`: methodology and unchanged upstream notices.

```powershell
bun run typecheck
bun run test
bun run check
```

The framework and addon TypeScript configurations are strict, with full library
checking and ES2020 libraries. Addons consume `@capsule/react` through package
exports, without browser/Node globals or tool-relative declarations. Install from
the repository workspace lockfile; the tool no longer has a separate lockfile.
`mise run check-ui` from the repository root checks SDK, framework, addon, example,
workshop and tool types plus lint/format, and fails on any error. Separate
strict tool and browser projects include Node or DOM declarations. Vitest4.0.18
replaces Bun tests; Bun remains the package manager and command runner. No browser
automation test suite is introduced. Tool types use Node25.0.3 declarations; these
are development types, not a Node runtime upgrade. TypeScript5.9.3,
React18.3.1 and esbuild0.25.12 pins remain unchanged.
JavaScript tooling is linted/formatted and imported by typed tests with its
inferred/JSDoc interface. Existing private ABI `any` exceptions remain confined
to the host/scheduler and historical adapters, not public components/examples.
Root ESLint/Prettier commands remain `bun run check:js` and `bun run format`.

## Builds and native proof

```powershell
bun run build
bun run build:yoga
bun run profile
bun run proof:windows ./.local/new-window-proof --capture
```

The package commands write ignored `.local/react-host.js` or
`.local/react-yoga-host.js` at the repository root. Direct `build.mjs` calls resolve
`--output` against the current working directory, defaulting to its
`.local/react-host.js`; importing `buildSdk` requires no game configuration.
The two names use the same current shared renderer; the runtime selects
Yoga through layout intent. The approved historical baseline SDK remains pinned,
as does the approved Yoga SDK; cleanup does not overwrite either Unity artifact.
An intentional SDK update requires explicit `--output <file>`. Strict checking
is separate from esbuild emission, which preserves the approved non-strict IIFE
bytes despite discovery of the new TypeScript project.

The Player proof uses the approved fixed executable path, actual EventSystem
input, normal1080p60FPS and cleanup/restoration. `--visual` selects4K captures.
Theme/provider integration requires actual Player validation. Source maps change paths naturally.
For an idle regression capture, run `bun scripts/ui-idle-player-proof.ts <built-package> <new-output-directory> [width height]`.
It uses the same approved windowed Player, warms up for five seconds, samples eight
FPS windows without screenshots, verifies idle React and the 60 FPS cap, and
checks automatic reload and teardown. Leave the window untouched during capture.
The report retains binary/package hashes; its FPS windows are not frame-time percentiles.
Workshop hot reload polls bounded file metadata and fully validates only changed
packages after settling. Set **Reload Poll Milliseconds** on the workshop component
in the Inspector, or use its `ReloadPollMilliseconds` property (100–500 ms,
default 200 ms). Grouped addons use their owner's interval. Metadata is a reload hint, never admission; explicit
Reload also handles edits that preserve file size and timestamps.
Reference capture and unscaled comparison remain separate because the browser and
native rendering surfaces have different dimensions. Browser native dropdown
popup capture remains unavailable; focused/closed evidence is labelled honestly.

[Profiling methodology](docs/PROFILING.md) separates fuel, instrumented overhead,
GC inference and native Yoga wall time. The active renderer is React18.3.1.
React is the only supported frontend. Retired alternatives remain documented
only in the historical spike ledger.

The host selects the installed default theme; manifests cannot globally restyle
others. Per-addon native scopes own immutable theme data, image leases and
materials. Bounded private palette/geometry overrides affect only that scope.
Trusted installed artwork uses generated Unity imports outside unchanged
community package quotas. PNG edits require import and a native rebuild; TSX and
theme tokens reload with the printed development configuration. Legacy HUD
templates and OS font resolution remain game adapters, so this is not a drop-in
cross-game SDK. Georgia bytes are not redistributed. See the
[canonical addon](../../addons/host-ui/README.md) for ownership and limits.
