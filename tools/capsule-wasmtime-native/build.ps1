param([switch]$Install)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$archive=Join-Path $root '.local/wasmtime-c-api.zip'
$api=Join-Path $root '.local/wasmtime-c-api/wasmtime-v48.0.2-x86_64-windows-c-api'
$archiveHash='0AAC55C8A8409A33920158F37F82A01AD1E8DE757369364BFBD48CD1B834754E'
$runtimeHash='19B3880FBB92F1826C58AC9F5E2A472692B5FC3D012A1A7A41983CCF77544607'
if((& zig version) -ne '0.16.0'){throw 'Requires pinned Zig 0.16.0.'}
if(!(Test-Path -LiteralPath $archive)){Invoke-WebRequest 'https://github.com/bytecodealliance/wasmtime/releases/download/v48.0.2/wasmtime-v48.0.2-x86_64-windows-c-api.zip' -OutFile $archive}
if((Get-FileHash -LiteralPath $archive).Hash -ne $archiveHash){throw 'C API archive hash mismatch.'}
if(!(Test-Path -LiteralPath $api)){Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $root '.local/wasmtime-c-api')}
$runtime=Join-Path $api 'lib/wasmtime.dll'
if((Get-FileHash -LiteralPath $runtime).Hash -ne $runtimeHash){throw 'Pinned runtime DLL hash mismatch.'}
$output=Join-Path $root '.local/portable_ui_wasmtime.dll'
$argsNative=@('cc','-target','x86_64-windows-gnu','-O2','-shared','-I',(Join-Path $api 'include'),(Join-Path $PSScriptRoot 'bridge.c'),(Join-Path $api 'lib/wasmtime.dll.lib'),'-o',$output)
& zig @argsNative
if($LASTEXITCODE -ne 0){throw 'C ABI bridge build failed.'}
[ordered]@{version='48.0.2';compiler='Zig 0.16.0';archiveSha256=$archiveHash;runtimeSha256=$runtimeHash;bridgeSha256=(Get-FileHash $output).Hash;sourceSha256=(Get-FileHash (Join-Path $PSScriptRoot 'bridge.c')).Hash;metricsHeaderSha256=(Get-FileHash (Join-Path $PSScriptRoot 'bridge-metrics.h')).Hash;arguments=$argsNative} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $root '.local/wasmtime-native-build.json')
if($Install){
 $destination=Join-Path $root 'unity/Packages/com.capsule.wasmtime/Runtime/Plugins/x86_64'
 New-Item -ItemType Directory -Force $destination | Out-Null
 Copy-Item -LiteralPath $output -Destination (Join-Path $destination 'portable_ui_wasmtime.dll') -Force
 Copy-Item -LiteralPath $runtime -Destination (Join-Path $destination 'wasmtime.dll') -Force
}
Write-Output "C ABI bridge built: $output"
