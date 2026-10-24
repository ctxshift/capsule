# Capsule managed and native runtime

The four standalone projects compile the canonical C# sources in
`unity/Packages/com.capsule.{core,package,wasmtime,yoga}/Runtime`.
They target .NET Standard 2.1. Core uses only the framework; Package also
uses Core and Newtonsoft.Json 13.0.2; Wasmtime and Yoga use Core.
`Directory.Build.targets` rejects unexpected project, package, and assembly
references. The Unity assemblies compile these same files from their UPM packages.

From the repository root, build and check the portable boundary:

```powershell
dotnet build packages/capsule-native/Core/Capsule.Core.csproj -p:RestoreLockedMode=true
dotnet build packages/capsule-native/Package/Capsule.Package.csproj -p:RestoreLockedMode=true
dotnet build packages/capsule-native/Wasmtime/Capsule.Wasmtime.csproj -p:RestoreLockedMode=true
dotnet build packages/capsule-native/Yoga/Capsule.Yoga.csproj -p:RestoreLockedMode=true
dotnet run --project tools/capsule-core-tests
dotnet run --project tools/capsule-managed-tests
dotnet run --project tools/capsule-package-tests
dotnet run --project tools/capsule-feature-tests
pwsh -NoProfile -File tools/capsule-managed-tests/verify-dependencies.ps1
```

The dependency proof writes retained logs below ignored `.local/` and
requires ten injected host or package references to fail with
`CAPSULE_DEPENDENCY`. This enforces the build dependency boundary; a host
still chooses and validates its own resources.

On Windows x86_64, the native proof uses explicit paths to the pinned Wasmtime,
bridge, Yoga, and carrier assets:

```powershell
$packages = (Resolve-Path unity/Packages).Path
pwsh -NoProfile -File tools/capsule-native-tests/run-isolated.ps1 `
  -EngineLibrary (Join-Path $packages 'com.capsule.wasmtime/Runtime/Plugins/x86_64/wasmtime.dll') `
  -BridgeLibrary (Join-Path $packages 'com.capsule.wasmtime/Runtime/Plugins/x86_64/portable_ui_wasmtime.dll') `
  -YogaLibrary (Join-Path $packages 'com.capsule.yoga/Runtime/Plugins/x86_64/portable_ui_yoga.dll') `
  -Carrier (Join-Path $packages 'com.capsule.wasmtime/Runtime/Carrier/CapsuleQuickJs.bytes') `
  -Output ([IO.Path]::GetFullPath('.local/native-smoke'))
```

Use a fresh output directory. Four resources exercise real guest initialization,
fuel accounting, Yoga layout, and disposal. Add `-Sdk` and `-AddonBundle` with
explicit absolute paths for the neutral React lifecycle proof. The smoke does
not require a React compiler or a game checkout.

`UiAsyncHostSession` owns worker load, candidate commit, acknowledgment, and
promotion. Stale candidates do not commit; failed active sessions retire input.
The host supplies presentation and native resource lifetimes. The runtime has
no implicit source, addon, or Unity project path.