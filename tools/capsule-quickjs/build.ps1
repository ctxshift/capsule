param(
    [string]$OutputPath = 'unity/Packages/com.capsule.wasmtime/Runtime/Carrier/CapsuleQuickJs.bytes',
    [switch]$Verify
)
$ErrorActionPreference = 'Stop'
function Get-Sha256([string]$path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($path)
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
}

$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$revision = '6d46d07d04041b40f4f49eaa7fdebe44c314c699'
$expected = '0C5DFA49D0797B3599057D7B1428FA9C42E75542CC09F163CBE69D9DBCCFA5B7'
$source = Join-Path $repository '.local/quickjs-build/upstream'
$work = Join-Path $repository '.local/quickjs-build'
$raw = Join-Path $work 'capsule-quickjs.raw.wasm'
$built = Join-Path $work 'capsule-quickjs.wasm'
$target = [System.IO.Path]::GetFullPath((Join-Path $repository $OutputPath))
$owned = [System.IO.Path]::GetFullPath((Join-Path $repository 'unity/Packages/com.capsule.wasmtime/Runtime/Carrier'))
if (-not $target.StartsWith($owned + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Carrier output must remain under the owned Capsule/Carrier directory.'
}
if ((& zig version) -ne '0.16.0') { throw 'Carrier build requires Zig 0.16.0.' }
if ((& wasm-tools --version) -ne 'wasm-tools 1.241.2') {
    throw 'Carrier build requires wasm-tools 1.241.2.'
}
New-Item -ItemType Directory -Force $work | Out-Null
if (-not (Test-Path (Join-Path $source '.git'))) {
    & git clone --depth 1 --branch v0.17.0 https://github.com/quickjs-ng/quickjs.git $source
    if ($LASTEXITCODE -ne 0) { throw 'QuickJS source checkout failed.' }
}
$actualRevision = (& git -C $source rev-parse HEAD).Trim()
if ($actualRevision -ne $revision) { throw "QuickJS source revision differs: $actualRevision" }
$dirty = & git -C $source status --porcelain --untracked-files=no
if ($dirty) { throw 'Pinned QuickJS upstream checkout has local edits.' }

$arguments = @(
    'cc', '-target', 'wasm32-wasi', '-Oz', '-D_GNU_SOURCE',
    '-I', $source,
    (Join-Path $PSScriptRoot 'capsule-quickjs.c'),
    (Join-Path $source 'quickjs.c'),
    (Join-Path $source 'dtoa.c'),
    (Join-Path $source 'libregexp.c'),
    (Join-Path $source 'libunicode.c'),
    '-lm', '-Wl,--no-entry', '-Wl,--export-memory',
    '-Wl,--max-memory=8388608', '-Wl,-z,stack-size=524288',
    '-o', $raw
)
& zig @arguments
if ($LASTEXITCODE -ne 0) { throw 'QuickJS carrier compile failed.' }
& wasm-tools strip $raw -o $built
if ($LASTEXITCODE -ne 0) { throw 'QuickJS carrier strip failed.' }
& wasm-tools validate $built
if ($LASTEXITCODE -ne 0) { throw 'QuickJS carrier validation failed.' }
$digest = Get-Sha256 $built
if ($digest -ne $expected) { throw "Carrier digest differs from the pinned build: $digest" }
if ($Verify) {
    if (-not (Test-Path -LiteralPath $target)) { throw 'Built carrier asset is missing.' }
    $existing = Get-Sha256 $target
    if ($existing -ne $digest) { throw "Carrier asset digest differs: $existing" }
} else {
    New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath $built -Destination $target -Force
}
Write-Output "Pinned QuickJS carrier verified: $digest ($((Get-Item -LiteralPath $built).Length) bytes)"
