# Aster demo game

A small fictional starship addon using the public Capsule packages. Its browser
mock publishes reactor charge on topic 7, schema 1, as a little-endian uint16.
The button requests capability 7 with payload `[1]`; the mock adds five points,
capped at 100. The addon only presents observations and sends requests.

From this directory, after installing the repository workspace dependencies:

```sh
bun run dev --sdk /absolute/path/to/host-runtime.js
bun run typecheck
```

Supply a compatible host runtime; the example does not build one. The workshop
serves this example's browser shell and theme. All theme values are authored for
this sample. `assets/neutral.png` is a generated three-pixel square; the sample
uses Courier New and synthetic monospace advances of 9.6 pixels at 16 pixels.
This is a browser development mock, with no connection to a game server.
