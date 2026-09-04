param(
    [double]$MinimumLineCoverage = 95
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$extensionRoot = Join-Path $repoRoot "vscode-extension"
$evidenceRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot ".cis/local/testing"))
$resultRoot = Join-Path $evidenceRoot "results"
$coverageRoot = Join-Path $evidenceRoot "coverage"
$runId = [DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + [guid]::NewGuid().ToString("N").Substring(0, 8)
$runRoot = Join-Path $evidenceRoot "runs/$runId/vscode-extension-tests"
New-Item -ItemType Directory -Force -Path $resultRoot, $coverageRoot, $runRoot | Out-Null

$syntaxFiles = @(
    "extension.js",
    "lib/authority.js",
    "lib/cis-cli.js",
    "lib/projections.js",
    "lib/security.js",
    "lib/views.js",
    "lib/webview.js"
)

Push-Location $extensionRoot
try {
    foreach ($syntaxFile in $syntaxFiles) {
        & node --check $syntaxFile
        if ($LASTEXITCODE -ne 0) { throw "Node syntax validation failed for $syntaxFile." }
    }

    $temporaryJunit = Join-Path $runRoot "vscode-extension-tests.junit.xml"
    & node --test --test-reporter=junit --test-reporter-destination=$temporaryJunit "test/*.test.js"
    if ($LASTEXITCODE -ne 0) { throw "The VS Code extension JUnit execution failed with exit code $LASTEXITCODE." }
    if (-not (Test-Path -LiteralPath $temporaryJunit) -or (Get-Item -LiteralPath $temporaryJunit).Length -eq 0) {
        throw "The VS Code extension execution produced no JUnit evidence."
    }
    [xml]$junitEvidence = Get-Content -LiteralPath $temporaryJunit -Raw
    $testCount = @($junitEvidence.testsuites.testcase).Count
    if ($testCount -le 0) { throw "The VS Code extension JUnit evidence contains no test cases." }

    $coverageLog = Join-Path $runRoot "coverage.log"
    $coverageOutput = @(& node --test --experimental-test-coverage "test/*.test.js" 2>&1)
    $coverageExitCode = $LASTEXITCODE
    $coverageOutput | Tee-Object -FilePath $coverageLog
    if ($coverageExitCode -ne 0) { throw "The VS Code extension coverage execution failed with exit code $coverageExitCode." }
}
finally {
    Pop-Location
}

$aggregate = $coverageOutput | Where-Object { $_ -match '^.*all files\s+\|' } | Select-Object -Last 1
if (-not $aggregate -or $aggregate -notmatch 'all files\s+\|\s+([0-9]+(?:\.[0-9]+)?)') {
    throw "The VS Code extension execution produced no readable aggregate line coverage."
}
$lineCoverage = [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
$passed = $lineCoverage -ge $MinimumLineCoverage

$canonicalJunit = Join-Path $resultRoot "vscode-extension-tests.junit.xml"
$junitStaging = "$canonicalJunit.$runId.tmp"
Copy-Item -LiteralPath $temporaryJunit -Destination $junitStaging
Move-Item -LiteralPath $junitStaging -Destination $canonicalJunit -Force

$summary = [ordered]@{
    schemaVersion = 1
    lineCoverage = $lineCoverage
    requiredLineCoverage = $MinimumLineCoverage
    passed = $passed
    scope = "vscode-extension-production"
    testCount = $testCount
    exclusions = @()
    total = [ordered]@{
        lines = [ordered]@{ total = 10000; covered = [int][Math]::Round($lineCoverage * 100); pct = $lineCoverage }
        statements = [ordered]@{ total = 0; covered = 0; pct = 0 }
        functions = [ordered]@{ total = 0; covered = 0; pct = 0 }
        branches = [ordered]@{ total = 0; covered = 0; pct = 0 }
    }
}
$canonicalCoverage = Join-Path $coverageRoot "vscode-extension-summary.json"
$coverageStaging = "$canonicalCoverage.$runId.tmp"
[IO.File]::WriteAllText($coverageStaging, ($summary | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $coverageStaging -Destination $canonicalCoverage -Force

if (-not $passed) { throw "VS Code extension line coverage is $lineCoverage%; required: $MinimumLineCoverage%." }
Write-Host "CIS VS Code extension evidence: $testCount tests; line coverage $lineCoverage%; required $MinimumLineCoverage%; run $runId"
