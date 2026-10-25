param([Parameter(Mandatory)][string]$Artifacts)
$ErrorActionPreference = 'Stop'
if (![IO.Path]::IsPathFullyQualified($Artifacts)) { throw 'Artifacts must be an absolute release directory.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$logs = Join-Path $repo '.local/release-upm'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$evidencePath = Join-Path $logs 'consumer.json'
& node (Join-Path $PSScriptRoot 'prepare-unity.mjs') --artifacts $Artifacts --output $evidencePath
if ($LASTEXITCODE -ne 0) { throw 'Unity bundle verification/preparation failed.' }
$evidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
$project = $evidence.project
& unity status --project-path $project --format json --non-interactive *> (Join-Path $logs 'verify-status.json')
& agent-unity test-headless editmode CapsuleReleaseProbe --project-path $project --timeout 600 --logs-dir $logs --json *> (Join-Path $logs 'verify-headless.json')
if ($LASTEXITCODE -ne 0) { throw 'Unity bundle EditMode validation failed; see retained helper logs.' }
$result = Get-Content -LiteralPath (Join-Path $logs 'verify-headless.json') -Raw | ConvertFrom-Json
if ($result.status -ne 'pass') { throw 'Unity helper did not report passing tests.' }
[xml]$nunit = Get-Content -LiteralPath $result.details.output -Raw
$run = $nunit.'test-run'
if (!$run -or $run.result -ne 'Passed' -or [int]$run.passed -ne $evidence.expectedTests -or [int]$run.total -ne $evidence.expectedTests -or [int]$run.failed -ne 0 -or [int]$run.inconclusive -ne 0 -or [int]$run.skipped -ne 0) { throw 'Unity results did not pass every expected bundle check.' }
$evidence | Add-Member -NotePropertyName passed -NotePropertyValue ([int]$run.passed)
$evidence | Add-Member -NotePropertyName helper -NotePropertyValue $result
$evidence | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $logs 'verification.json') -Encoding utf8NoBOM
Write-Output "Unity bundle EditMode checks passed; retained consumer: $project"
