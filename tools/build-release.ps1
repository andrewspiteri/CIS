param(
    [string]$Configuration = "Release",
    [string]$Output = "artifacts/release",
    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutput = [IO.Path]::GetFullPath((Join-Path $repoRoot $Output))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
if (-not $resolvedOutput.Equals($allowedRoot, $comparison) -and
    -not $resolvedOutput.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, $comparison)) {
    throw "Release output must remain beneath $allowedRoot"
}

$versionText = Get-Content (Join-Path $repoRoot "Version.props") -Raw
$version = [regex]::Match($versionText, '<VersionPrefix>(?<v>[^<]+)</VersionPrefix>').Groups['v'].Value
$extension = Get-Content (Join-Path $repoRoot "vscode-extension/package.json") -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($version) -or $extension.version -ne $version) {
    throw "Version.props and vscode-extension/package.json must declare the same version."
}
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw "Release version is not a safe semantic version." }
$dirty = @(git -C $repoRoot status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) { throw "Unable to verify the Git worktree before packaging." }
if ($dirty.Count -gt 0) { throw "Release packaging requires a clean worktree so the source archive and binaries have identical provenance." }

New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null
Get-ChildItem -LiteralPath $resolvedOutput -Force | Remove-Item -Recurse -Force
Push-Location $repoRoot
try {
    if (-not $SkipRestore) { dotnet restore ChangeImpactStudio.slnx --configfile NuGet.Config }
    node tools/audit-dotnet-packages.mjs ChangeImpactStudio.slnx
    dotnet build ChangeImpactStudio.slnx -c $Configuration --no-restore
    dotnet test ChangeImpactStudio.slnx -c $Configuration --no-build --no-restore --maxcpucount:1
    node --check vscode-extension/extension.js
    node --test vscode-extension/test/*.test.js
    dotnet pack src/Cis.Host/Cis.Host.csproj -c $Configuration --no-build --no-restore -o $resolvedOutput
    & (Join-Path $PSScriptRoot "package-vsix.ps1") -Output $Output

    $sourceArchive = Join-Path $resolvedOutput "change-impact-studio-$version-source.zip"
    git archive --format=zip --output=$sourceArchive HEAD

    $toolPath = Join-Path $resolvedOutput ".tool-smoke"
    $toolPackageCache = Join-Path $resolvedOutput ".tool-smoke-packages"
    $toolNugetConfig = Join-Path $resolvedOutput ".tool-smoke.nuget.config"
    $escapedPackageSource = [Security.SecurityElement]::Escape($resolvedOutput)
    [IO.File]::WriteAllText($toolNugetConfig, @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="release-under-test" value="$escapedPackageSource" />
  </packageSources>
</configuration>
"@, [Text.UTF8Encoding]::new($false))
    $previousNugetPackages = $env:NUGET_PACKAGES
    try {
        $env:NUGET_PACKAGES = $toolPackageCache
        New-Item -ItemType Directory -Force -Path $toolPath | Out-Null
        dotnet tool install AndrewSpiteri.ChangeImpactStudio --tool-path $toolPath --version $version --configfile $toolNugetConfig --no-cache
        $packagedCis = Join-Path $toolPath "cis"
        & $packagedCis --help | Out-Null
        $loadedModules = (& $packagedCis host modules --format agent) -join "`n"
        foreach ($requiredModule in @(
            "module=agent;",
            "module=agent-claude;",
            "module=agent-codex;",
            "module=skills;",
            "module=standards;",
            "module=technical-intent;",
            "module=solution-design;",
            "module=ui-direction;",
            "module=definition;",
            "module=test;",
            "module=verify;")) {
            if (-not $loadedModules.Contains($requiredModule, [StringComparison]::Ordinal)) {
                throw "Packaged CLI did not register required module marker '$requiredModule'."
            }
        }
        & $packagedCis skills --help | Out-Null
        & $packagedCis agent --help | Out-Null
        & $packagedCis standards --help | Out-Null
        & $packagedCis technical-intent --help | Out-Null
        & $packagedCis solution-design --help | Out-Null
        & $packagedCis ui-direction --help | Out-Null
        & $packagedCis definition --help | Out-Null
        & $packagedCis test --help | Out-Null
        & $packagedCis verify --help | Out-Null
    }
    finally {
        $env:NUGET_PACKAGES = $previousNugetPackages
        if (Test-Path -LiteralPath $toolPath) { Remove-Item -LiteralPath $toolPath -Recurse -Force }
        if (Test-Path -LiteralPath $toolPackageCache) { Remove-Item -LiteralPath $toolPackageCache -Recurse -Force }
        if (Test-Path -LiteralPath $toolNugetConfig) { Remove-Item -LiteralPath $toolNugetConfig -Force }
    }

    $checksums = Get-ChildItem -LiteralPath $resolvedOutput -File | Sort-Object Name | ForEach-Object {
        "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
    }
    [IO.File]::WriteAllLines((Join-Path $resolvedOutput "SHA256SUMS"), $checksums, [Text.UTF8Encoding]::new($false))
    Write-Host "Release $version created at $resolvedOutput"
}
finally {
    Pop-Location
}
