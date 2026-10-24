param(
    [Parameter(Mandatory)][string]$Artifacts,
    [string]$Sdk,
    [string]$AddonBundle
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
if (![IO.Path]::IsPathFullyQualified($Artifacts)) { throw 'Artifacts must be an absolute path.' }
if ([bool]$Sdk -ne [bool]$AddonBundle) { throw 'Provide both SDK and compiled addon bundle for lifecycle mode.' }
foreach ($resource in @($Sdk, $AddonBundle)) {
    if ($resource -and (![IO.Path]::IsPathFullyQualified($resource) -or !(Test-Path -LiteralPath $resource))) { throw "Expected absolute existing resource: $resource" }
}
$Artifacts = [IO.Path]::GetFullPath($Artifacts)
$manifestPath = Join-Path $Artifacts 'nuget-manifest.json'
if (!(Test-Path -LiteralPath $manifestPath)) { throw 'Missing nuget-manifest.json.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.version -ne '0.1.0' -or $manifest.revision -cnotmatch '^[0-9a-f]{40}$') { throw 'Invalid release manifest header.' }
$expected = [ordered]@{
    'Capsule.Core' = @{}
    'Capsule.Package' = @{ 'Capsule.Core'='[0.1.0]'; 'Newtonsoft.Json'='[13.0.2]' }
    'Capsule.Wasmtime' = @{ 'Capsule.Core'='[0.1.0]' }
    'Capsule.Yoga' = @{ 'Capsule.Core'='[0.1.0]' }
    'Rpg.Features' = @{ 'Capsule.Core'='[0.1.0]' }
    'Mmo.Features' = @{ 'Capsule.Core'='[0.1.0]' }
    'SkillSystem.Features' = @{ 'Capsule.Core'='[0.1.0]' }
}
if (@($manifest.packages).Count -ne $expected.Count) { throw 'Expected seven package records.' }
foreach ($package in $manifest.packages) {
    $id = [string]$package.id
    if (!$expected.Contains($id)) { throw "Unexpected package $id" }
    $file = "$id.0.1.0.nupkg"
    if ($package.file -cne $file -or $package.version -ne '0.1.0') { throw "Invalid package identity $id" }
    $path = Join-Path $Artifacts $file
    if (!(Test-Path -LiteralPath $path)) { throw "Missing $file" }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -cne $package.sha256) { throw "Hash mismatch $file" }
    $archive = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $names = @($archive.Entries | ForEach-Object FullName)
        if (@($names | Select-Object -Unique).Count -ne $names.Count) { throw "Repeated ZIP entry in $file" }
        if (@($names | Where-Object { $_.StartsWith('/') -or $_ -match '(^|/)\.\.(/|$)' -or $_.Contains('\') }).Count) { throw "Unsafe ZIP path in $file" }
        $nuspec = @($archive.Entries | Where-Object { $_.FullName -eq "$id.nuspec" })
        if ($nuspec.Count -ne 1) { throw "Missing specification in $file" }
        $reader = [IO.StreamReader]::new($nuspec[0].Open())
        try { $specText = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $spec = [Xml.XmlDocument]::new(); $spec.LoadXml($specText)
        $ns = [Xml.XmlNamespaceManager]::new($spec.NameTable); $ns.AddNamespace('n', $spec.DocumentElement.NamespaceURI)
        $metadata = $spec.SelectSingleNode('//n:metadata', $ns)
        if ($metadata.SelectSingleNode('n:id', $ns).InnerText -cne $id -or $metadata.SelectSingleNode('n:version', $ns).InnerText -ne '0.1.0') { throw "Nuspec identity mismatch $file" }
        if ($metadata.SelectSingleNode('n:license', $ns).InnerText -ne 'Apache-2.0') { throw "License mismatch $file" }
        $repository = $metadata.SelectSingleNode('n:repository', $ns)
        if ($repository.GetAttribute('url') -ne 'https://github.com/ctxshift/capsule' -or $repository.GetAttribute('commit') -ne $manifest.revision) { throw "Repository metadata mismatch $file" }
        $deps = @{}
        foreach ($dependency in $spec.SelectNodes('//n:dependency', $ns)) {
            $depId = $dependency.GetAttribute('id')
            if ($deps.ContainsKey($depId)) { throw "Repeated dependency $depId" }
            $deps[$depId] = $dependency.GetAttribute('version')
        }
        if ($deps.Count -ne $expected[$id].Count) { throw "Dependency count mismatch $file" }
        foreach ($depId in $expected[$id].Keys) { if ($deps[$depId] -cne $expected[$id][$depId]) { throw "Dependency mismatch $file $depId" } }
        $manifestDeps = @{}
        foreach ($property in $package.dependencies.PSObject.Properties) { $manifestDeps[$property.Name] = [string]$property.Value }
        if ($manifestDeps.Count -ne $deps.Count) { throw "Manifest dependency count mismatch $file" }
        foreach ($depId in $deps.Keys) { if ($manifestDeps[$depId] -cne $deps[$depId]) { throw "Manifest dependency mismatch $file $depId" } }
        $declared = @{}
        foreach ($asset in $package.payload) { $declared[[string]$asset.path] = [string]$asset.sha256 }
        $payloadEntries = @($archive.Entries | Where-Object { $_.FullName -match '^(runtimes|contentFiles|licenses)/' -and !$_.FullName.EndsWith('/') })
        if ($payloadEntries.Count -ne $declared.Count) { throw "Payload count mismatch $file" }
        foreach ($entry in $payloadEntries) {
            $memory = [IO.MemoryStream]::new()
            try { $stream = $entry.Open(); try { $stream.CopyTo($memory) } finally { $stream.Dispose() }; $assetHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($memory.ToArray())).ToLowerInvariant() } finally { $memory.Dispose() }
            if ($declared[$entry.FullName] -cne $assetHash) { throw "Payload hash mismatch $file $($entry.FullName)" }
        }
        if (!$names.Contains("lib/netstandard2.1/$id.dll") -or !$names.Contains('licenses/LICENSE')) { throw "Missing assembly or license $file" }
        if ($id -eq 'Capsule.Wasmtime') {
            foreach ($asset in @('runtimes/win-x64/native/wasmtime.dll','runtimes/win-x64/native/portable_ui_wasmtime.dll','contentFiles/any/any/Capsule/Wasmtime/Carrier/CapsuleQuickJs.bytes','contentFiles/any/any/Capsule/Wasmtime/Resources/CapsuleReactSdk.txt')) {
                if (!$names.Contains($asset)) { throw "Missing Wasmtime asset $asset" }
            }
        }
        if ($id -eq 'Capsule.Yoga' -and !$names.Contains('runtimes/win-x64/native/portable_ui_yoga.dll')) { throw 'Missing Yoga native library.' }
    } finally { $archive.Dispose() }
    Write-Output "Verified $file"
}

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$logs = Join-Path $repo '.local/release-native'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('capsule-nuget-' + [Guid]::NewGuid().ToString('N'))
$feed = Join-Path $scratch 'feed'; $consumer = Join-Path $scratch 'consumer'; $cache = Join-Path $scratch 'cache'
New-Item -ItemType Directory -Path $feed,$consumer,$cache -Force | Out-Null
foreach ($package in $manifest.packages) { Copy-Item -LiteralPath (Join-Path $Artifacts $package.file) -Destination $feed }
$feedXml = [Security.SecurityElement]::Escape($feed)
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /><add key="release" value="$feedXml" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>
"@ | Set-Content -LiteralPath (Join-Path $scratch 'NuGet.Config') -Encoding utf8NoBOM
$references = ($expected.Keys | ForEach-Object { "    <PackageReference Include=`"$_`" Version=`"[0.1.0]`" />" }) -join "`n"
@"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup>
$references
</ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $consumer 'Consumer.csproj') -Encoding utf8NoBOM
@'
using System;
using Rpg.Features;
using Mmo.Features;
using SkillSystem.Features;

var prerequisites = new TQuestPrerequisites(1, "root", new[] { new TQuestConditionNode("root", "always", "", Array.Empty<string>()) });
if (RpgFeatureCodec.DecodeQuestPrerequisites(RpgFeatureCodec.Encode(prerequisites)).root != "root") throw new Exception("RPG codec failed");
var chat = new TChatSend(1, 2, "party", "hello", "");
if (MmoFeatureCodec.DecodeChatSend(MmoFeatureCodec.Encode(chat)).text != "hello") throw new Exception("MMO codec failed");
var training = new TTrainingIntent(1, 2, 3, "navigation-2", false);
if (SkillSystemFeatureCodec.DecodeTrainingIntent(SkillSystemFeatureCodec.Encode(training)).offer != "navigation-2") throw new Exception("Skill codec failed");
foreach (var type in new[] { typeof(Capsule.UiSession), typeof(Capsule.Package.UiPackageManifest), typeof(Capsule.Wasmtime.WasmtimeUiGuest), typeof(Capsule.Yoga.NativeYoga), typeof(TQuestPrerequisites), typeof(TChatSend), typeof(TTrainingIntent) })
    if (type.Assembly.GetName().Name == null) throw new Exception("Missing assembly");
Console.WriteLine("PASS packaged provider roundtrips");
'@ | Set-Content -LiteralPath (Join-Path $consumer 'Program.cs') -Encoding utf8NoBOM
$env:NUGET_PACKAGES = $cache
& dotnet restore (Join-Path $consumer 'Consumer.csproj') --configfile (Join-Path $scratch 'NuGet.Config') --packages $cache --nologo *> (Join-Path $logs 'verify-restore.log')
if ($LASTEXITCODE -ne 0) { throw "Isolated package restore failed; see $(Join-Path $logs 'verify-restore.log')" }
& dotnet run --project (Join-Path $consumer 'Consumer.csproj') --no-restore -c Release --nologo *> (Join-Path $logs 'verify-consumer.log')
if ($LASTEXITCODE -ne 0) { throw "Packaged consumer failed; see $(Join-Path $logs 'verify-consumer.log')" }
Set-Content -LiteralPath (Join-Path $logs 'verify-scratch.txt') -Value $scratch -Encoding utf8NoBOM
Write-Output "Packaged consumer passed; retained scratch: $scratch"

$nativeProject = Join-Path $scratch 'native-consumer'
New-Item -ItemType Directory -Path $nativeProject -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repo 'tools/capsule-native-tests') -Filter '*.cs' -File | Copy-Item -Destination $nativeProject
$nativeReferences = @('Capsule.Package','Capsule.Wasmtime','Capsule.Yoga') | ForEach-Object { "    <PackageReference Include=`"$_`" Version=`"[0.1.0]`" />" }
@"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>
$($nativeReferences -join "`n")
</ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $nativeProject 'NativeConsumer.csproj') -Encoding utf8NoBOM
& dotnet restore (Join-Path $nativeProject 'NativeConsumer.csproj') --configfile (Join-Path $scratch 'NuGet.Config') --packages $cache --nologo *> (Join-Path $logs 'verify-native-restore.log')
if ($LASTEXITCODE -ne 0) { throw "Native consumer restore failed; see $(Join-Path $logs 'verify-native-restore.log')" }
& dotnet publish (Join-Path $nativeProject 'NativeConsumer.csproj') -c Release -o (Join-Path $scratch 'host') --no-restore --nologo *> (Join-Path $logs 'verify-native-publish.log')
if ($LASTEXITCODE -ne 0) { throw "Native consumer publish failed; see $(Join-Path $logs 'verify-native-publish.log')" }
$resources = Join-Path $scratch 'resources'
New-Item -ItemType Directory -Path $resources -Force | Out-Null
$wasmtimePackage = Join-Path $Artifacts 'Capsule.Wasmtime.0.1.0.nupkg'
$yogaPackage = Join-Path $Artifacts 'Capsule.Yoga.0.1.0.nupkg'
function Copy-PackageEntry([string]$packagePath, [string]$entryName, [string]$destination) {
    $zip = [IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $entry = $zip.GetEntry($entryName)
        if (!$entry) { throw "Missing packaged entry $entryName" }
        $source = $entry.Open(); $target = [IO.File]::Create($destination)
        try { $source.CopyTo($target) } finally { $source.Dispose(); $target.Dispose() }
    } finally { $zip.Dispose() }
}
Copy-PackageEntry $wasmtimePackage 'runtimes/win-x64/native/wasmtime.dll' (Join-Path $resources 'wasmtime.dll')
Copy-PackageEntry $wasmtimePackage 'runtimes/win-x64/native/portable_ui_wasmtime.dll' (Join-Path $resources 'bridge.dll')
Copy-PackageEntry $wasmtimePackage 'contentFiles/any/any/Capsule/Wasmtime/Carrier/CapsuleQuickJs.bytes' (Join-Path $resources 'carrier.bytes')
Copy-PackageEntry $yogaPackage 'runtimes/win-x64/native/portable_ui_yoga.dll' (Join-Path $resources 'yoga.dll')
if ($Sdk) { Copy-Item -LiteralPath $Sdk -Destination (Join-Path $resources 'sdk.js'); Copy-Item -LiteralPath $AddonBundle -Destination (Join-Path $resources 'bundle.js') }
$runtimeArgs = @('wasmtime.dll','bridge.dll','yoga.dll','carrier.bytes') | ForEach-Object { Join-Path $resources $_ }
$hostDll = Join-Path $scratch 'host/NativeConsumer.dll'
Push-Location $scratch
try {
    & dotnet $hostDll @runtimeArgs *> (Join-Path $logs 'verify-native-smoke.log')
    if ($LASTEXITCODE -ne 0) { throw "Packaged native smoke failed; see $(Join-Path $logs 'verify-native-smoke.log')" }
    if ($Sdk) {
        & dotnet $hostDll @runtimeArgs (Join-Path $resources 'sdk.js') (Join-Path $resources 'bundle.js') *> (Join-Path $logs 'verify-native-lifecycle.log')
        if ($LASTEXITCODE -ne 0) { throw "Packaged native lifecycle failed; see $(Join-Path $logs 'verify-native-lifecycle.log')" }
    }
} finally { Pop-Location }
Write-Output "Packaged native smoke passed; lifecycle $(if ($Sdk) {'passed'} else {'not requested'})."
