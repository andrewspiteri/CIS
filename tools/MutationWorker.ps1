param(
    [Parameter(Mandatory)][string]$InputPath,
    [Parameter(Mandatory)][string]$GatePath,
    [ValidateRange(1, 60)][int]$GateTimeoutSeconds = 15
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# The parent owns these run-local files. No application process is created until
# the parent has assigned this trusted worker to its job/session and opens the gate.
$command = Get-Content -LiteralPath $InputPath -Raw | ConvertFrom-Json -AsHashtable
$watch = [Diagnostics.Stopwatch]::StartNew()
while (-not [IO.File]::Exists($GatePath)) {
    if ($watch.Elapsed.TotalSeconds -ge $GateTimeoutSeconds) { throw 'Mutation launch gate was not released.' }
    Start-Sleep -Milliseconds 10
}
$info = [Diagnostics.ProcessStartInfo]::new([string]$command.FilePath)
$info.WorkingDirectory = [string]$command.WorkingDirectory
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
foreach ($argument in $command.Arguments) { $info.ArgumentList.Add([string]$argument) }
if ($command.ContainsKey('Environment') -and $null -ne $command.Environment) {
    foreach ($entry in $command.Environment.GetEnumerator()) {
        $info.Environment[[string]$entry.Key] = [string]$entry.Value
    }
}
$process = [Diagnostics.Process]::new()
$process.StartInfo = $info
try {
    if (-not $process.Start()) { throw 'Mutation command did not start.' }
    $stdout = $process.StandardOutput.BaseStream.CopyToAsync([Console]::OpenStandardOutput())
    $stderr = $process.StandardError.BaseStream.CopyToAsync([Console]::OpenStandardError())
    $process.WaitForExit()
    if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout, $stderr), 2000)) {
        [Console]::Error.WriteLine('CIS capture failure: command streams remained open after exit.')
        exit 125
    }
    exit $process.ExitCode
}
finally { $process.Dispose() }
