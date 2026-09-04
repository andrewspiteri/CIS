param(
    [double]$MinimumLineCoverage = 75
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$testingRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot ".cis/local/testing"))
$runId = [DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + [guid]::NewGuid().ToString("N").Substring(0, 8)
$runRoot = Join-Path $testingRoot "runs/$runId/dotnet-tests"
$resultRoot = Join-Path $runRoot "results"
$coverageRoot = $resultRoot
New-Item -ItemType Directory -Force -Path $resultRoot | Out-Null

$logPath = Join-Path $runRoot "test.log"
Push-Location $repoRoot
try {
    & dotnet test ChangeImpactStudio.slnx -c Release --no-build --no-restore --maxcpucount:1 `
        --logger trx --results-directory $resultRoot --collect:"XPlat Code Coverage" 2>&1 |
        Tee-Object -FilePath $logPath
    $testExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}
if ($testExitCode -ne 0) { throw "The .NET test suite failed with exit code $testExitCode." }

$seenCoverage = @{}
$duplicateCoverage = 0
foreach ($coverageReport in @(Get-ChildItem -LiteralPath $coverageRoot -Filter "coverage.cobertura.xml" -File -Recurse)) {
    if ($coverageReport.Length -gt 100MB) { throw "Coverage evidence exceeds 100 MiB: $($coverageReport.FullName)" }
    $digest = (Get-FileHash -LiteralPath $coverageReport.FullName -Algorithm SHA256).Hash
    if ($seenCoverage.ContainsKey($digest)) {
        Remove-Item -LiteralPath $coverageReport.FullName -Force
        $duplicateCoverage++
    }
    else {
        $seenCoverage[$digest] = $coverageReport.FullName
    }
}

$trxFiles = @(Get-ChildItem -LiteralPath $resultRoot -Filter "*.trx" -File -Recurse)
if ($trxFiles.Count -eq 0) { throw "The .NET test suite produced no TRX evidence." }
$combined = [xml]"<TestRun><Results /></TestRun>"
$combinedResults = $combined.DocumentElement.SelectSingleNode("Results")
$testCount = 0
foreach ($trx in $trxFiles) {
    if ($trx.Length -gt 100MB) { throw "TRX evidence exceeds 100 MiB: $($trx.FullName)" }
    [xml]$document = Get-Content -LiteralPath $trx.FullName -Raw
    foreach ($result in @($document.SelectNodes("//*[local-name()='UnitTestResult']"))) {
        $copy = $combined.CreateElement("UnitTestResult")
        foreach ($attributeName in @("testName", "outcome", "duration")) {
            if ($result.HasAttribute($attributeName)) {
                $copy.SetAttribute($attributeName, $result.GetAttribute($attributeName))
            }
        }
        $combinedResults.AppendChild($copy) | Out-Null
        $testCount++
    }
}
if ($testCount -eq 0) { throw "TRX evidence contains no test executions." }

$canonicalResultsRoot = Join-Path $testingRoot "results"
New-Item -ItemType Directory -Force -Path $canonicalResultsRoot | Out-Null
$canonicalTrx = Join-Path $canonicalResultsRoot "dotnet-tests.trx"
$temporaryTrx = "$canonicalTrx.$runId.tmp"
$combined.Save($temporaryTrx)
Move-Item -LiteralPath $temporaryTrx -Destination $canonicalTrx -Force

Push-Location $repoRoot
try {
    # Windows PowerShell 5.1 does not expose System.IO.Path.GetRelativePath.
    $relativeCoverage = Resolve-Path -LiteralPath $coverageRoot -Relative
}
finally {
    Pop-Location
}
& (Join-Path $PSScriptRoot "verify-coverage.ps1") -Root $relativeCoverage -MinimumLineCoverage $MinimumLineCoverage
$runSummary = Join-Path $coverageRoot "summary.json"
$canonicalCoverageRoot = Join-Path $testingRoot "coverage"
New-Item -ItemType Directory -Force -Path $canonicalCoverageRoot | Out-Null
$canonicalSummary = Join-Path $canonicalCoverageRoot "summary.json"
$temporarySummary = "$canonicalSummary.$runId.tmp"
Copy-Item -LiteralPath $runSummary -Destination $temporarySummary
Move-Item -LiteralPath $temporarySummary -Destination $canonicalSummary -Force
Write-Host "CIS .NET evidence: $testCount tests, $($trxFiles.Count) TRX files, $duplicateCoverage duplicate coverage files removed, run $runId"
