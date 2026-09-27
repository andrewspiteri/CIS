#requires -Version 7.4
param(
    [ValidateSet('win-x64', 'linux-x64')]
    [string[]]$Runtime = @('win-x64', 'linux-x64'),
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts/standalone'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repoRoot = Split-Path -Parent $PSScriptRoot
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $Output))
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
if (-not $outputRoot.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, $comparison)) {
    throw 'Standalone output must be a directory beneath artifacts.'
}
$version = ([xml](Get-Content (Join-Path $repoRoot 'Version.props') -Raw)).Project.PropertyGroup.VersionPrefix
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Invalid release version.' }
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$stage = Join-Path $outputRoot ('.build-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$artifacts = @()
Push-Location $repoRoot
try {
    foreach ($rid in ($Runtime | Select-Object -Unique)) {
        $publish = Join-Path $stage $rid
        dotnet publish src/Cis.Host/Cis.Host.csproj -c $Configuration -r $rid `
            --self-contained true -p:CisStandalone=true -p:DebugType=embedded `
            --configfile NuGet.Config -o $publish
        if ($LASTEXITCODE -ne 0) { throw "Standalone publish failed for $rid." }
        $name = if ($rid -eq 'win-x64') { 'cis.exe' } else { 'cis' }
        $binary = Join-Path $publish $name
        $files = @(Get-ChildItem -LiteralPath $publish -File -Recurse)
        if ($files.Count -ne 1 -or -not (Test-Path -LiteralPath $binary -PathType Leaf)) {
            throw "Expected exactly one executable for $rid; found $($files.Count) files."
        }
        $base = "change-impact-studio-$version-$rid"
        if ($rid -eq 'win-x64') {
            $archive = Join-Path $outputRoot "$base.zip"
            Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $archive -Force
        } else {
            $archive = Join-Path $outputRoot "$base.tar.gz"
            $stream = [IO.File]::Create($archive)
            $gzip = [IO.Compression.GZipStream]::new($stream, [IO.Compression.CompressionLevel]::Optimal)
            $tar = [System.Formats.Tar.TarWriter]::new($gzip)
            try {
                foreach ($entryName in @('cis')) {
                    $entry = [System.Formats.Tar.PaxTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile, $entryName)
                    # Set Unix permissions explicitly, including when cross-publishing on Windows.
                    $entry.Mode = [IO.UnixFileMode]493
                    $entry.DataStream = [IO.File]::OpenRead((Join-Path $publish $entryName))
                    try { $tar.WriteEntry($entry) } finally { $entry.DataStream.Dispose() }
                }
            } finally { $tar.Dispose(); $gzip.Dispose(); $stream.Dispose() }
        }
        if (($IsWindows -and $rid -eq 'win-x64') -or ($IsLinux -and $rid -eq 'linux-x64')) {
            $unpacked = Join-Path $stage "$rid-unpacked"
            New-Item -ItemType Directory -Path $unpacked | Out-Null
            if ($IsWindows) { Expand-Archive -LiteralPath $archive -DestinationPath $unpacked }
            else {
                tar -xzf $archive -C $unpacked
                if (([IO.File]::GetUnixFileMode((Join-Path $unpacked $name)) -band [IO.UnixFileMode]::UserExecute) -eq 0) {
                    throw 'Linux archive lost executable permissions.'
                }
            }
            & (Join-Path $PSScriptRoot 'test-standalone.ps1') -Executable (Join-Path $unpacked $name)
        } else {
            Write-Host "Published $rid; execution must be verified on its target OS."
        }
        $artifacts += $archive
        Write-Host "Created $archive"
    }
    $checksums = $artifacts | Sort-Object | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($_)
    }
    [IO.File]::WriteAllLines((Join-Path $outputRoot 'SHA256SUMS'), [string[]]$checksums, [Text.UTF8Encoding]::new($false))
} finally {
    Pop-Location
    # Delete only the unique staging directory created by this invocation.
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    if (-not $resolvedStage.StartsWith($outputRoot + [IO.Path]::DirectorySeparatorChar, $comparison)) {
        throw 'Unsafe standalone staging cleanup path.'
    }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
