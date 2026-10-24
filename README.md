# Capsule

Capsule provides portable addon runtime libraries and a Unity presentation adapter.
This repository contains the native package candidate, React authoring SDK,
feature infrastructure, compiler/workshop and reusable RPG/MMO/skill packages.
The engine plugin is the installation entry point: npm supplies JS/TS tooling,
and GitHub Releases supplies engine bundles. See the [release plan](docs/releases.md).
No game integration, game artwork, backend or remote registry is required for the
portable checks below. Candidate creation does not publish a release.

First-party code is Apache-2.0. Bundled upstream components retain their original
licenses; see [third-party notices](THIRD-PARTY.md).

See the [Unity setup guide](docs/unity-setup.md) for existing-project integration,
first-run resource/settings checks and the proposed Editor setup automation.
See [contributing](CONTRIBUTING.md) for pinned mise tools and the Capsule Bees
tracker, and [game features](docs/game-features.md) for contracts and generators.
See [UI development](docs/ui-development.md) for the primary workflow: a running
game plus a React development server, with UI edits live-reloading in the game.
The engine demonstration remains an explicit acceptance gate.

## React authoring

From the repository root with Bun 1.3.6 and Node installed:

```powershell
bun install --frozen-lockfile
bun run typecheck
bun run test
bun run check:js
bun run build:sdk
bun run dev:demo
```

The last command starts the neutral Aster browser preview and watched workshop.
It does not start an engine host. UI projects use public `@capsule-sdk/react`,
`@capsule-sdk/features` and `@capsule-sdk/workshop` exports. The compiler CLI accepts
explicit source/project/manifest/output paths; see the
[counter example](examples/addons/capsule-counter/README.md).
Packed authoring and engine candidates use the [release workflow](docs/releases.md).
The [authoring import map](docs/provenance/authoring-import.json)
records the reviewed sources and adaptations; filtered authoring history is
retained as a merge parent. [Verification](docs/verification/authoring.json)
distinguishes browser/compiler/native proofs from in-game live reload.

## Layout

- `unity/Packages/com.capsule.core`: engine-independent sessions, contracts,
  bounded queues and host lifecycle.
- `unity/Packages/com.capsule.package`: package validation, assets, feature
  admission and bindings; explicit Core/Newtonsoft dependencies.
- `unity/Packages/com.capsule.wasmtime`: managed guest, QuickJS carrier, shipped
  SDK TextAssets and Windows x86_64 runtime/bridge libraries.
- `unity/Packages/com.capsule.yoga`: shared layout and Windows x86_64 Yoga bridge.
- `unity/Packages/com.capsule.unity`: native rendering, text, input, images, world
  projection and shaders, with separate EditMode/PlayMode test assemblies.
- `packages/capsule-native`: standalone .NET Standard 2.1 projects compiling
  those same package-owned sources, with dependency enforcement.
- `packages/capsule-{features,react,workshop}`: schemas/codecs/codegen, public
  React controls/contracts, compiler and browser workshop.
- `tools/capsule-react`: SDK build, diagnostics and authoring regression tests.
- `examples`: neutral Aster and counter addons with no game content.
- `packages/{rpg-features,mmo-features,skill-system-features}`: reusable feature
  definitions, mocks and native contract projects.
- `packages/inventory-feature`: example-only inventory contract and view.
- `unity/Packages/com.capsule.features.*`: canonical generated native contracts.
- `tools`: neutral managed/native test runners and pinned native build recipes.
- `unity`: minimal Unity 6000.6.3f1 consumer of the embedded packages.

The [import map](docs/provenance/native-import.json) records source revision,
original paths, byte hashes and preserved GUIDs. Native-only history was imported
with its preceding path names and original authorship. The former shared parent
folder metadata is archived outside Unity; each former assembly directory's GUID
now belongs to its package's `Runtime` directory.

Host integrations belong in their consuming projects. Do not install copied
runtime sources and these UPM packages into the same Unity project.

## Portable verification

Run from this repository root with Node, .NET SDK supporting .NET 9 runners, and
PowerShell 7. Portable production libraries target .NET Standard 2.1.

