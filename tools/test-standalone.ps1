#requires -Version 7.4
param([Parameter(Mandatory)][string]$Executable)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$source = (Resolve-Path -LiteralPath $Executable).Path
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('cis-standalone-' + [guid]::NewGuid().ToString('N'))
$savedEnvironment = @{}
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    $binary = Join-Path $fixture ([IO.Path]::GetFileName($source))
    Copy-Item -LiteralPath $source -Destination $binary
    if ($IsLinux) { chmod +x $binary }
    foreach ($key in @('DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_MULTILEVEL_LOOKUP', 'DOTNET_BUNDLE_EXTRACT_BASE_DIR')) {
        $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
    }
    $env:DOTNET_ROOT = Join-Path $fixture 'no-installed-runtime'
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $fixture 'bundle-cache'
    $repository = Join-Path $fixture 'sample'
    New-Item -ItemType Directory -Path $repository | Out-Null
    [IO.File]::WriteAllText((Join-Path $repository 'Sample.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
    [IO.File]::WriteAllText((Join-Path $repository 'Sample.cs'), 'namespace Smoke; public class StandaloneProbe { public string Run(string value) => System.String.Concat(value, "!"); }')
    Push-Location $repository
    try {
        & $binary --help | Out-Null
        $modules = (& $binary host modules --format agent) -join "`n"
        foreach ($module in @('host', 'repo', 'graph', 'agent-codex', 'agent-claude', 'definition', 'test', 'verify')) {
            if (-not $modules.Contains("module=$module;", [StringComparison]::Ordinal)) { throw "Missing packaged module: $module" }
        }
        & $binary repo init --repo $repository --root docs --yes --format agent | Out-Null
        foreach ($module in @('docs', 'skills', 'standards')) {
            & $binary $module validate --repo $repository --strict --format agent | Out-Null
        }
        $graph = (& $binary graph build --repo $repository --format json) | ConvertFrom-Json
        if ($graph.exitCode -ne 0 -or $graph.nodeCount -le 0 -or -not (Test-Path -LiteralPath $graph.graphPath)) {
            throw 'Packaged graph build did not create a SQLite graph.'
        }
        $symbols = (& $binary graph find --repo $repository --kind symbol --text StandaloneProbe --format json) | ConvertFrom-Json
        if ($symbols.exitCode -ne 0 -or $symbols.nodes.Count -eq 0) { throw 'Packaged C# analysis found no fixture symbol.' }
        $calls = (& $binary graph find --repo $repository --kind symbol --facet external --text 'Concat' --format json) | ConvertFrom-Json
        if ($calls.exitCode -ne 0 -or $calls.nodes.Count -eq 0) { throw 'Packaged C# analysis could not bind a runtime method.' }
        Write-Host 'Standalone smoke passed: isolated executable, module registration, embedded starters, SQLite and compiler metadata.'
    } finally { Pop-Location }
} finally {
    foreach ($key in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], 'Process') }
    $tempRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath()))
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (-not $resolvedFixture.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Unsafe standalone smoke cleanup path.'
    }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
