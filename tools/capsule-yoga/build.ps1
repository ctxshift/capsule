param()
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$source = Join-Path $root '.local/yoga-build/upstream'
$pin = '042f5013152eb81c1552dec945b88f7b95ca350f'
if ((& zig version) -ne '0.16.0') { throw 'Requires Zig 0.16.0.' }
if (!(Test-Path -LiteralPath $source)) {
    & git clone --depth 1 --branch v3.2.1 https://github.com/facebook/yoga.git $source
    if ($LASTEXITCODE -ne 0) { throw 'Yoga clone failed.' }
}
if ((& git -C $source rev-parse HEAD) -ne $pin) { throw 'Yoga source pin mismatch.' }
if (& git -C $source status --porcelain) { throw 'Yoga source must be unmodified.' }
$output = Join-Path $root '.local/yoga-build/portable_ui_yoga.dll'
$sources = Get-ChildItem -LiteralPath (Join-Path $source 'yoga') -Recurse -Filter '*.cpp' | ForEach-Object FullName
$arguments = @('c++', '-target', 'x86_64-windows-gnu', '-std=c++20', '-O2', '-fno-rtti', '-shared', '-I', $source, (Join-Path $PSScriptRoot 'native.cpp')) + $sources + @('-o', $output)
& zig @arguments
if ($LASTEXITCODE -ne 0) { throw 'Native Yoga build failed.' }
[ordered]@{ yoga = '3.2.1'; commit = $pin; compiler = 'Zig 0.16.0'; platform = 'Windows x86_64'; sourceSha256 = (Get-FileHash (Join-Path $PSScriptRoot 'native.cpp')).Hash; dllSha256 = (Get-FileHash $output).Hash; arguments = $arguments } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $root '.local/yoga-build/native-build.json')
Write-Output $output