```powershell
node tools/verify-native-import.mjs
node --test tools/tests/verify-native-import.test.mjs
dotnet build packages/capsule-native/Core/Capsule.Core.csproj -p:RestoreLockedMode=true
dotnet build packages/capsule-native/Package/Capsule.Package.csproj -p:RestoreLockedMode=true
dotnet build packages/capsule-native/Wasmtime/Capsule.Wasmtime.csproj -p:RestoreLockedMode=true
dotnet build packages/capsule-native/Yoga/Capsule.Yoga.csproj -p:RestoreLockedMode=true
dotnet run --project tools/capsule-managed-tests -p:RestoreLockedMode=true
dotnet run --project tools/capsule-core-tests -p:RestoreLockedMode=true
dotnet run --project tools/capsule-package-tests -p:RestoreLockedMode=true
dotnet run --project tools/capsule-feature-tests -p:RestoreLockedMode=true
pwsh -NoProfile -File tools/capsule-managed-tests/verify-dependencies.ps1
```

The import verifier checks this extraction snapshot's unchanged payloads. A later
intentional runtime change requires reviewing its provenance expectations; this
snapshot is not a promise that runtime source can never evolve.
Git line-ending translation is permitted only for C# and `.meta` text with an
explicit canonical digest. Runtime binaries, SDKs, carrier and shaders retain
their exact byte hashes.

For a real Windows native lifecycle check, resolve resources from this checkout
and use a fresh output directory:

```powershell
$repo = (Get-Location).Path
$wasm = "$repo/unity/Packages/com.capsule.wasmtime/Runtime"
$yoga = "$repo/unity/Packages/com.capsule.yoga/Runtime"
pwsh -NoProfile -File tools/capsule-native-tests/run-isolated.ps1 `
  -EngineLibrary "$wasm/Plugins/x86_64/wasmtime.dll" `
  -BridgeLibrary "$wasm/Plugins/x86_64/portable_ui_wasmtime.dll" `
  -YogaLibrary "$yoga/Plugins/x86_64/portable_ui_yoga.dll" `
  -Carrier "$wasm/Carrier/CapsuleQuickJs.bytes" `
  -Sdk "$wasm/Resources/CapsuleReactSdk.txt" `
  -AddonBundle "$repo/tools/capsule-native-tests/fixture/compiled/bundle.js" `
  -Output "$repo/.local/native-proof-new"
```

The checked-in compiled addon is the neutral TSX fixture snapshot, not game UI.
Its source is adjacent under `fixture/`; rebuild it with the public compiler via
`bun tools/capsule-native-tests/build-fixture.mjs <fresh-output-directory>`.
Use that output's `bundle.js` with `.local/authoring-sdk.js` to test current
authoring output. The import provenance records the snapshot digest. Omitting both SDK and
bundle runs only the four-resource smoke. Native build/install operations are
for runtime maintenance and require Zig 0.16.0; carrier builds additionally require
wasm-tools 1.241.2. Never replace a DLL while an Editor or Player uses it.

## Unity validation consumer

Open `unity/` with Unity 6000.6.3f1. Its project settings enable the Input System
backend and Linear color space. It intentionally has no game scenes or game
bootstrap. The consumer includes imported TMP Essentials with their upstream
notices so a fresh checkout does not need the first-use import prompt. If these
resources are removed, use the standard **Import TMP Essentials** action, without
examples/extras. They are consumer dependencies, not Capsule artwork or runtime-owned fonts.
The text-input conformance fixture also constructs Windows Arial dynamically;
it does not redistribute system fonts.

Use the official Unity CLI/Pipeline and `agent-unity` with the explicit project
path. Preserve user scenes and active Play work. Do not start a headless import
against an open project or start a second Editor for it.

```powershell
unity status --project-path ./unity --format json --non-interactive
agent-unity preflight --project-path ./unity
agent-unity test editmode Capsule.Unity.EditModeTests --filter-type assembly --project-path ./unity
agent-unity test playmode Capsule.Unity.PlayModeTests --filter-type assembly --project-path ./unity
```

For a closed project use `agent-unity test-headless` with the same mode/filter.
Retain nonzero results and fresh logs. [Test extraction](docs/test-extraction.json)
identifies excluded game-only assertions. Package tests do not establish game
backend behavior, final game rendering parity, IL2CPP or additional platforms.
