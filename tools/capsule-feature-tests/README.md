# Addon feature activation checks

From the repository root, run `dotnet run --project tools/capsule-feature-tests`.
This standalone consumer checks the reusable Core and Package assemblies without a
Unity editor or Host gameplay classes. It covers manifest requirements, native
version/dependency resolution, explicit permissions and provider retirement.

An optional `features` array in `addon.json` contains records with `name`, `range`,
`contractVersion`, `fingerprint`, `optional` and `permissions`. The compiler preserves
these records in the published package. Names are publisher-qualified; ranges are
exact versions or `major.minor.*`. `contractVersion` identifies the definition used
by the addon, and its fingerprint must match a provider claiming that same version.
Optional requirements that cannot resolve are omitted. Dependencies do not grant
permissions; host grants are scoped to both feature name and permission.

Packages without this field remain compatible. Manifest requirements only declare
needs: the host resolves them before guest construction and attaches the resulting
selection to a fresh activation. Replacement of a selected provider or any transitive
dependency invalidates the old selection. No provider code runs during resolution.

Session checks cover permission-filtered metadata, per-member coalescing under
backpressure, initial state delivery, asynchronous request results, duplicate/stale
request fencing and retirement. The Unity Player fixture separately checks real
loader rejection and preservation of the accepted addon.

Run `dotnet run --project tools/capsule-feature-tests -- --metrics .local/feature-transport/metrics.json`
for an equivalent-payload native transport benchmark: 555 and 23,903 byte states,
one/eight addon sessions, and zero/two rejected queue attempts before acceptance.
It compares contiguous and segmented delivery, including the final copy to unmanaged
memory, and records median/p95 time and thread allocations after warmup. It excludes
codecs, worker scheduling, guest execution, rendering and networking; do not present
it as end-to-end latency.
Sessions retain immutable state/result envelopes across rejected queue attempts.
A newer state invalidates its retained envelope, and acceptance or retirement
releases the session reference. This removes repeated framing copies under
backpressure. The segmented production path retains a small per-addon header and
borrows the shared immutable provider payload until the Wasm boundary. The legacy
contiguous API lazily materializes an immutable compatibility buffer. Never mutate
either segment after publication. Worker tests verify identity, lifetime fencing
and native byte order; the neutral native host verifies actual carrier delivery.

The live HUD backend fixture enables `HOST_FEATURE_TIMING_PROOF` in a Development
Player and retains `session.json.feature-timing.json`. It asserts that ongoing state
publications reach the worker queue in the same host frame. Initial activation
snapshots are excluded. Diagnostic file writes happen after the measured interval.
The nameplate fixture also records click-to-handler and click-to-visible-model time;
these do not measure GPU presentation or physical display latency.
