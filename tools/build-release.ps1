param(
    [string]$Configuration = "Release",
    [string]$Output = "artifacts/release",
    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutput = [IO.Path]::GetFullPath((Join-Path $repoRoot $Output))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
if (-not $resolvedOutput.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Release output must remain beneath $allowedRoot"
}

$versionText = Get-Content (Join-Path $repoRoot "Version.props") -Raw
$version = [regex]::Match($versionText, '<VersionPrefix>(?<v>[^<]+)</VersionPrefix>').Groups['v'].Value
$extension = Get-Content (Join-Path $repoRoot "vscode-extension/package.json") -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($version) -or $extension.version -ne $version) {
    throw "Version.props and vscode-extension/package.json must declare the same version."
}

New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null
Get-ChildItem -LiteralPath $resolvedOutput -Force | Remove-Item -Recurse -Force
Push-Location $repoRoot
try {
    if (-not $SkipRestore) { dotnet restore ChangeImpactStudio.slnx }
    dotnet build ChangeImpactStudio.slnx -c $Configuration --no-restore
    dotnet test ChangeImpactStudio.slnx -c $Configuration --no-build --no-restore
    node --check vscode-extension/extension.js
    node --test vscode-extension/test/*.test.js
    dotnet pack src/Cis.Host/Cis.Host.csproj -c $Configuration --no-build --no-restore -o $resolvedOutput
    & (Join-Path $PSScriptRoot "package-vsix.ps1") -Output $Output

    $sourceArchive = Join-Path $resolvedOutput "change-impact-studio-$version-source.zip"
    git archive --format=zip --output=$sourceArchive HEAD

    $toolPath = Join-Path $resolvedOutput ".tool-smoke"
    New-Item -ItemType Directory -Force -Path $toolPath | Out-Null
    dotnet tool install AndrewSpiteri.ChangeImpactStudio --tool-path $toolPath --version $version --add-source $resolvedOutput --ignore-failed-sources
    & (Join-Path $toolPath "cis") --help | Out-Null
    Remove-Item -LiteralPath $toolPath -Recurse -Force

    $checksums = Get-ChildItem -LiteralPath $resolvedOutput -File | Sort-Object Name | ForEach-Object {
        "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
    }
    [IO.File]::WriteAllLines((Join-Path $resolvedOutput "SHA256SUMS"), $checksums, [Text.UTF8Encoding]::new($false))
    Write-Host "Release $version created at $resolvedOutput"
}
finally {
    Pop-Location
}
