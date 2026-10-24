param([Parameter(Mandatory)][string]$Output, [Parameter(Mandatory)][string]$Revision)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (![IO.Path]::IsPathFullyQualified($Output) -or $Revision -cnotmatch '^[a-f0-9]{40}$') { throw 'Expected absolute output and full source revision.' }
if (Test-Path -LiteralPath $Output) { throw 'Use a fresh output directory.' }
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$logs = Join-Path $repo '.local/release-native'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$plans = [ordered]@{
  'Capsule.Core'='packages/capsule-native/Core/Capsule.Core.csproj'
  'Capsule.Package'='packages/capsule-native/Package/Capsule.Package.csproj'
  'Capsule.Wasmtime'='packages/capsule-native/Wasmtime/Capsule.Wasmtime.csproj'
  'Capsule.Yoga'='packages/capsule-native/Yoga/Capsule.Yoga.csproj'
  'Rpg.Features'='packages/rpg-features/native/Rpg.Features.csproj'
  'Mmo.Features'='packages/mmo-features/native/Mmo.Features.csproj'
  'SkillSystem.Features'='packages/skill-system-features/native/SkillSystem.Features.csproj'
}
foreach ($id in $plans.Keys) {
  $project = Join-Path $repo $plans[$id]
  & dotnet build $project -c Release --nologo --disable-build-servers -p:RestoreLockedMode=true -p:SourceRevisionId=$Revision *> (Join-Path $logs "$id-build.log")
  if ($LASTEXITCODE -ne 0) { throw "Managed build failed: $id; see retained log." }
  $built = Join-Path (Split-Path $project) "bin/Release/netstandard2.1/$id.dll"
  Copy-Item -LiteralPath $built -Destination $Output
}
$jsonProject = Join-Path $repo $plans['Capsule.Package']
& dotnet publish $jsonProject -c Release --no-restore --nologo -p:CopyLocalLockFileAssemblies=true *> (Join-Path $logs 'dependency-publish.log')
if ($LASTEXITCODE -ne 0) { throw 'Managed dependency publish failed.' }
Copy-Item -LiteralPath (Join-Path (Split-Path $jsonProject) 'bin/Release/netstandard2.1/publish/Newtonsoft.Json.dll') -Destination $Output
$assets = Get-Content -LiteralPath (Join-Path (Split-Path $jsonProject) 'obj/project.assets.json') -Raw | ConvertFrom-Json
$notice = @($assets.packageFolders.PSObject.Properties.Name | ForEach-Object { Join-Path $_ 'newtonsoft.json/13.0.2/LICENSE.md' } | Where-Object { Test-Path -LiteralPath $_ })
if ($notice.Count -ne 1) { throw 'Expected one restored Newtonsoft license.' }
$licenseDir = Join-Path (Split-Path $Output) 'licenses'
New-Item -ItemType Directory -Path $licenseDir -Force | Out-Null
Copy-Item -LiteralPath $notice[0] -Destination (Join-Path $licenseDir 'Newtonsoft-LICENSE.md')
Write-Output 'Built seven Capsule managed DLLs and pinned Newtonsoft dependency.'
