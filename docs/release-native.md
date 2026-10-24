# Native NuGet release candidate

The managed Capsule surface ships as seven coordinated NuGet packages at version `0.1.0`: `Capsule.Core`, `Capsule.Package`, `Capsule.Wasmtime`, `Capsule.Yoga`, `Rpg.Features`, `Mmo.Features`, and `SkillSystem.Features`. All target `netstandard2.1`. The core package has no package dependencies. Each other package depends on the exact `Capsule.Core` version; `Capsule.Package` also depends on exact `Newtonsoft.Json` version `13.0.2`. Their managed assembly names match their package IDs.

On a Windows build host with PowerShell 7 and the repository's .NET SDK, pack from the repository root:

```powershell
$revision = (git rev-parse HEAD).Trim()
pwsh -NoProfile -File tools/release/pack-nuget.ps1 -Output (Join-Path (Get-Location) '.local/release-native/artifacts') -Revision $revision -Version 0.1.0
pwsh -NoProfile -File tools/release/verify-nuget.ps1 -Artifacts (Join-Path (Get-Location) '.local/release-native/artifacts')
```

The output directory must be empty. Packing uses locked restores and serial builds, stamps the full repository revision, makes NuGet dependency ranges exact, and canonicalizes ZIP entry order and timestamps. `nuget-manifest.json` records the package SHA-256 hashes, dependency ranges, and hashes for native/resource/license payloads. Compare packages and the manifest from two fresh output directories before publication. The scripts retain diagnostics in `.local/release-native/`.

`Capsule.Wasmtime` includes the Windows x64 Wasmtime and Capsule bridge DLLs under `runtimes/win-x64/native/`, the QuickJS carrier under `contentFiles/any/any/Capsule/Wasmtime/Carrier/`, and shipped SDK/fixture resources under `contentFiles/any/any/Capsule/Wasmtime/Resources/`. `Capsule.Yoga` includes its Windows x64 native DLL under `runtimes/win-x64/native/`. Both include their applicable upstream notices and provenance under `licenses/`; every package includes the Apache-2.0 repository license. Hosts must pass explicit resource paths to the runtime APIs and deploy the native libraries for their target platform. These packages carry Windows x64 native binaries only.

The verifier checks package identities, exact dependency ranges, payload hashes, repository revision, license, and required runtime assets. It then creates a consumer outside the repository using only the built `.nupkg` files and a fresh NuGet cache. It runs provider codec roundtrips and the real Wasmtime/Yoga native smoke test. For the full addon lifecycle proof, provide a freshly packed SDK and compiled addon bundle:

```powershell
pwsh -NoProfile -File tools/release/verify-nuget.ps1 -Artifacts <absolute-artifact-directory> -Sdk <absolute-sdk-js> -AddonBundle <absolute-addon-js>
```

The verifier retains its external consumer and logs for inspection. Publishing the verified artifacts to a release download location is a separate operation.

The prebuilt upstream Wasmtime DLL is preserved byte-for-byte. Its embedded upstream build metadata may contain the upstream builder's source path; this does not come from the Capsule build. Inspect or replace that upstream binary through a separately reviewed native upgrade, rather than rewriting a signed-off runtime payload during managed packaging.
