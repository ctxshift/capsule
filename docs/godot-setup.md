# Set up Capsule in Godot

The initial adapter runs in **standard Godot 4.7 on Windows x86_64**. It uses
GDScript, native Godot Controls and a bundled GDExtension. The tested engine is
Godot 4.7.2 with the Compatibility renderer. Users do not need Godot .NET or a
separate .NET installation. The bundled host is compiled to a self-contained
native library from Capsule's existing portable runtime.

This is a development adapter for the running-game React workflow. Release
exports do not connect to the workshop. Production package loading, the full
control catalog and other platforms
remain separate acceptance gates. No marketplace listing or published Godot
release is implied by a locally built candidate.

## Install the addon

Until a GitHub release is promoted, build from the Capsule repository root with
its pinned tools (including Zig), .NET SDK, CMake, Git and Visual Studio C++ desktop build tools:

```powershell
node tools/capsule-godot/build.mjs --output .local/godot-candidate
```

The builder fetches a pinned godot-cpp revision, compiles the extension and native
host, and creates `capsule-godot-win-x64-0.1.0.zip`. Its root contains
`addons/capsule/` and a hash manifest. The bundle carries Wasmtime, the native
bridges, Yoga, the QuickJS carrier, matching SDK and upstream notices. It does
not create NuGet packages or publish anything. Build logs stay in the requested
output directory; NativeAOT currently reports dependency analysis warnings for
Newtonsoft and its expression infrastructure. The exercised paths require the
real native consumer gate below; warnings are not suppressed.
The engine bundle also includes a precompiled trusted QuickJS carrier for the
pinned Wasmtime runtime and Windows x86_64 baseline target. The builder produces
it from the pinned source carrier, keeping compilation out of UI startup. The
host verifies the source, target, runtime and artifact identities before loading
it; incompatible native code is rejected by Wasmtime. Addons cannot provide a
compiled carrier. Source-only engine resources remain supported for existing
adapters. Corrupt or incomplete compiled resources fail explicitly.

The native bridge is built into the fresh candidate directory. Building a Godot
bundle does not install or replace libraries in an open Unity project. The
compiled carrier has a separate hash descriptor and is covered by the bundle
inventory. Matching bytes in repeated local compilation do not establish full
clean-snapshot release reproducibility.
The tested C++ build uses Visual Studio 2026 and CMake 4.2. Compiler path mapping
keeps local checkout paths out of the owned native binaries; the builder rejects
a binary containing the current checkout or user-profile path.

Unpack the archive and copy `addons/capsule/` into a fresh or existing Godot
project, preserving unrelated addons. For a checked installation from this
checkout:

```powershell
node tools/capsule-godot/install.mjs --bundle <unpacked-bundle> --project <godot-project>
```

The installer verifies the complete inventory and hashes before mutation. An
identical installation is a no-op. A modified or different existing Capsule
directory is preserved and reported as a conflict.

Open the project normally and enable **Capsule** under **Project Settings >
Plugins**. Use **Project > Tools > Capsule: Connect React UI** to save an explicit
loopback workshop endpoint. Add a `CapsuleDevelopmentHost` Control to the scene
and give it a full-rect layout. A per-node Endpoint overrides the project setting.
The plugin does not rewrite existing scenes, create gameplay providers or start
an external process automatically.

Fresh first import is tested with a graphical Editor. On the tested engine,
`--headless --editor --import` exits with an access violation when this extension
is first scanned, including in a minimal project without the editor plugin.
Cached imports and the headless native-host smoke can pass. The cause remains
unresolved; do not use a cached success as fresh headless acceptance. The supplied
verification uses a normal window for import and rendering.

## Connect an external React project

Use the same workshop project and compiler path as the other engine adapter.
Before npm publication, from the Capsule checkout:

```powershell
bun packages/capsule-workshop/src/project-cli.ts init --project <ui-project> --packages <npm-candidate-directory>
bun install --cwd <ui-project>
bun packages/capsule-workshop/src/project-cli.ts dev --project <ui-project>
```

The npm candidate and engine bundle must have the same SDK. A mismatched SDK is
rejected while the previous UI remains visible. The workshop prints its endpoint;
use that endpoint in the plugin. Start the game once, then edit the external
project's TSX, theme or declared image files. Valid publications replace the
complete surface in that running game process. Compile, theme, preparation and
admission failures retain the last-good generation.

For the neutral starter, enable **Enable Demo Provider** on the host. It grants
only the example topic/action and counts requests; it is not a gameplay authority
or a general feature-provider implementation. Leave it disabled when registering
game-owned feature providers.

## Register game feature providers

Create a `feature_provider.gd` instance for each feature implemented by the game.
Set `definition` to its generated descriptor and `grants` to explicitly approved
permissions. Register providers with `host.register_provider(provider)` before
adding the host to the scene tree. The game supplies providers; UI publications
cannot install providers or grant themselves permissions. Required contracts,
versions, fingerprints and dependencies must match before a candidate can load.

Publish initial state before registration. State is replayed into each accepted
generation, including after React reload. `feature_codec.gd` encodes and decodes
the shared bool, uint32, UTF-8 text, list and ordered-record schemas:

```gdscript
var codec = load("res://addons/capsule/feature_codec.gd")
var provider = load("res://addons/capsule/feature_provider.gd").new()
provider.definition = descriptor # Generated feature descriptor supplied by the game.
provider.grants = PackedStringArray(["energy.read", "energy.add"])
var initial = codec.encode(descriptor.states.State.schema, {"count": 0})
assert(initial.ok)
assert(provider.publish_state("State", initial.bytes))
provider.request_received.connect(handle_request)
assert(host.register_provider(provider))
add_child(host)
```

