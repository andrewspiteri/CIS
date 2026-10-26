param(
    [string]$OutputRoot = '.cis/local/testing/mutation',
    [switch]$Canary,
    [ValidateRange(1, 3600)][int]$TimeoutSeconds = 1200,
    [ValidateRange(1, [long]::MaxValue)][long]$MaximumRunBytes = 536870912,
    [ValidateRange(1, [long]::MaxValue)][long]$MaximumRetainedBytes = 1073741824,
    [ValidateRange(0, [long]::MaxValue)][long]$MinimumFreeBytes = 1073741824
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Canary) {
    $TimeoutSeconds = [Math]::Min($TimeoutSeconds, 120)
    $MaximumRunBytes = [Math]::Min($MaximumRunBytes, 134217728)
}
Import-Module (Join-Path $PSScriptRoot 'MutationRunner.psm1') -Force
$repoRoot = Split-Path -Parent $PSScriptRoot
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot '.cis/local/testing/mutation'))
$resolvedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
if (-not $resolvedRoot.Equals($allowedRoot, $comparison) -and
    -not $resolvedRoot.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, $comparison)) {
    throw "Mutation output must remain beneath $allowedRoot"
}
# Reject linked ancestors before writing any run files.
$cursor = $resolvedRoot
while (-not $cursor.Equals($repoRoot, $comparison)) {
    if ((Test-Path -LiteralPath $cursor) -and
        ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Mutation output crosses a symbolic link: $cursor"
    }
    $cursor = [IO.Path]::GetDirectoryName($cursor)
}
if ((Get-MutationTreeBytes $allowedRoot) -ge $MaximumRetainedBytes) {
    throw 'Mutation storage budget reached. Review cis artifacts plan --family mutation-runs before another run.'
}
$existingParent = $resolvedRoot
while (-not [IO.Directory]::Exists($existingParent)) { $existingParent = [IO.Path]::GetDirectoryName($existingParent) }
$drive = [IO.DriveInfo]::new($existingParent)
if ($drive.AvailableFreeSpace -lt $MinimumFreeBytes) { throw 'Insufficient free space for mutation; no run started.' }

