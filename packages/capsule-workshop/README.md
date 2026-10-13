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
`tools/portable-ui-react/scripts/workshop.ts` supplies Host's repository paths
and native Player launch guidance. Those integrations are outside this package.

Source exports are for this private workspace stage; TypeScript tooling consumers
enable `allowJs` for the compiler modules. Registry publication and removal of
the legacy portable-ui import shims are later migration work.

The repository Vitest suite includes an isolated-workspace proof that copies
only the Capsule packages and fictional example, supplies an SDK artifact, and
checks HTTP preview, valid hot reload and invalid-TypeScript last-good retention.
Host theme/workshop tests live separately under the tooling's `tests/host`.
