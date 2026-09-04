param(
    [string]$Root = ".cis/local/testing/coverage",
    [double]$MinimumLineCoverage = 75
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $Root))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot ".cis/local/testing"))
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
if (-not $resolvedRoot.Equals($allowedRoot, $comparison) -and
    -not $resolvedRoot.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, $comparison)) {
    throw "Coverage input must remain beneath the derived testing root $allowedRoot"
}

$reports = @(Get-ChildItem -LiteralPath $resolvedRoot -Filter "coverage.cobertura.xml" -File -Recurse)
if ($reports.Count -eq 0) { throw "No Cobertura coverage reports were found beneath $resolvedRoot" }

$exclusions = @(
    [ordered]@{
        pattern = "(^|/)obj/"
        reason = "Compiler and source-generator output is not authored production code."
        owner = "CIS maintainers"
    }
)
$lines = @{}
foreach ($report in $reports) {
    if ($report.Length -gt 100MB) { throw "Coverage report exceeds 100 MiB: $($report.FullName)" }
    [xml]$document = Get-Content -LiteralPath $report.FullName -Raw
    foreach ($class in @($document.coverage.packages.package.classes.class)) {
        $file = ([string]$class.filename).Replace('\', '/')
        if ($exclusions | Where-Object { $file -match $_.pattern }) { continue }
        foreach ($line in @($class.lines.line)) {
            $number = [string]$line.number
            if ([string]::IsNullOrWhiteSpace($file) -or [string]::IsNullOrWhiteSpace($number)) { continue }
            $key = "$file`:$number"
            $covered = [int64]$line.hits -gt 0
            if (-not $lines.ContainsKey($key) -or $covered) { $lines[$key] = $covered }
        }
    }
}

if ($lines.Count -eq 0) { throw "Coverage reports contain no production line evidence." }
$coveredLines = @($lines.Values | Where-Object { $_ }).Count
$percentage = [Math]::Round(($coveredLines * 100.0) / $lines.Count, 2)
$summary = [ordered]@{
    schemaVersion = 1
    reports = $reports.Count
    coveredLines = $coveredLines
    totalLines = $lines.Count
    lineCoverage = $percentage
    requiredLineCoverage = $MinimumLineCoverage
    passed = $percentage -ge $MinimumLineCoverage
    exclusions = $exclusions
    total = [ordered]@{
        lines = [ordered]@{ total = $lines.Count; covered = $coveredLines; pct = $percentage }
        statements = [ordered]@{ total = $lines.Count; covered = $coveredLines; pct = $percentage }
        functions = [ordered]@{ total = 0; covered = 0; pct = 0 }
        branches = [ordered]@{ total = 0; covered = 0; pct = 0 }
    }
}
$summaryPath = Join-Path $resolvedRoot "summary.json"
[IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Host "CIS line coverage: $percentage% ($coveredLines/$($lines.Count)); required: $MinimumLineCoverage%"
if ($percentage -lt $MinimumLineCoverage) { throw "Line coverage is below the required threshold." }
