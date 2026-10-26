# Set up Capsule in Godot

The initial adapter runs in **standard Godot 4.7 on Windows x86_64**. It uses
GDScript, native Godot Controls and a bundled GDExtension. The tested engine is
Godot 4.7.2 with the Compatibility renderer. Users do not need Godot .NET or a
separate .NET installation. The bundled host is compiled to a self-contained
native library from Capsule's existing portable runtime.

This is a development adapter for the running-game React workflow. Release
exports do not connect to the workshop. Production package loading, the full
control catalog, generated feature-provider integration and other platforms
remain separate acceptance gates. No marketplace listing or published Godot
release is implied by a locally built candidate.

## Install the addon

Until a GitHub release is promoted, build from the Capsule repository root with
its pinned tools, .NET SDK, CMake, Git and Visual Studio C++ desktop build tools:

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
or a general feature-provider implementation. Leave it disabled in real game
integration until an explicit game-owned provider adapter is implemented.

Current native presentation supports Box, ClipBox, Text, Image and Button.
Layout uses the shared Yoga coordinator and worker lifecycle. Buttons use Godot's
native input path. Unsupported node kinds reject replacement. Text currently
uses Godot's default font while layout uses the publication's metrics; exact font
matching, full theme effects, complex image recipes, text editing, world surfaces
and focus-preserving incremental rendering are not established by this slice.
The host rebuilds a complete tree on each accepted snapshot.

React component-local state resets on generation replacement. Observations from
the opt-in demo provider are replayed. Disconnecting the workshop preserves the
last-good UI and controls; restarting it on the same endpoint resumes polling.
Native library, carrier or embedded SDK changes require a safe game/Editor
restart. Never replace loaded DLLs. Godot does not unload the NativeAOT module;
sessions close individually and the library remains loaded for the process.

## Verify a candidate

The native host exposes ABI version 1 through `capsule_abi_version`,
`capsule_create`, `capsule_exchange`, `capsule_free` and `capsule_destroy`.
The extension checks the version and pins one native library location per
process. JSON requests are bounded to 900,000 UTF-8 bytes; source and SDK
quotas remain those of the shared runtime. Native host calls and presentation
acknowledgments belong to the Godot main thread. Guest execution and Yoga
layout stay on the existing portable worker. This bridge is separate from
the guest's existing `portable_ui_v1` imports; no Godot API enters the guest.

```powershell
bun tools/capsule-godot/verify.ts --godot <godot-console-executable> --output <fresh-proof-directory> --addon <unpacked-bundle>/addons/capsule
```

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
