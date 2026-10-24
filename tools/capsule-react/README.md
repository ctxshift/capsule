# Capsule React SDK tools

Build and test the reusable native React renderer. The public compiler and browser
renderer belong to [@capsule/workshop](../../packages/capsule-workshop/README.md).
See [UI development](../../docs/ui-development.md) for the running-game workflow
and the remaining engine demonstration gate.

From the repository root:

```powershell
bun install --frozen-lockfile
bun run typecheck
bun run test
bun run build:sdk
bun run dev:demo
```

`src/host.ts`, `native-bridge.ts` and `scheduler.ts` implement the native SDK.
`scripts/build.mjs` accepts explicit output and frontend options without loading
game configuration. The default package build writes ignored `.local/react-host.js`
at repository root. `build:yoga` uses the same renderer; layout comes from runtime
intent. `scripts/compile.ts` compiles or watches an addon with explicit source,
project, manifest and output paths. No game-specific configuration is loaded.

Tests cover encoding, scheduling, schema requirements, compiler publication,
themes, browser rendering, isolated consumers and last-good preservation.
[Diagnostic inputs](docs/benchmarks.md) and [profiling](docs/PROFILING.md) describe
optional instrumentation. Upstream React/reconciler/scheduler notices remain in
`licenses/`. Private `portableUi*` globals and the PUI wire format are compatibility
names. Ordinary addon edits do not rebuild the SDK or game; installing an altered
SDK requires matching runtime compatibility and validation.
