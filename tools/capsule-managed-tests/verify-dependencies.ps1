param([string]$Output)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ([string]::IsNullOrEmpty($Output)) {
    $run = 'dependency-proof-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N')
    $Output = Join-Path '.local/capsule-managed-validation' $run
}
$out = [IO.Path]::GetFullPath((Join-Path $repo $Output))
if (Test-Path -LiteralPath $out) { throw 'Use a fresh dependency-proof output directory.' }
New-Item -ItemType Directory -Path $out | Out-Null
function Xml([string]$value) { [Security.SecurityElement]::Escape($value) }
function Build([string]$project, [string]$log, [bool]$mustPass) {
    & dotnet build $project --nologo --verbosity minimal *> $log
    $code = $LASTEXITCODE
    if ($mustPass -and $code -ne 0) { throw "Build failed; see $log" }
    if (!$mustPass -and ($code -eq 0 -or !(Select-String -LiteralPath $log -SimpleMatch 'CAPSULE_DEPENDENCY:' -Quiet))) {
        throw "Expected dependency guard rejection; see $log"
    }
}
$stubs = @{}
foreach ($name in @('UnityEngine.Probe', 'Host.Probe')) {
    $dir = Join-Path $out $name
    New-Item -ItemType Directory -Path $dir | Out-Null
    $project = Join-Path $dir 'Probe.csproj'
    Set-Content -LiteralPath $project -Value "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><AssemblyName>$name</AssemblyName></PropertyGroup></Project>"
    Set-Content -LiteralPath (Join-Path $dir 'Probe.cs') -Value "namespace $name { public sealed class HostType { } }"
    Build $project (Join-Path $out "$name-build.log") $true
    $stubs[$name] = Join-Path $dir "bin/Debug/netstandard2.1/$name.dll"
}
$count = 0
foreach ($library in @('Core', 'Package', 'Wasmtime', 'Yoga')) {
    foreach ($name in @('UnityEngine.Probe', 'Host.Probe')) {
        $dir = Join-Path $out "$library-$name"
        New-Item -ItemType Directory -Path $dir | Out-Null
        $props = Xml (Join-Path $repo 'packages/capsule-native/Directory.Build.props')
        $targets = Xml (Join-Path $repo 'packages/capsule-native/Directory.Build.targets')
        $sources = Xml (Join-Path $repo "unity/Packages/com.capsule.$($library.ToLowerInvariant())/Runtime/*.cs")
        $hint = Xml $stubs[$name]
        $json = if ($library -eq 'Package') { '<PackageReference Include="Newtonsoft.Json" Version="[13.0.2]" />' } else { '' }
        $allow = if ($library -eq 'Package') { 'true' } else { 'false' }
        $runtime = $library -in @('Wasmtime', 'Yoga')
        $allowCore = if ($runtime) { 'true' } else { 'false' }
        $corePath = Xml (Join-Path $repo 'packages/capsule-native/Core/Capsule.Core.csproj')
        $coreReference = if ($runtime) { "<ProjectReference Include=`"$corePath`" />" } else { '' }
        $project = Join-Path $dir 'Probe.csproj'
        Set-Content -LiteralPath $project -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$props" />
  <PropertyGroup><CapsuleAllowNewtonsoft>$allow</CapsuleAllowNewtonsoft><CapsuleAllowCore>$allowCore</CapsuleAllowCore></PropertyGroup>
  <ItemGroup><Compile Include="$sources" />$json$coreReference<Reference Include="$name"><HintPath>$hint</HintPath></Reference></ItemGroup>
  <Import Project="$targets" />
</Project>
"@
        Build $project (Join-Path $out "$library-$name-rejected.log") $false
        $count++
    }
}
# Project dependencies are rejected independently of the assembly whitelist.
$projectDir = Join-Path $out 'Core-HostProject'
New-Item -ItemType Directory -Path $projectDir | Out-Null
$coreSources = Xml (Join-Path $repo 'unity/Packages/com.capsule.core/Runtime/*.cs')
$hostProject = Xml (Join-Path $out 'Host.Probe/Probe.csproj')
$project = Join-Path $projectDir 'Probe.csproj'
Set-Content -LiteralPath $project -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$props" />
  <ItemGroup><Compile Include="$coreSources" /><ProjectReference Include="$hostProject" /></ItemGroup>
  <Import Project="$targets" />
</Project>
"@
Build $project (Join-Path $out 'Core-HostProject-rejected.log') $false
$count++
# Even the approved Package dependency is forbidden in Core.
$packageDir = Join-Path $out 'Core-UnapprovedPackage'
New-Item -ItemType Directory -Path $packageDir | Out-Null
$project = Join-Path $packageDir 'Probe.csproj'
Set-Content -LiteralPath $project -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$props" />
  <ItemGroup><Compile Include="$coreSources" /><PackageReference Include="Newtonsoft.Json" Version="[13.0.2]" /></ItemGroup>
  <Import Project="$targets" />
</Project>
"@
Build $project (Join-Path $out 'Core-UnapprovedPackage-rejected.log') $false
$count++
Set-Content -LiteralPath (Join-Path $out 'result.json') -Value (@{ passed = $count; sourceMutations = $false } | ConvertTo-Json)
Write-Output "Capsule dependency rejection checks passed: $count; logs: $out"
