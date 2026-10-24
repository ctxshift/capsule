param(
    [Parameter(Mandatory)][string]$EngineLibrary,
    [Parameter(Mandatory)][string]$BridgeLibrary,
    [Parameter(Mandatory)][string]$YogaLibrary,
    [Parameter(Mandatory)][string]$Carrier,
    [string]$Sdk,
    [string]$AddonBundle,
    [Parameter(Mandatory)][string]$Output
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ([bool]$Sdk -ne [bool]$AddonBundle) { throw 'Provide both SDK and compiled addon bundle for lifecycle mode.' }
$paths = @($EngineLibrary, $BridgeLibrary, $YogaLibrary, $Carrier, $Output)
if ($Sdk) { $paths += @($Sdk, $AddonBundle) }
foreach ($path in $paths) {
    if (![IO.Path]::IsPathFullyQualified($path)) { throw "Absolute path required: $path" }
}
if (Test-Path -LiteralPath $Output) { throw 'Use a fresh isolated output directory.' }
$resources = Join-Path $Output 'resources'
New-Item -ItemType Directory -Path $resources | Out-Null
$inputs = @{
    'wasmtime.dll' = $EngineLibrary
    'bridge.dll' = $BridgeLibrary
    'yoga.dll' = $YogaLibrary
    'carrier.bytes' = $Carrier
}
if ($Sdk) { $inputs['sdk.js'] = $Sdk; $inputs['bundle.js'] = $AddonBundle }
foreach ($entry in $inputs.GetEnumerator()) {
    Copy-Item -LiteralPath $entry.Value -Destination (Join-Path $resources $entry.Key)
}
& dotnet publish (Join-Path $PSScriptRoot 'Capsule.Native.Tests.csproj') -c Release -o (Join-Path $Output 'host') --nologo *> (Join-Path $Output 'publish.log')
if ($LASTEXITCODE -ne 0) { throw 'Host publication failed; see publish.log.' }
$runtimeArgs = @('wasmtime.dll', 'bridge.dll', 'yoga.dll', 'carrier.bytes') | ForEach-Object { Join-Path $resources $_ }
function RunProof([string]$name, [string[]]$arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.WorkingDirectory = $Output
    $start.ArgumentList.Add((Join-Path $Output 'host/Capsule.Native.Tests.dll'))
    foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(120000)) { $process.Kill($true); throw "$name exceeded the 120-second deadline." }
    Set-Content -LiteralPath (Join-Path $Output "$name.log") -Value ($stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult())
    if ($process.ExitCode -ne 0) { throw "$name failed; see $name.log." }
    Write-Output "$name passed; retained log: $(Join-Path $Output "$name.log")"
    $process.Dispose()
}
RunProof 'smoke' $runtimeArgs
if ($Sdk) { RunProof 'lifecycle' ($runtimeArgs + @((Join-Path $resources 'sdk.js'), (Join-Path $resources 'bundle.js'))) }
$hashes = Get-ChildItem -LiteralPath $resources, (Join-Path $Output 'host') -File |
    ForEach-Object { @{ file = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } }
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'hashes.json')
