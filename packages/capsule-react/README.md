# Capsule React SDK

`@capsule/react` is the versioned, game-independent authoring package. It has no
Host theme, artwork or gameplay dependencies. This is a private workspace package;
it is not yet published to a registry.

- `@capsule/react`: declarative primitives, render and observation hooks.
- `@capsule/react/components`: theme-driven composed controls.
- `@capsule/react/window-state`: bounded window placement/state helpers.
- `@capsule/react/scroll-list`: controlled fixed-row scrolling and keyboard offset helpers.

`ScrollList` takes row IDs, a row renderer, viewport/row dimensions, an offset and
callbacks. It mounts only visible rows, preserves keyed rows while they remain
visible, and accepts scrollbar artwork from its caller. Keep each list's offset
and focused-list identity in screen state; route root key events through
`scrollKeyOffset` for the focused list. Wheel input does not change focus.

`onWheel` receives `{ deltaX, deltaY, modifiers }` in design units, positive
right/down. Native wheel steps map to 64 design units; the browser normalizes
pixel, line and page deltas. Events target the nearest subscribed ancestor and
do not move keyboard focus. Hosts bound each axis to ±8192 units. Compose scroll
state and clipped content in the addon; wheel input itself grants no game action.

`TextInput` is a single-line native editor. Use `onTextChange`,
`onCompositionChange`, `onSubmit`, `onCancel` and `onTextFocusChange` for semantic
events. Each receives text, provisional composition, UTF-16 selection anchor/focus,
`composing`, `userInput`, phase and the source-bound action context. Enter submits
once per press outside composition; Escape cancels and releases focus. Tab moves
between controls. Keyboard editing and clipboard operations stay native.

The `text` prop is a programmatic command, not a value that must be echoed after
every keystroke. Unchanged text preserves the native draft and caret across React
renders. Change `text`, or increment `textRevision` to apply the same text again
(for example, clear a submitted draft). Programmatic changes report
`userInput: false`; do not mirror them recursively. `maxLength` counts UTF-16
units, `maxBytes` counts UTF-8 bytes; both default to 512 and accept 1..512.
Oversized edits revert as a whole. `numeric` accepts ASCII digits and an empty
draft, and `readOnly` permits focus/selection without edits or submission.
Both adapters reuse the theme's select colors, border, font and padding.
Text events do not expand the separate 256-byte gameplay action limit.

```tsx
const [reset, setReset] = useState(0);
<TextInput width={240} height={32} text="" textRevision={reset} maxLength={80} maxBytes={160}
  onSubmit={event => {
    sendChatIntent(event.text); // The game's authorized API owns the request.
    setReset(value => value + 1);
  }} />;
```

See the [FrameXML findings and wire contract](../../docs/research/framexml-text-input.md).

Activation, pointer, close and key callbacks receive an action context with
`request(capability, payload?)`. Requests retain the originating widget handle;
the host still validates capabilities, generation, visibility and game-specific
preconditions. This permits guarded Back/close/Escape actions without routing
through a hidden activation control. Existing callbacks that ignore their event
argument continue to work. Retired widget callbacks cannot submit new requests
from queued input. This does not grant capabilities or route gameplay keys while
a native text field owns focus.

Exports include TypeScript source as the authoritative development types. Run
`bun run declarations` here to emit declaration files from the implementation;
generated `dist/` output is ignored. The current addon compiler bundles the source
and binds React to the host-provided compatible runtime. Importing this package
outside a host does not create a renderer or grant native capabilities.

`Viewport surface="preview-key"` describes a live host-rendered resource in the
normal layout tree. It is separate from `Image asset`: only explicit immutable
host surface admission grants access, with a bounded lease count. The wire uses
node kind13 and the existing resource-key field/SetImage opcode; the key is
validated against surface admission rather than sprite/theme assets. An empty
or unknown surface is rejected. Existing hosts default to no admitted surfaces.
The node/SDK contract is available; native and browser rendering providers are
not installed yet and currently reject viewport commits explicitly. No static
image fallback or per-frame React update is implied.

Install the repository workspace with `bun install --frozen-lockfile` at its root.
Run `bun run typecheck` here or `mise run check-ui` at the root. Addons depend on
this package normally; they do not include ambient declarations from tool paths.
Legacy `@portable-ui/react` declarations and source re-exports remain temporary
compatibility adapters for existing diagnostics.

Storage, service discovery and public asset exports are separate planned slices;
they are not advertised here as implemented APIs.

`Range.onValueChange(value, event)` and `Select.onSelectionChange(index, event)`
provide an `ActionEvent` as their second argument. Use `event.request` to send a
host-authorized intent associated with that control. Existing callbacks that
accept only the value remain compatible. The native host still checks the
capability, generation, source control and payload before applying a setting.
## World anchors and effects

`WorldAnchor` and `Effect` are Box aliases with optional `worldAnchor` and `effect`
props. The host projects admitted entity attachments and advances bounded motion,
fade and expiry without per-frame React updates. See the
[world presentation contract](../../docs/capsule-world-presentation.md) for
options, native grants, browser simulation, lifecycle rules and verification.
