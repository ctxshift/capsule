param([string]$UnityPluginDirectory = 'unity/Packages/com.capsule.yoga/Runtime/Plugins/x86_64')
$ErrorActionPreference = 'Stop'
$taskYogaRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$taskYogaTarget = [IO.Path]::GetFullPath((Join-Path $taskYogaRoot $UnityPluginDirectory))
$taskYogaBuild = Join-Path $taskYogaRoot '.local/yoga-build'
$taskYogaProvenance = Get-Content -LiteralPath (Join-Path $taskYogaBuild 'native-build.json') -Raw | ConvertFrom-Json
$taskYogaSource = Join-Path $taskYogaBuild 'portable_ui_yoga.dll'
if ((Get-FileHash -LiteralPath $taskYogaSource).Hash -ne $taskYogaProvenance.dllSha256 -or
    (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'native.cpp')).Hash -ne $taskYogaProvenance.sourceSha256) {
    throw 'Build provenance does not match native source and DLL; rebuild first.'
}
New-Item -ItemType Directory -Path $taskYogaTarget -Force | Out-Null
$taskYogaInstalled = Join-Path $taskYogaTarget 'portable_ui_yoga.dll'
if (!(Test-Path -LiteralPath $taskYogaInstalled) -or
    (Get-FileHash -LiteralPath $taskYogaInstalled).Hash -ne $taskYogaProvenance.dllSha256) {
    Copy-Item -LiteralPath $taskYogaSource -Destination $taskYogaInstalled -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE.yoga.txt') -Destination (Join-Path $taskYogaTarget 'LICENSE.yoga.txt') -Force
$taskYogaMeta = Join-Path $taskYogaTarget 'portable_ui_yoga.dll.meta'
if (!(Test-Path -LiteralPath $taskYogaMeta)) {
    $taskYogaGuid = [guid]::NewGuid().ToString('N')
    @"
fileFormatVersion: 2
guid: $taskYogaGuid
PluginImporter:
  externalObjects: {}
  serializedVersion: 3
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
    Any:
      enabled: 0
      settings: {}
    Editor:
      enabled: 1
      settings:
        CPU: x86_64
        DefaultValueInitialized: true
        OS: Windows
    Win64:
      enabled: 1
      settings:
        CPU: x86_64
  userData:
  assetBundleName:
  assetBundleVariant:
"@ | Set-Content -LiteralPath $taskYogaMeta
}
[ordered]@{
    abi = 2; yoga = $taskYogaProvenance.yoga; commit = $taskYogaProvenance.commit
    compiler = $taskYogaProvenance.compiler; platform = $taskYogaProvenance.platform
    sourceSha256 = $taskYogaProvenance.sourceSha256; dllSha256 = $taskYogaProvenance.dllSha256
    recipe = 'C++20, O2, no RTTI, exceptions enabled; tools/capsule-yoga/build.ps1'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskYogaTarget 'provenance.json')
Write-Output "Installed ABI2 Yoga at $taskYogaTarget; existing GUID preserved."
