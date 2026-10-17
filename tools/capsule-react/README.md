# Capsule React SDK tools

This workspace builds and tests the reusable native React renderer. Host's
workshop launcher, browser shell, theme policy and game integration proofs live in
[tools/host-ui](../host-ui/README.md). The portable compiler and browser renderer
are published from [@capsule/workshop](../../packages/capsule-workshop/README.md).

From the repository root:

```powershell
bun install --frozen-lockfile
bun run --cwd tools/capsule-react typecheck
bun run --cwd tools/capsule-react test
bun run --cwd tools/capsule-react build
bun run --cwd tools/capsule-react profile
```

- `src/host.ts`, `native-bridge.ts` and `scheduler.ts` implement the native SDK.
- `scripts/build.mjs` builds the SDK without loading game configuration.
  `package-paths.mjs` and `sdk-paths.mjs` resolve its source and public dependencies.
- `tests/` covers protocol encoding, scheduling, portable compilation/publication,
  browser rendering, isolated external consumers and tooling boundaries.
- `benchmarks/` and `fixtures/` retain explicitly selected diagnostic inputs;
  [the experiment audit](docs/benchmarks.md) explains their purpose.
- `licenses/` retains upstream notices.

The default build writes ignored `.local/react-host.js` at repository root.
`build:yoga` writes `.local/react-yoga-host.js`; both use the current shared
renderer, with layout selected by runtime intent. Explicit SDK installation is
a native-runtime change and needs its own validation. Ordinary addon edits do
not rebuild the SDK or Unity.

Use `mise run ui-camp-dev` or `mise run ui-entry-dev` for Host authoring.
There is no game-specific `dev` command in this workspace.

[Profiling methodology](docs/PROFILING.md) retains historical measurements;
those measurements are not current game FPS or current resource limits.
Private `portableUi*` globals and the PUI wire format are compatibility names,
not another UI framework.
