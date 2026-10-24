# Neutral native Capsule proof

`--whole-observations` with the four resource paths exercises a 100,000-byte message
through the actual carrier, checks the 32-bit length envelope and measured sandbox
copy bytes, and verifies repeated length queries and short-buffer retention.
It exercises both contiguous and segmented observations, asserting that segmented
headers/payloads reach the sandbox without materializing an intermediate buffer.

This host uses Capsule assemblies, native Wasmtime/QuickJS and Yoga. Optional
lifecycle mode uses a separately compiled neutral React addon. Its mock game observes uint16 charge on topic 7/schema 1 and accepts
capability 7 requests after checking the accepted generation, source and payload.
The fixture's monospace metrics are constructed by this neutral host.

From the repository, provide absolute paths to the existing pinned resources:

```powershell
pwsh -NoProfile -File tools/capsule-native-tests/run-isolated.ps1 `
  -EngineLibrary <absolute-wasmtime.dll> -BridgeLibrary <absolute-portable_ui_wasmtime.dll> `
  -YogaLibrary <absolute-portable_ui_yoga.dll> -Carrier <absolute-carrier.bytes> `
  -Output <absolute-fresh-directory>
```

The script publishes the .NET host and copies the supplied resources. For lifecycle
mode, also pass `-Sdk <absolute-compatible-react-sdk.js>` and
`-AddonBundle <absolute-compiled-neutral-addon.js>`. The four-resource smoke
requires no React compiler or Host checkout.

Build a fresh neutral addon through the public workshop compiler when authoring
dependencies are installed:

```powershell
bun tools/capsule-native-tests/build-fixture.mjs .local/authoring-native/addon
```

Use the resulting `bundle.js` as `-AddonBundle`; pass the matching SDK as
`-Sdk`. Keep those input paths explicit in native proofs.
Execution changes working directory to the isolated output and passes only staged
absolute resource paths. It needs .NET 9 and the copied native resources, not Unity
or game files. Each subprocess has a 120-second deadline and retained stdout/stderr.

The four-resource smoke mode remains available. Six-resource mode additionally
accepts SDK and bundle paths and proves real React snapshot/layout, two addons on
one dispatcher, observation/request isolation, failed and successful replacement,
stale work rejection, continued peer operation, and complete native root cleanup.
Events enter the portable semantic input queue; this headless proof does not claim
Unity raycast or visual validation. Fault injection appends a throwing statement
to a copy of the compiled addon; it never constructs UI procedurally or changes
production runtime code. Existing resource/fuel/queue limits remain unchanged.

Artifacts include publish, smoke and optional lifecycle logs plus SHA-256 hashes
of copied runtime resources and published managed files.

## Bridge regression checks

With the same pinned native resources, run the import-boundary checks directly:

```powershell
dotnet run --project tools/capsule-native-tests -- --imports `
  <absolute-wasmtime.dll> <absolute-portable_ui_wasmtime.dll> `
  <absolute-portable_ui_yoga.dll> <absolute-carrier.bytes>
```

These combine guarded-memory calls to the managed import boundary with a real
native guest for invocation-quota and terminal-state checks. They cover short
output buffers without dequeuing, rejected grants, stale handles, unsupported
services, malformed packets, exception containment and host-call accounting.
Six-resource lifecycle mode also verifies diagnostic propagation through Yoga
and the worker, addon isolation and cumulative metrics across reload/disposal.
See the [bridge contract](../../packages/capsule-native/bridge-contract.json).

`--text-input` takes the same four resource paths followed by the built React
SDK path. It exercises real QuickJS React callbacks, the native TextInput packet,
Unicode composition/change/submit delivery, source-bound requests, a React clear
command returning to the native snapshot, and stale-generation rejection.
The Unity PlayMode text-input fixture separately exercises TMP editing and
gameplay shortcut isolation; neither test claims an operating-system IME session.
## Observation transport measurements

For feature snapshot recovery, build the neutral fixture with
`bun tools/capsule-react/scripts/build-feature-recovery.ts .local/authoring-native/recovery.js`.
Provide a compatible SDK and the compiled recovery fixture as explicit absolute
paths after the four native resources for `--feature-recovery`.
The real Wasm guest exercises loader admission, an
explicit read without a UI event, a dropped ordered revision, snapshot recovery,
late subscription and retirement. `--imports` separately checks that gameplay
and ungranted capabilities cannot use the read-only path.

Set `CAPSULE_OBSERVATION_METRICS` to a JSON output path and run
`--whole-observations` with the four native resource paths shown above. Unset the
variable to run the ordinary correctness proof. The benchmark warms each case
for 20 updates and measures 200 updates at 555 and 23,903 payload bytes, comparing
contiguous envelopes with segmented transfer through the real Wasm carrier.
It checks JavaScript receipt and reports median/p95 time, managed allocations,
managed collection counts and bytes written into sandbox memory.

This measures framing and receipt only: contract encoding/decoding, worker queues,
networking and rendering are excluded. Managed allocation counters do not measure
the unmanaged QuickJS/Wasm heap. Run it separately from Player FPS captures.
One local run measured 24,064 versus 80 managed bytes per large update and
0.0132 versus 0.0033 ms p95; both paths wrote 23,919 sandbox bytes. These are
diagnostic measurements, not portable performance thresholds.

## World presentation proof

Use `--world-presentation` with the same five resource arguments as `--text-input`
to exercise React world attachment/effect commands through the actual carrier,
effect revision updates and generation teardown. It tests native transport;
Camera/Canvas presentation is covered by `CapsuleWorldPresentationTests`.

A checked-in neutral compiled fixture is available at `fixture/compiled/bundle.js`. Its TSX source and provenance are retained; no authoring compiler is required to execute it.
