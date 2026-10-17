# Capsule workshop

Development compiler and browser renderer for Capsule hosts. This workspace
package accepts explicit addon, theme, browser-shell and runtime-artifact paths;
it does not locate a game checkout or choose a game theme.

Host integrations call `startWorkshop` with `addonRoot`, `source`, `output`,
`browserRoot`, `themeFile` and `sdkFile`. The addon root contains `addon.json`.
Optional `port`, `runtimeStatus` and `watchRoots` configure development behavior.
Ports are strict, so a different host's server is never silently reused.
The server binds loopback, typechecks TSX before publishing, and keeps the last
successful browser publication when compilation fails. It watches the addon,
SDK source, theme directory and supplied runtime artifact.

The browser shell supplies a `#stage-host` element and creates a
`BrowserGeneration` with a `BrowserGame` implementation (`nextObservation` and
`request`). It wakes the generation when a new observation arrives. The current
preview surface is 1440×900. Browser execution is a development mock, **not** a
Wasm security or fuel-budget test.

`../capsule-react` provides the addon-facing TSX API. `examples/portable-demo-game`
shows a fictional host with its own assets, mock observations and actions;
`tools/capsule-react/scripts/workshop.ts` supplies Host's repository paths
and native Player launch guidance. Those integrations are outside this package.

Source exports are for this private workspace stage; TypeScript tooling consumers
enable `allowJs` for the compiler modules. Registry publication and removal of
the legacy portable-ui import shims are later migration work.

Large addons can declare `scriptEntries` in their source manifest: relative TS/TSX
module paths that define static bundling boundaries. They do not mount extra UI
roots. The compiler extracts shared imports and emits declared `kind: "script"`
assets plus a small entry loader. All modules execute within the same guest and
React instance; there is no filesystem or network module resolution. Dynamic
imports are rejected. Omit `scriptEntries` for the existing single-file format.

Each generated script remains limited to 64 KiB, with at most eight module assets
and 256 KiB of aggregate source including the entry. Script declarations also
count against the host's existing declaration and encoded-package budgets.
Native activation shares one 200M fuel allowance across every module and entry;
memory, action grants and update limits remain unchanged. Module bytes participate
in revision hashing and atomic reload. The browser preview executes the same
ordered declarations before the entry. Chunk source maps are retained for offline
inspection; native carrier errors currently retain raw locations rather than
misattributing them to a single-file source map.

The repository Vitest suite includes an isolated-workspace proof that copies
only the Capsule packages and fictional example, supplies an SDK artifact, and
checks HTTP preview, valid hot reload and invalid-TypeScript last-good retention.
Host theme/workshop tests live separately under the tooling's `tests/host`.

Host themes may declare up to 16 named `buttonStyles` with `selectedTint` RGBA
values and `selectedTextureOpacity` in [0, 1]. Selecting that style retains
the normal button artwork, with the selected tint and texture strength.
Surface `rules` name up to four edges (`top`, `right`, `bottom`, `left`), each an
array `[width, red, green, blue, alpha]`, with width greater than zero and at most
8 logical pixels. These are inset decorations: they do not change layout or
intercept input. Style changes clear old decorations; absent tokens preserve
existing presentation. Authors reserve any desired content inset in normal layout.