# Use the current built CLI; do not silently use a stale globally installed CIS.
$cli = Join-Path $repoRoot 'src/Cis.Host/bin/Release/net10.0/cis.dll'
if (-not [IO.File]::Exists($cli)) {
    throw 'Build src/Cis.Host in Release before mutation; assessment requires the current CIS CLI.'
}
$assessorInputs = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -File -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -in @('.cs', '.csproj', '.props', '.targets') })
foreach ($inputFile in $assessorInputs) {
    $projectName = [IO.Path]::GetRelativePath((Join-Path $repoRoot 'src'), $inputFile.FullName).Split([IO.Path]::DirectorySeparatorChar)[0]
    $assemblyName = if ($projectName -eq 'Cis.Host') { 'cis' } else { $projectName }
    $assembly = Join-Path ([IO.Path]::GetDirectoryName($cli)) "$assemblyName.dll"
    if (-not [IO.File]::Exists($assembly) -or $inputFile.LastWriteTimeUtc -gt [IO.File]::GetLastWriteTimeUtc($assembly)) {
        throw "CIS source is newer than its built $assemblyName assembly. Rebuild src/Cis.Host before mutation."
    }
}
# Fail SDK discovery before native mutation analysis, rather than letting a partial
# `dotnet --info` response send the analyzer looking in runtime directories.
$sdkInfo = & dotnet --info 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Native dotnet SDK discovery failed. Correct execution permissions/tooling before mutation; no campaign started.' }
if (-not $Canary) {
    & $PSCommandPath -Canary -OutputRoot $OutputRoot -TimeoutSeconds $TimeoutSeconds `
        -MaximumRunBytes $MaximumRunBytes -MaximumRetainedBytes $MaximumRetainedBytes -MinimumFreeBytes $MinimumFreeBytes
}
$runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
if ($Canary) { $runId = 'canary-' + $runId }
$runOutput = Join-Path $resolvedRoot "runs/$runId"
[IO.Directory]::CreateDirectory($runOutput) | Out-Null
function Write-AtomicJson([string]$Path, $Value) {
    $temporary = "$Path.tmp"
    [IO.File]::WriteAllText($temporary, (ConvertTo-Json -InputObject $Value -Depth 32) + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporary, $Path, $true)
}
function Publish-MutationResult([IO.FileInfo]$Report, $Assessment, [string]$Destination) {
    $temporary = "$Destination.$runId.tmp"
    [IO.File]::Copy($Report.FullName, $temporary, $true)
    [IO.File]::Move($temporary, $Destination, $true)
    $copy = $Assessment | ConvertTo-Json -Depth 32 | ConvertFrom-Json
    $relative = [IO.Path]::GetRelativePath($repoRoot, $Destination).Replace('\', '/')
    if ($null -ne $copy.mutation) { $copy.mutation.sourcePath = $relative }
    foreach ($artifact in $copy.artifacts) {
        if ($artifact.kind -eq 'mutation-result') { $artifact.path = $relative }
    }
    Write-AtomicJson "$Destination.assessment.json" $copy
}
function Digest-Text([string]$Value) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($Value))).ToLowerInvariant()
}
Push-Location $repoRoot
try {
    $revision = (& git rev-parse HEAD) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source revision.' }
    $diff = (& git diff --binary --no-ext-diff --no-textconv HEAD) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify tracked worktree changes.' }
    $untracked = @(& git ls-files --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify untracked inputs.' }
    [long]$untrackedBytes = 0
    $untrackedPatch = @($untracked | ForEach-Object {
        $path = Join-Path $repoRoot $_
        if ([IO.File]::Exists($path)) {
            $length = ([IO.FileInfo]::new($path)).Length
            if ($length -gt 768KB - $untrackedBytes) { throw 'Untracked inputs exceed the 768 KiB source-evidence budget. Commit or narrow the work before mutation.' }
            $bytes = [IO.File]::ReadAllBytes($path)
            $untrackedBytes += $bytes.Length
            @{ path = $_; sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
                base64 = [Convert]::ToBase64String($bytes) }
        }
    })
    $untrackedDigests = @($untrackedPatch | ForEach-Object { @{ path = $_.path; sha256 = $_.sha256 } })
} finally { Pop-Location }
if ([Text.Encoding]::UTF8.GetByteCount($diff) -gt 1MB) {
    throw 'Tracked source diff exceeds the 1 MiB evidence budget. Commit or narrow the work before mutation.'
}
[IO.File]::WriteAllText((Join-Path $runOutput 'source.diff'), $diff, [Text.UTF8Encoding]::new($false))
Write-AtomicJson -Path (Join-Path $runOutput 'untracked-inputs.json') -Value @($untrackedPatch)
$configuration = Join-Path $repoRoot 'src/Cis.Abstractions/stryker-config.json'
$config = Get-Content -LiteralPath $configuration -Raw | ConvertFrom-Json
$breakThreshold = [double]$config.'stryker-config'.thresholds.break
$arguments = @('stryker', '--config-file', 'stryker-config.json', '--output', $runOutput)
if ($Canary) {
    $source = [IO.File]::ReadAllText((Join-Path $repoRoot 'src/Cis.Abstractions/CisPathSafety.cs'))
    $start = $source.IndexOf('if (string.IsNullOrWhiteSpace(root)', [StringComparison]::Ordinal)
    if ($start -lt 0) { throw 'The canary source expression changed; review its scope before mutation.' }
    $end = $source.IndexOf(';', $start) + 1
    $arguments += @('--mutate', ('CisPathSafety.cs{' + $start + '..' + $end + '}'))
}
$record = [ordered]@{
    schemaVersion = 1; runId = $runId; startedUtc = [DateTimeOffset]::UtcNow.ToString('O'); status = 'running'
    purpose = $(if ($Canary) { 'canary' } else { 'full-scope' })
    source = @{ revision = $revision; trackedDiffSha256 = (Digest-Text $diff); untrackedFiles = $untrackedDigests }
    configSha256 = (Get-FileHash -LiteralPath $configuration -Algorithm SHA256).Hash.ToLowerInvariant()
    toolManifestSha256 = (Get-FileHash -LiteralPath (Join-Path $repoRoot '.config/dotnet-tools.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    assessorSha256 = (Get-FileHash -LiteralPath $cli -Algorithm SHA256).Hash.ToLowerInvariant()
    assessorAssemblies = @('cis.dll', 'Cis.Modules.Testing.dll', 'Cis.Abstractions.dll') | ForEach-Object {
        @{ name = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($cli)) $_) -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    command = @('dotnet') + $arguments
    limits = @{ timeoutSeconds = $TimeoutSeconds; maximumRunBytes = $MaximumRunBytes
        maximumRetainedBytes = $MaximumRetainedBytes; minimumFreeBytes = $MinimumFreeBytes }
}
$recordPath = Join-Path $runOutput 'run.json'
Write-AtomicJson $recordPath $record
try {
$execution = Invoke-BoundedMutationProcess -FilePath 'dotnet' -Arguments $arguments `
    -WorkingDirectory (Join-Path $repoRoot 'src/Cis.Abstractions') -RunRoot $runOutput -StorageRoot $allowedRoot `
    -TimeoutSeconds $TimeoutSeconds -MaximumRunBytes $MaximumRunBytes `
    -MaximumRetainedBytes $MaximumRetainedBytes -MinimumFreeBytes $MinimumFreeBytes
$record.status = $execution.status
$record.execution = $execution
$record.finishedUtc = [DateTimeOffset]::UtcNow.ToString('O')
Push-Location $repoRoot
try {
    $currentRevision = (& git rev-parse HEAD) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot recheck source revision.' }
    $currentDiff = (& git diff --binary --no-ext-diff --no-textconv HEAD) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot recheck tracked worktree changes.' }
    $currentUntracked = @(& git ls-files --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot recheck untracked inputs.' }
    $sourceUnchanged = $revision -ceq $currentRevision -and $diff -ceq $currentDiff -and
        ($untracked -join "`n") -ceq ($currentUntracked -join "`n")
    foreach ($inputFile in $untrackedDigests) {
        $path = Join-Path $repoRoot $inputFile.path
        if (-not [IO.File]::Exists($path) -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $inputFile.sha256) {
            $sourceUnchanged = $false
        }
    }
    $record.sourceUnchanged = $sourceUnchanged
} finally { Pop-Location }
Write-AtomicJson $recordPath $record

# A threshold failure can still have a useful native report. Always assess that
# report, preserving its bytes and the original execution status independently.
$reports = @(Get-ChildItem -LiteralPath $runOutput -Filter 'cis-abstractions-hardening.json' -File -Recurse)
if ($reports.Count -ne 1 -or $reports[0].Length -le 0 -or $reports[0].Length -gt 100MB) {
    throw "Mutation execution $($execution.status), exit $($execution.exitCode): expected exactly one bounded native report. See $recordPath"
}
$report = $reports[0]
$assessmentJson = & dotnet $cli test mutation assess --repo $repoRoot --report $report.FullName --break $breakThreshold --format json
$assessmentExit = $LASTEXITCODE
$assessment = ($assessmentJson -join [Environment]::NewLine) | ConvertFrom-Json
Write-AtomicJson "$($report.FullName).assessment.json" $assessment
$canonical = Join-Path $resolvedRoot 'cis-abstractions.json'
if (-not $Canary) {
    # Reconciliation reads the current attempt even when qualification fails.
    # Its failed workflow state remains authoritative for execution health.
    Publish-MutationResult $report $assessment (Join-Path $resolvedRoot 'latest-result.json')
}
# Preserve failed reports for diagnosis; a stopped process never replaces the
# last qualifying report or publishes a passing assessment.
if (-not $Canary -and $execution.status -eq 'exited' -and $execution.exitCode -eq 0 -and $assessmentExit -eq 0 -and $sourceUnchanged) {
    Publish-MutationResult $report $assessment $canonical
}
Write-Host "CIS mutation assessment: $($report.FullName).assessment.json"
if ($execution.status -ne 'exited' -or $execution.exitCode -ne 0 -or $assessmentExit -ne 0 -or -not $sourceUnchanged) {
    throw "Mutation qualification failed: execution=$($execution.status), nativeExit=$($execution.exitCode), assessmentExit=$assessmentExit, sourceUnchanged=$sourceUnchanged. See $recordPath"
}
}
catch {
    $record.status = 'failed'
    $record.failure = $_.Exception.Message
    $record.finishedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Write-AtomicJson $recordPath $record
    throw
}
