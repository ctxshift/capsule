# Capsule counter example

A minimal community-style addon using only public Capsule imports. It displays a
counter and handles a button click without gameplay capabilities or Host artwork.
It does not demonstrate installation permissions or native multi-addon isolation.

From the repository root, install with `bun install --frozen-lockfile`, then run
`bun run --cwd examples/addons/capsule-counter typecheck`.

To compile a native-compatible development package, run:

```powershell
bun tools/capsule-react/scripts/dev.mjs --source examples/addons/capsule-counter/src/main.tsx --project-root examples/addons/capsule-counter --manifest examples/addons/capsule-counter/addon.json --output .local/capsule-counter --once
```

Generated JavaScript is tooling output; edit the TSX source. The integration test
exercises rendering, click behavior and last-good retention after a type error.