`request_received(token, operation, payload)` delivers a bounded request on the
Godot main thread. Decode against the operation's input schema, apply the game's
rules, publish changed state, then call `resolve_request(token, encoded_result)`.
Pass `true` as the third argument to reject a request. The token is opaque and
single use. `request_cancelled(token)` lets asynchronous game logic abandon work
when its UI generation retires. Replies for cancelled or already resolved tokens
return `false`. The provider transport permits 64 outstanding requests and a
4 MiB payload; schema-specific bounds should be much smaller.

Keep game authority outside the addon. A successful transport request is not
permission to bypass server-owned rules. The host's optional `admitted_addon_id`
pins a game-selected addon identity. Enable `first_party_profile` only in trusted
game composition to select the shared extended runtime quotas; packages cannot
enable it themselves.
This trusted profile also disables guest fuel metering, matching a game-owned
script's execution trust. Community addons retain the shared metered budget.
The selected text catalog limit follows the profile's theme limit.

Image preparation follows the current snapshot instead of downloading the entire
theme art library. Atlas recipes reuse one decoded source texture per publication.
Workshops publish image content hashes. The host verifies downloaded bytes and
reuses matching decoded textures across revisions, including TSX-only reloads.
Missing images are fetched in groups of at most eight threaded requests. Failed
preparation preserves the previous surface and its image references.
Preparation still completes before a replacement is shown. Community sessions
allow 16 MiB encoded and 64 MiB decoded image data per revision; trusted sessions
allow 64 MiB encoded and 256 MiB decoded. Each download is limited to 4 MiB, and
PNG dimensions are checked before decoding. These are resource ceilings, not a
startup latency guarantee. Full image recipe rendering remains separate work.

`presentation_metrics` retains the latest 32 snapshot presentation records,
including package transfer, native admission/worker/layout, image preparation,
control construction and a frame-post-draw timestamp. Native initialization
trace offsets distinguish runtime construction from SDK/addon evaluation.
Measure compilation separately from ready-publication-to-frame latency. These
diagnostics are host data and add no player-facing controls. The startup and
post-compilation reload target is below 300 ms; the presence of timing records
does not establish that target or correct initial viewport presentation.

Current native presentation supports Box, ClipBox, Text, Image and Button.
Layout uses the shared Yoga coordinator and worker lifecycle. Buttons use Godot's
native input path. Unsupported node kinds reject replacement. Text currently
uses Godot's default font while layout uses the publication's metrics; exact font
matching, full theme effects, complex image recipes, text editing, world surfaces
and focus-preserving incremental rendering are not established by this slice.
The host rebuilds a complete tree on each accepted snapshot.

React component-local state resets on generation replacement. Observations from
registered providers are replayed. Disconnecting the workshop preserves the
last-good UI and controls; restarting it on the same endpoint resumes polling.
Native library, carrier or embedded SDK changes require a safe game/Editor
restart. Never replace loaded DLLs. Godot does not unload the NativeAOT module;
sessions close individually and the library remains loaded for the process.

## Verify a candidate

The native host exposes ABI version 1 through `capsule_abi_version`,
`capsule_create`, `capsule_exchange`, `capsule_free` and `capsule_destroy`.
The extension checks the version and pins one native library location per
process. Native JSON requests are bounded to 8 MiB; development HTTP publications
are separately limited to 900,000 bytes. Source and SDK
quotas remain those of the shared runtime. Native host calls and presentation
acknowledgments belong to the Godot main thread. Guest execution and Yoga
layout stay on the existing portable worker. This bridge is separate from
the guest's existing `portable_ui_v1` imports; no Godot API enters the guest.

```powershell
bun tools/capsule-godot/verify.ts --godot <godot-console-executable> --output <fresh-proof-directory> --addon <unpacked-bundle>/addons/capsule
```

Add `--providers` to exercise a generated neutral feature through the shared
feature registry and GDScript game provider instead of the demo capability.
This also checks the GDScript codec against TypeScript golden bytes and invalid
wire values. Run the engine-independent boundary checks with
`dotnet run --project tools/capsule-host-tests`; shared feature regressions use
`dotnet run --project tools/capsule-feature-tests`.
The [provider verification record](verification/godot-feature-providers.json)
and [native capture](verification/godot-feature-providers.png) retain the neutral
provider candidate's hashes, checks and remaining production limitations.
The [publication preparation record](verification/godot-publication-preparation.json)
adds referenced-image caching, source aliases, dimension limits, integer-token
preservation and community/trusted extended-theme admission checks.

The fixture creates a fresh game and a separate React project, uses the real
workshop CLI as an owned child process, imports the plugin, and starts a normal
window. It exercises native pointer input through an addon request and returned
observation, TSX/layout, theme color and image replacement, invalid TSX/theme
last-good recovery, workshop stop/restart, capture and clean shutdown. It retains
game process identities, publication revisions, logs and a PNG. Source-linked
authoring dependencies are explicit inside the temporary UI project.

The verifier accepts an executable path as an argument; no local installation
path is stored in tracked configuration. Its elapsed samples include compilation,
polling and capture scheduling and do not establish a 200 ms startup guarantee.
The [retained engine record](verification/godot-development.json) and
[unmodified capture](verification/godot-development.png) describe the tested
working-tree candidate, its archive/resource hashes and explicit limits.
