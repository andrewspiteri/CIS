param(
    [string]$OutputRoot = ".cis/local/testing/mutation"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot ".cis/local/testing/mutation"))
$resolvedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
if (-not $resolvedRoot.Equals($allowedRoot, $comparison) -and
    -not $resolvedRoot.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, $comparison)) {
    throw "Mutation output must remain beneath $allowedRoot"
}

$runId = [DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + [guid]::NewGuid().ToString("N").Substring(0, 8)
$runOutput = Join-Path $resolvedRoot "runs/$runId"
New-Item -ItemType Directory -Force -Path $runOutput | Out-Null

Push-Location (Join-Path $repoRoot "src/Cis.Abstractions")
try {
    & dotnet stryker --config-file stryker-config.json --output $runOutput
    if ($LASTEXITCODE -ne 0) { throw "Mutation gate failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}

$report = Get-ChildItem -LiteralPath $runOutput -Filter "cis-abstractions-hardening.json" -File -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($null -eq $report -or $report.Length -le 0 -or $report.Length -gt 100MB) {
    throw "Mutation gate did not produce one bounded Stryker JSON report."
}

try { $parsed = Get-Content -LiteralPath $report.FullName -Raw | ConvertFrom-Json }
catch { throw "Mutation report is not valid JSON." }
if ([string]$parsed.schemaVersion -ne "2" -or $null -eq $parsed.files) {
    throw "Mutation report does not match the supported Stryker schema."
}

$config = Get-Content -LiteralPath (Join-Path $repoRoot "src/Cis.Abstractions/stryker-config.json") -Raw | ConvertFrom-Json
$breakThreshold = [double]$config.'stryker-config'.thresholds.break
$killed = 0; $survived = 0; $timedOut = 0; $noCoverage = 0
foreach ($file in $parsed.files.PSObject.Properties) {
    foreach ($mutant in @($file.Value.mutants)) {
        switch ([string]$mutant.status) {
            "Killed" { $killed++ }
            "Survived" { $survived++ }
            "Timeout" { $timedOut++ }
            "NoCoverage" { $noCoverage++ }
        }
    }
}
$denominator = $killed + $survived + $timedOut
if ($denominator -le 0) { throw "Mutation report contains no scored mutants." }
$score = [Math]::Round((($killed + $timedOut) * 100.0) / $denominator, 2)
$gatePassed = $score -ge $breakThreshold -and $noCoverage -eq 0
if (-not $gatePassed) { throw "Mutation evidence does not satisfy the configured $breakThreshold% gate." }
$sourceDigest = (Get-FileHash -LiteralPath $report.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$parsed | Add-Member -NotePropertyName cisGate -NotePropertyValue ([ordered]@{
    break = $breakThreshold
    score = $score
    passed = $gatePassed
    noCoverage = $noCoverage
    sourceDigest = $sourceDigest
}) -Force

$canonical = Join-Path $resolvedRoot "cis-abstractions.json"
$temporary = "$canonical.$runId.tmp"
[IO.File]::WriteAllText($temporary, ($parsed | ConvertTo-Json -Depth 100) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $canonical -Force
Write-Host "CIS mutation evidence: $canonical"
