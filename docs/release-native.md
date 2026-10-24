# Direct managed and native runtime bundle

GitHub Releases distributes `capsule-runtime-win-x64-<version>.zip` for standalone
hosts. Its `managed/` directory contains seven .NET Standard 2.1 Capsule and
feature DLLs plus the pinned Newtonsoft.Json dependency. Reference these DLLs
directly in a standalone .NET host. No Capsule NuGet packages are produced.

The bundle also contains Windows x64 Wasmtime, Capsule bridge and Yoga DLLs in
`native/`, the QuickJS carrier and matching React SDK in `resources/`, and licenses
in `licenses/`. Pass explicit paths to the runtime API. This bundle is not a Godot
or Unreal adapter; those plugins require separate engine integration.

Unity users install the Unity bundle instead. It contains the canonical C# source,
assembly definitions, native binaries, shaders and resources as embedded UPM
packages. Do not install both portable DLLs and equivalent UPM source assemblies
in the same project.

Managed builds use the pinned .NET SDK, locked dependency restore, deterministic
compilation and the release source revision. Verification creates an external
consumer with direct DLL references, an empty package cache and no package feeds,
then exercises real Wasmtime/Yoga lifecycle behavior using freshly packed npm
tooling. See [release workflow](releases.md).

The prebuilt upstream Wasmtime DLL is preserved byte-for-byte. It includes upstream
Cargo build metadata that can contain upstream source paths. Capsule does not
rewrite that binary; replacing it requires a reviewed native upgrade. First-party
managed assemblies omit debug paths and symbols.
