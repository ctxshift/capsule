param([Parameter(Mandatory)][string]$Artifacts)

$ErrorActionPreference = 'Stop'
if (![IO.Path]::IsPathFullyQualified($Artifacts)) { throw 'Artifacts must be an absolute directory.' }
$Artifacts = [IO.Path]::GetFullPath($Artifacts)
$packages = @(
    'com.capsule.core', 'com.capsule.package', 'com.capsule.wasmtime', 'com.capsule.yoga',
    'com.capsule.unity', 'com.capsule.features.rpg', 'com.capsule.features.mmo', 'com.capsule.features.skill-system'
)
foreach ($name in $packages) {
    if (!(Test-Path -LiteralPath (Join-Path $Artifacts "$name-0.1.0.tgz"))) { throw "Missing packed UPM archive: $name" }
}
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$logs = Join-Path $repo '.local/release-upm'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('capsule-upm-' + [Guid]::NewGuid().ToString('N'))
$project = Join-Path $scratch 'project'
$archives = Join-Path $project 'Packages/archives'
$testDir = Join-Path $project 'Assets/Tests/Editor'
New-Item -ItemType Directory -Path $archives,$testDir,(Join-Path $project 'ProjectSettings') -Force | Out-Null
foreach ($name in $packages) { Copy-Item -LiteralPath (Join-Path $Artifacts "$name-0.1.0.tgz") -Destination $archives }
@'
m_EditorVersion: 6000.6.3f1
m_EditorVersionWithRevision: 6000.6.3f1 (45d8eee7de74)
'@ | Set-Content -LiteralPath (Join-Path $project 'ProjectSettings/ProjectVersion.txt') -Encoding utf8NoBOM
$dependencies = [ordered]@{}
foreach ($name in $packages) { $dependencies[$name] = "file:archives/$name-0.1.0.tgz" }
$dependencies['com.unity.inputsystem'] = '1.19.0'
$dependencies['com.unity.ugui'] = '2.6.0'
$dependencies['com.unity.nuget.newtonsoft-json'] = '3.2.2'
$dependencies['com.unity.test-framework'] = '1.8.0'
$dependencies['com.unity.pipeline'] = '0.8.0-exp.1'
[ordered]@{ dependencies=$dependencies } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $project 'Packages/manifest.json') -Encoding utf8NoBOM
@'
{
  "name": "Capsule.Release.Tests",
  "rootNamespace": "Capsule.Release.Tests",
  "references": [
    "Capsule.Core", "Capsule.Package", "Capsule.Wasmtime", "Capsule.Yoga",
    "Capsule.Unity", "Rpg.Features", "Mmo.Features", "SkillSystem.Features"
  ],
  "optionalUnityReferences": ["TestAssemblies"],
  "autoReferenced": false,
  "includePlatforms": ["Editor"]
}
'@ | Set-Content -LiteralPath (Join-Path $testDir 'Capsule.Release.Tests.asmdef') -Encoding utf8NoBOM
@'
using System;
using NUnit.Framework;

namespace Capsule.Release.Tests
{
    public sealed class CapsuleReleaseProbe
    {
        [Test]
        public void AllPackedAssembliesImportAndExposeContracts()
        {
            var types = new[] {
                typeof(Capsule.UiSession),
                typeof(Capsule.Package.UiPackageManifest),
                typeof(Capsule.Wasmtime.WasmtimeUiGuest),
                typeof(Capsule.Yoga.NativeYoga),
                typeof(Capsule.Unity.CapsuleSurfaceView),
                typeof(Rpg.Features.TQuestPrerequisites),
                typeof(Mmo.Features.TChatSend),
                typeof(SkillSystem.Features.TTrainingIntent)
            };
            var assemblies = new[] {
                "Capsule.Core", "Capsule.Package", "Capsule.Wasmtime", "Capsule.Yoga",
                "Capsule.Unity", "Rpg.Features", "Mmo.Features", "SkillSystem.Features"
            };
            Assert.AreEqual(assemblies.Length, types.Length);
            for (var i = 0; i < types.Length; i++)
                Assert.AreEqual(assemblies[i], types[i].Assembly.GetName().Name);
        }
    }
}
'@ | Set-Content -LiteralPath (Join-Path $testDir 'CapsuleReleaseProbe.cs') -Encoding utf8NoBOM
Set-Content -LiteralPath (Join-Path $logs 'verify-scratch.txt') -Value $scratch -Encoding utf8NoBOM

& unity status --project-path $project --format json --non-interactive *> (Join-Path $logs 'verify-status.json')
$statusText = Get-Content -LiteralPath (Join-Path $logs 'verify-status.json') -Raw
if ($statusText -match '"projectPath"\s*:\s*"' -and $statusText -match '"ready"\s*:\s*true') { throw 'Fresh consumer project is already open; refusing headless validation.' }
& agent-unity test-headless editmode CapsuleReleaseProbe --project-path $project --timeout 600 --logs-dir $logs --json *> (Join-Path $logs 'verify-headless.json')
if ($LASTEXITCODE -ne 0) { throw "Packed UPM EditMode validation failed; see $(Join-Path $logs 'verify-headless.json') and retained helper logs." }
$result = Get-Content -LiteralPath (Join-Path $logs 'verify-headless.json') -Raw
if ($result -notmatch 'CapsuleReleaseProbe' -or $result -notmatch 'passed') { throw 'EditMode helper returned without an identifiable passing probe.' }
Write-Output "Packed UPM EditMode probe passed; retained consumer: $project"
