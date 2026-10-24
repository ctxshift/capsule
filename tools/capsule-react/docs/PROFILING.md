# SDK profiling

From the repository root, `bun run --cwd tools/capsule-react profile` writes
`.local/react-profile-host.js`. This optional builder adds diagnostic imports at
explicit scheduler/observation/render/commit markers. It fails if an expected
instrumentation anchor has changed.

Run instrumented and uninstrumented SDKs against the same explicit addon,
carrier and native libraries in a compatible measurement host. Fuel measures
Wasm guest instructions; native Yoga and engine text/rendering CPU are outside
that measure. Instrumentation itself consumes fuel. Do not treat instrumented
maxima or old game-specific reports as current production budgets or frame rate.
Historical game workload results remain in the originating game repository.
