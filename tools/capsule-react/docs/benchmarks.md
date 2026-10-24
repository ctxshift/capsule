# Retained diagnostic inputs

The active native renderer uses React 18.3.1. `react-bailout-adapter.ts` is a
historical comparison prefix concatenated with the shared packet backend by the
SDK builder; it is not a standalone module. The builder rejects a missing backend
boundary. `hello.ts` is a minimal engine-entry diagnostic. These do not change
the production frontend or resource budgets.

From the repository root:

```powershell
bun tools/capsule-react/scripts/build.mjs --frontend bailout --output .local/bailout-sdk.js
bun tools/capsule-react/scripts/build.mjs --frontend hello --output .local/hello-engine.js
```

Use explicit addon, SDK, carrier and native library inputs for native harnesses.
Game-specific performance workloads and their historical reports remain with the
game repository; they are not independent Capsule acceptance measurements.
