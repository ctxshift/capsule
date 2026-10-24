param([switch]$Verify)
$ErrorActionPreference = 'Stop'
function Get-Sha256([string]$path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($path)
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
}
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$source = Join-Path $PSScriptRoot 'fixture.ts'
$generated = Join-Path $repository '.local/quickjs-build/fixture.js'
$asset = Join-Path $repository 'unity/Packages/com.capsule.wasmtime/Runtime/Resources/PlainJsFixture.txt'
if ((& bun --version) -ne '1.3.6') { throw 'Fixture transpilation requires Bun 1.3.6.' }
New-Item -ItemType Directory -Force (Split-Path -Parent $generated) | Out-Null
& bun build $source --target browser --outfile $generated
if ($LASTEXITCODE -ne 0) { throw 'Fixture TypeScript transpilation failed.' }
if ((Get-Item -LiteralPath $generated).Length -gt 65536) { throw 'Fixture bundle exceeds 64 KiB.' }
if ($Verify) {
    if (-not (Test-Path -LiteralPath $asset)) { throw 'Generated fixture asset missing.' }
    if ((Get-Sha256 $asset) -ne (Get-Sha256 $generated)) {
        throw 'Generated fixture asset differs from TypeScript source.'
    }
} else {
    Copy-Item -LiteralPath $generated -Destination $asset -Force
}
Write-Output "Fixture TypeScript bundle verified: $(Get-Sha256 $generated)"
