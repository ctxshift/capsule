# Set up Capsule in a Unity project

This guide records the working native package setup and first-run problems
observed during validation. The native libraries are real UPM packages; an Editor
setup wizard is not implemented yet. Authoring/compiler distribution, reusable
game features and release feeds are separate stages.

The intended everyday workflow is a running game plus a React UI development
server, with edits live-reloading in the game. See [UI development](ui-development.md)
for the loop and required end-to-end demonstration. Plugin setup should configure
and validate that development connection as well as runtime dependencies.

## Requirements and verified starting point

Use Unity **6000.6.3f1** on Windows x86_64 for the shipped native libraries. The
manifests declare uGUI/TextMeshPro **2.6.0**, Input System **1.19.0** and Unity
Newtonsoft **3.2.2**. Other platforms and final IL2CPP Player acceptance are not
established by this candidate. Ordinary integration does not require rebuilding
Wasmtime, QuickJS or Yoga.

Open this repository's `unity/` project for the verified reference consumer.
It embeds all eight packages, retains TMP Essentials, enables native input and
uses Linear color space. It has no Host scenes, art, backend or game bootstrap.

Run from the repository root with the official Unity CLI/Pipeline and
`agent-unity` installed:

```powershell
node tools/verify-native-import.mjs
unity status --project-path ./unity --format json --non-interactive
agent-unity preflight --project-path ./unity
agent-unity test editmode Capsule.Unity.EditModeTests --filter-type assembly --project-path ./unity
agent-unity test playmode Capsule.Unity.PlayModeTests --filter-type assembly --project-path ./unity
```

Use connected tests while the Editor is open. Use `agent-unity test-headless`
only for a genuinely closed project. Do not launch a second Editor against it.
Diagnose inconsistent process/Pipeline discovery rather than bypassing safety
checks. Pipeline is validation tooling, not a production addon dependency.

## Install into an existing project

Commit or back up the host's dependency/settings changes first. Use one compatible
Capsule revision. Do not install the packages alongside old copied runtime scripts
in `Assets`; that would compile duplicate assemblies.

For local development today, use Package Manager's **Add package from disk** to
select `package.json` under each `unity/Packages` directory, resolving dependencies
before the adapter:

1. `com.capsule.core`
2. `com.capsule.package`
3. `com.capsule.wasmtime` and `com.capsule.yoga`
4. `com.capsule.unity`

Every internal package must resolve locally. A version dependency alone cannot
find an unpublished Capsule package in Unity's public registry. Check the host's
manifest/lockfile, preserve package `.meta` files and native importer settings,
and do not commit machine-specific local paths. The planned attach/detach workflow
will manage temporary overrides and restore the pinned release set; it is not
available yet. The [release workflow](releases.md) builds a GitHub Unity bundle
containing all eight embedded packages; install that compatible set together.

Hosts using reusable contracts can also install `com.capsule.features.rpg`,
`com.capsule.features.mmo` and `com.capsule.features.skill-system` from disk.
They depend on Core and supply the canonical generated contract assemblies;
standalone consumers compile the same source. See [game features](game-features.md).
Preserved GUIDs support migration, but host wiring and Unity import/build of these
new feature packages are not established by standalone provider tests.

A native DLL change requires a safe Editor restart because Windows retains loaded
libraries. Keep carrier, SDK and native versions compatible. A compiler/SDK build
does not establish compatibility with an arbitrary older native runtime.

## First-run settings and resources

**Input System:** installing the package does not enable its native backends.
The fresh consumer displayed the warning that native input would not arrive.
Enable compatible **Project Settings > Player > Active Input Handling**, save
work and allow the requested restart. The reference consumer uses **Both**
(`activeInputHandler: 2`) for native text/composition and Input System coverage.
Setup must validate the host's required paths without silently disabling existing
input. Check editing/focus and shortcut routing, not compilation alone.

**Color space:** the reference is **Linear**. Gamma failed the alpha-aware shader
conformance test; changing only the consumer setting made the unchanged shader
pass. A plugin should report this difference and offer an explicit choice,
rather than silently change an existing game's rendering configuration.

**TextMeshPro:** if TMP Importer appears, choose **Import TMP Essentials**. Examples
and extras are unnecessary. Await import and verify
`Assets/TextMesh Pro/Resources/TMP Settings.asset` before text tests. Missing
settings caused font-creation/clipping exceptions and contaminated unrelated
PlayMode cases. The automatic import timed out while the importer was open;
completing the standard import action resolved it.

The consumer retains Essentials and the bundled Liberation Sans OFL/Unity notices
so a fresh checkout avoids that prompt. These are consumer dependencies, not
Capsule game artwork. Exact game fonts/themes remain explicit addon/host inputs.
The editing fixture additionally constructs Windows Arial dynamically; no system
font is redistributed. Preserve upstream notices when shipping consumer resources.

## Host integration boundary

The adapter is a rendering/input library, not a game bootstrap. A host supplies:

- Its Canvas/RectTransform and `INativeUiWidgetFactory`, including text resources
  and explicitly admitted images/materials.
- A `CapsuleNativeTarget`, accepted snapshot commits, activation and disposal.
- Per-addon admission/runtime lifecycle through `UiAsyncHostSession`, bounded
  queues and normalized input for the matching generation.
- Explicit compatible carrier/SDK/native resources and optional viewport/world
  projection services with owned leases.
- Typed observation/request providers, permissions and authoritative game services.

Engine objects stay on the presentation thread, outside worker guest factories.
Rejected replacement retains the prior valid generation. Retirement must release
focus, pointer capture, image leases, native roots and subscriptions. Compiler or
package failures retain the last-good addon publication. Capsule does not own
account, economy or world outcomes and must not add a hardcoded game UI fallback.

The neutral fixtures demonstrate adapter/lifecycle behavior, not a production
game host, live backend binding or an OS IME session. The extracted authoring
workspace supplies the [UI development entry point](ui-development.md); its
browser preview and native proofs do not yet establish rendered game live reload.

## Proposed plugin setup automation

Add an Editor-only **Capsule > Setup** entry and the same callable API for CI to
`com.capsule.unity`. Keep runtime assemblies free of Editor dependencies and do
not mutate projects automatically on import.

The setup command should validate package closure, versions, native importers and
carrier/SDK/native identity; inspect input and restart requirements; detect TMP
Settings and import only Essentials from the installed uGUI package; and await
completion with a bounded deadline. Report modal action requirements instead of
blindly resubmitting an ambiguous import. Report color-space/font differences,
offering explicit fixes while preserving existing host choices.

Validate Canvas/factory and resource/lifecycle configuration. An optional neutral
sample scene is useful, but setup must not create game-specific services or copy
licensed game content. Return structured readiness results with actionable
failures, resource identities and restart requirements. Repeated runs must be
idempotent and preserve dirty scenes/custom settings.

Exercise actual fresh/existing-project installation, missing resources, disabled
input, incompatible native versions and interrupted/modal imports. Mock readiness
alone is insufficient. Global SDK installation, game backend startup, registry
publication and branch creation do not belong in plugin setup.

## Validation boundaries

This candidate passed 37 EditMode and 11 PlayMode tests; PlayMode was repeated to
check retained state. Portable managed/native checks also passed. See
[validation evidence](verification/native-packages.json). This does not establish
published-package installation, final game fidelity, another platform or a Player
build.
