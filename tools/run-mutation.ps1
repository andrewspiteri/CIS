param(
    [string]$Profile = 'tools/mutation-profile.json',
    [switch]$Canary
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$cli = Join-Path $repoRoot 'src/Cis.Host/bin/Release/net10.0/cis.dll'
if (-not [IO.File]::Exists($cli)) {
    throw 'Build src/Cis.Host in Release before running mutation.'
}
$kind = if ($Canary) { 'canary' } else { 'qualification' }
& dotnet $cli test mutation run --repo $repoRoot --profile $Profile --kind $kind --format human
exit $LASTEXITCODE
