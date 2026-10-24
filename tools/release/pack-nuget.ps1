param(
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][string]$Revision,
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (![IO.Path]::IsPathFullyQualified($Output)) { throw 'Output must be an absolute path.' }
if ($Revision -cnotmatch '^[0-9a-f]{40}$') { throw 'Revision must be a full lowercase Git SHA.' }
if ($Version -ne '0.1.0') { throw 'This candidate releases only version 0.1.0.' }
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) {
    if (@(Get-ChildItem -LiteralPath $Output -Force).Count -ne 0) { throw 'Output directory must be empty.' }
} else { New-Item -ItemType Directory -Path $Output -Force | Out-Null }
$logs = Join-Path $repo '.local/release-native'
New-Item -ItemType Directory -Path $logs -Force | Out-Null

$plans = @(
    [ordered]@{ id='Capsule.Core'; project='packages/capsule-native/Core/Capsule.Core.csproj'; dependencies=[ordered]@{} },
    [ordered]@{ id='Capsule.Package'; project='packages/capsule-native/Package/Capsule.Package.csproj'; dependencies=[ordered]@{ 'Capsule.Core'='[0.1.0]'; 'Newtonsoft.Json'='[13.0.2]' } },
    [ordered]@{ id='Capsule.Wasmtime'; project='packages/capsule-native/Wasmtime/Capsule.Wasmtime.csproj'; dependencies=[ordered]@{ 'Capsule.Core'='[0.1.0]' } },
    [ordered]@{ id='Capsule.Yoga'; project='packages/capsule-native/Yoga/Capsule.Yoga.csproj'; dependencies=[ordered]@{ 'Capsule.Core'='[0.1.0]' } },
    [ordered]@{ id='Rpg.Features'; project='packages/rpg-features/native/Rpg.Features.csproj'; dependencies=[ordered]@{ 'Capsule.Core'='[0.1.0]' } },
    [ordered]@{ id='Mmo.Features'; project='packages/mmo-features/native/Mmo.Features.csproj'; dependencies=[ordered]@{ 'Capsule.Core'='[0.1.0]' } },
    [ordered]@{ id='SkillSystem.Features'; project='packages/skill-system-features/native/SkillSystem.Features.csproj'; dependencies=[ordered]@{ 'Capsule.Core'='[0.1.0]' } }
)

function Get-ZipEntries([string]$path) {
    $archive = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $entries = [ordered]@{}
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName.EndsWith('/')) { continue }
            $memory = [IO.MemoryStream]::new()
            try { $stream = $entry.Open(); try { $stream.CopyTo($memory) } finally { $stream.Dispose() }; $entries[$entry.FullName] = $memory.ToArray() }
            finally { $memory.Dispose() }
        }
        return $entries
    } finally { $archive.Dispose() }
}

function Normalize-Package([string]$path, [System.Collections.IDictionary]$expected) {
    $entries = Get-ZipEntries $path
    $nuspecName = @($entries.Keys | Where-Object { $_.EndsWith('.nuspec') })
    if ($nuspecName.Count -ne 1) { throw 'Expected one NuGet specification.' }
    $xml = [Xml.XmlDocument]::new()
    $source = [IO.MemoryStream]::new($entries[$nuspecName[0]], $false)
    try { $xml.Load($source) } finally { $source.Dispose() }
    $namespaces = [Xml.XmlNamespaceManager]::new($xml.NameTable)
    $namespaces.AddNamespace('n', $xml.DocumentElement.NamespaceURI)
    $found = @{}
    foreach ($dependency in $xml.SelectNodes('//n:dependency', $namespaces)) {
        $id = $dependency.GetAttribute('id')
        if (!$expected.Contains($id) -or $found.ContainsKey($id)) { throw "Unexpected or repeated NuGet dependency: $id" }
        $dependency.SetAttribute('version', $expected[$id])
        $found[$id] = $true
    }
    if ($found.Count -ne $expected.Count) { throw 'NuGet dependency set is incomplete.' }
    $settings = [Xml.XmlWriterSettings]::new()
    $settings.Encoding = [Text.UTF8Encoding]::new($false)
    $settings.Indent = $true
    $settings.NewLineChars = "`n"
    $memory = [IO.MemoryStream]::new()
    try {
        $writer = [Xml.XmlWriter]::Create($memory, $settings)
        try { $xml.Save($writer) } finally { $writer.Dispose() }
        $entries[$nuspecName[0]] = $memory.ToArray()
    } finally { $memory.Dispose() }
    $temporary = $path + '.canonical'
    $archive = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in @($entries.Keys | Sort-Object -CaseSensitive)) {
            $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(1980,1,1,0,0,0,[TimeSpan]::Zero)
            $entry.ExternalAttributes = 0
            $stream = $entry.Open()
            try { $bytes = $entries[$name]; $stream.Write($bytes, 0, $bytes.Length) }
            finally { $stream.Dispose() }
        }
    } finally { $archive.Dispose() }
    Move-Item -LiteralPath $temporary -Destination $path -Force
    return $entries
}

$packages = @()
foreach ($plan in $plans) {
    $project = Join-Path $repo $plan.project
    $log = Join-Path $logs ($plan.id + '-pack.log')
    & dotnet pack $project -c Release -o $Output --nologo --disable-build-servers -p:RestoreLockedMode=true -p:Version=$Version -p:PackageVersion=$Version -p:RepositoryCommit=$Revision -p:SourceRevisionId=$Revision *> $log
    if ($LASTEXITCODE -ne 0) { throw "Packing $($plan.id) failed; see $log" }
    $file = $plan.id + '.' + $Version + '.nupkg'
    $path = Join-Path $Output $file
    if (!(Test-Path -LiteralPath $path)) { throw "Missing package: $file" }
    $entries = Normalize-Package $path $plan.dependencies
    $payload = @()
    foreach ($name in @($entries.Keys | Where-Object { $_.StartsWith('runtimes/') -or $_.StartsWith('contentFiles/') -or $_.StartsWith('licenses/') } | Sort-Object -CaseSensitive)) {
        $payload += [ordered]@{ path=$name; sha256=([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($entries[$name]))).ToLowerInvariant() }
    }
    $packages += [ordered]@{ id=$plan.id; version=$Version; file=$file; sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant(); dependencies=$plan.dependencies; payload=$payload }
    Write-Output "Packed $file"
}
[ordered]@{ schemaVersion=1; revision=$Revision; version=$Version; packages=$packages } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Output 'nuget-manifest.json') -Encoding utf8NoBOM
Write-Output "NuGet manifest: $(Join-Path $Output 'nuget-manifest.json')"
