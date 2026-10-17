# Retained experiments and cleanup audit

The active native renderer is React18.3.1. The historical measurements and
negative results are retained in [the spike ledger](../../../docs/spikes/wasm-ui-addons.md).
They describe their original SDK/carrier/workloads; they are not fuel evidence for
current System/Settings with native Yoga.

## What remains

`react-bailout-adapter.ts` preserves the rejected React bailout experiment
against the shared native packet backend. It is a source prefix concatenated with the current host backend by the SDK
builder, not standalone modules, so standalone TypeScript checks would be false.
The builder fails explicitly if its shared-backend boundary is missing. `hello.ts`
is the minimal engine-entry diagnostic used by existing standalone .NET harnesses.
React/reconciler/scheduler license texts remain under `licenses/`. No experimental
frontend is selected by the Host workshop; unused alternate-framework tooling is removed.

From the repository root:

```powershell
bun tools/capsule-react/scripts/build.mjs --frontend bailout --output .local/react-bailout-optimized-sdk.js
bun tools/capsule-react/scripts/build.mjs --frontend hello --output .local/hello-engine.js
bun run --cwd tools/host-ui dev:native --source addons/host-ui/dev/hud.tsx --output .local/new-comparison-addon --once
```

Host development source/output paths are repository-relative. Direct SDK build
output is relative to its current working directory; package scripts supply the
repository output path explicitly. Absolute output paths also work.

The .NET harnesses remain the measurement owners. Their existing `--compare`,
`--functional`, `--hello`, `--guards`, and Yoga modes can be called directly;
explicitly choose the same addon and SDK identities before comparing results.
Some older modes consume `.local/react-fixture-owned/bundle.js`, so they require
a fresh package prepared at that path with current ownership, or a harness path
update. Never silently reuse a package owned by a moved entry. Fuel units are engine-specific. Historical alternate-framework measurements remain
only in the spike ledger; they have no current build/dependency support.

## Removed or consolidated

- Duplicate default/Yoga SDK build files and three frontend build wrappers are
  consolidated into `build.mjs` with `--frontend`/`--output`; same recipe, fewer
  entry points. The default destination is local, preserving approved artifacts.
- Native/Wasmtime run wrappers only forwarded existing .NET modes and wrote
  logs; direct harness commands preserve the capability without another API.
- Historical report wrappers asserted expired fixed SDK/carrier hashes and
  depended on retained local result names. Their methodology/results remain in
  the ledger; they are removed rather than presenting stale reports as current.
- The V8 no-op control was a historical diagnostic, not a native runtime or
  current acceptance test. Its measured finding remains in the ledger.
- Custom `test.mjs` and command dispatch `run.mjs` are removed. Vitest
  owns cases/timeouts/filtering; package scripts invoke actual tools directly.
- The unused alternate-framework adapter, SDK frontend branch, dependency,
  license and expired Pulley report were removed. The generic .NET backend
  comparison/functional modes and bounded Pulley runner remain React-capable.
- The duplicate scheduler process test is merged into the isolated VM scheduler
  test, including startup delay and due-callback cancellation, without mutating
  the test runner's global timers.

Source extraction, quota/wire goldens, keyed callbacks, atomic publication,
shared-root hot reload and128 mixed interaction equivalence remain covered by
typed tests. No UI/runtime behavior, assets, quotas or carrier changed.
