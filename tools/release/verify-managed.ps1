param([Parameter(Mandatory)][string]$Artifacts, [string]$Sdk, [string]$AddonBundle)
$ErrorActionPreference = 'Stop'
if (![IO.Path]::IsPathFullyQualified($Artifacts)) { throw 'Artifacts must be absolute.' }
if ([bool]$Sdk -ne [bool]$AddonBundle) { throw 'Provide SDK and addon together.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$logs = Join-Path $repo '.local/release-native'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('capsule-dll-consumer-' + [Guid]::NewGuid().ToString('N'))
$consumer = Join-Path $scratch 'consumer'
New-Item -ItemType Directory -Path $consumer -Force | Out-Null
$libraries = Join-Path $scratch 'libraries'
Copy-Item -LiteralPath $Artifacts -Destination $libraries -Recurse
$names = @('Capsule.Core','Capsule.Package','Capsule.Wasmtime','Capsule.Yoga','Rpg.Features','Mmo.Features','SkillSystem.Features','Newtonsoft.Json')
$refs = foreach ($name in $names) {
  if (!(Test-Path -LiteralPath (Join-Path $libraries "managed/$name.dll"))) { throw "Missing library $name" }
  "<Reference Include=`"$name`"><HintPath>../libraries/managed/$name.dll</HintPath></Reference>"
}
@"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>
$($refs -join "`n")
</ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $consumer 'Consumer.csproj') -Encoding utf8NoBOM
Get-ChildItem -LiteralPath (Join-Path $repo 'tools/capsule-native-tests') -Filter '*.cs' -File | Copy-Item -Destination $consumer
'<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath (Join-Path $scratch 'NuGet.Config') -Encoding utf8NoBOM
& dotnet restore (Join-Path $consumer 'Consumer.csproj') --configfile (Join-Path $scratch 'NuGet.Config') --packages (Join-Path $scratch 'empty-cache') --nologo *> (Join-Path $logs 'verify-restore.log')
if ($LASTEXITCODE -ne 0) { throw 'DLL consumer restore failed; see retained log.' }
& dotnet publish (Join-Path $consumer 'Consumer.csproj') -c Release --no-restore -o (Join-Path $scratch 'host') --nologo *> (Join-Path $logs 'verify-publish.log')
if ($LASTEXITCODE -ne 0) { throw 'DLL consumer publish failed; see retained log.' }
$runtimeArgs = @('native/wasmtime.dll','native/portable_ui_wasmtime.dll','native/portable_ui_yoga.dll','resources/CapsuleQuickJs.bytes') | ForEach-Object { Join-Path $libraries $_ }
Push-Location $scratch
try {
  & dotnet (Join-Path $scratch 'host/Consumer.dll') @runtimeArgs *> (Join-Path $logs 'verify-native-smoke.log')
  if ($LASTEXITCODE -ne 0) { throw 'DLL native smoke failed; see retained log.' }
  if ($Sdk) {
    & dotnet (Join-Path $scratch 'host/Consumer.dll') @runtimeArgs $Sdk $AddonBundle *> (Join-Path $logs 'verify-native-lifecycle.log')
    if ($LASTEXITCODE -ne 0) { throw 'DLL native lifecycle failed; see retained log.' }
  }
} finally { Pop-Location }
Set-Content -LiteralPath (Join-Path $logs 'verify-scratch.txt') -Value $scratch -Encoding utf8NoBOM
Write-Output 'PASS external DLL-only native consumer; no package feed or source project references.'
