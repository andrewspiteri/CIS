Set-StrictMode -Version Latest

function Get-MutationTreeBytes {
    param([string]$Path)
    if (-not [IO.Directory]::Exists($Path)) { return [long]0 }
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($Path)
    [long]$bytes = 0
    while ($pending.Count -gt 0) {
        try {
            $directory = [IO.DirectoryInfo]::new($pending.Pop())
            # FileSystemInfo.Attributes can return -1 after deletion, setting every
            # flag including ReparsePoint. Fresh path queries throw on disappearance.
            $attributes = [IO.File]::GetAttributes($directory.FullName)
            if ($attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Mutation storage crosses a symbolic link: $($directory.FullName)"
            }
            foreach ($entry in $directory.EnumerateFileSystemInfos()) {
                try {
                    $attributes = [IO.File]::GetAttributes($entry.FullName)
                    if ($attributes -band [IO.FileAttributes]::ReparsePoint) {
                        throw "Mutation storage contains a symbolic link: $($entry.FullName)"
                    }
                    if ($attributes -band [IO.FileAttributes]::Directory) { $pending.Push($entry.FullName) }
                    else {
                        $length = [IO.FileInfo]::new($entry.FullName).Length
                        if ($length -gt [long]::MaxValue - $bytes) { throw 'Mutation storage size exceeds the supported range.' }
                        $bytes += $length
                    }
                }
                catch [IO.FileNotFoundException], [IO.DirectoryNotFoundException] { continue }
            }
        }
        catch [IO.FileNotFoundException], [IO.DirectoryNotFoundException] { continue }
    }
    return $bytes
}

function Receive-MutationStreams($Streams) {
    foreach ($stream in $Streams) {
        if (-not $stream.done -and $stream.task.IsCompleted) {
            $read = $stream.task.GetAwaiter().GetResult()
            if ($read -eq 0) { $stream.done = $true; continue }
            $take = [Math]::Min($read, 262144 - $stream.retained)
            if ($take -gt 0) { $stream.writer.Write($stream.buffer, 0, $take); $stream.retained += $take }
            if ($take -lt $read) { $stream.truncated = $true }
            $stream.task = $stream.reader.ReadAsync($stream.buffer, 0, $stream.buffer.Length)
        }
    }
}

function Get-MutationRunBytes {
    param([string]$RunRoot, [string]$ScratchRoot)
    [long]$bytes = Get-MutationTreeBytes $RunRoot
    if (-not [string]::IsNullOrEmpty($ScratchRoot)) {
        $ancestor = $ScratchRoot
        while (-not [string]::IsNullOrEmpty($ancestor)) {
            try {
                if ([IO.File]::GetAttributes($ancestor) -band [IO.FileAttributes]::ReparsePoint) {
                    throw 'Mutation scratch storage crosses a symbolic link.'
                }
            }
            catch [IO.FileNotFoundException], [IO.DirectoryNotFoundException] { }
            $ancestor = [IO.Path]::GetDirectoryName($ancestor)
        }
        if ([IO.File]::Exists($ScratchRoot) -and -not [IO.Directory]::Exists($ScratchRoot)) {
            throw 'Mutation scratch storage is no longer a directory.'
        }
        [long]$scratchBytes = Get-MutationTreeBytes $ScratchRoot
        if ($scratchBytes -gt [long]::MaxValue - $bytes) { throw 'Mutation run size exceeds the supported range.' }
        $bytes += $scratchBytes
    }
    return $bytes
}

function Get-MutationRetainedBytes {
    param([string]$StorageRoot, [string]$ScratchRoot)
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $storage = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($StorageRoot))
    if (-not [string]::IsNullOrEmpty($ScratchRoot) -and
        -not $ScratchRoot.StartsWith($storage + [IO.Path]::DirectorySeparatorChar, $comparison)) {
        return Get-MutationRunBytes $StorageRoot $ScratchRoot
    }
    return Get-MutationTreeBytes $StorageRoot
}

function Invoke-BoundedMutationProcess {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [string]$RunRoot,
        [string]$StorageRoot,
        [ValidateRange(1, 3600)][int]$TimeoutSeconds = 1200,
        [ValidateRange(1, [long]::MaxValue)][long]$MaximumRunBytes = 536870912,
        [ValidateRange(1, [long]::MaxValue)][long]$MaximumRetainedBytes = 1073741824,
        [ValidateRange(0, [long]::MaxValue)][long]$MinimumFreeBytes = 1073741824,
        [Collections.IDictionary]$Environment = @{},
        [string]$ScratchRoot
    )
    # Polling limits are a circuit breaker, not an OS filesystem quota. A writer can
    # overshoot between samples; retain observed bytes and never call a stop a pass.
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $streams = @()
    $status = 'exited'
    $problem = $null
    $watch = [Diagnostics.Stopwatch]::StartNew()
    [long]$peakRun = 0
    [long]$peakRetained = 0
    $exitCode = $null
    $job = $null
    $sessionId = 0
    $validatedScratchRoot = $null
    try {
        if (-not [string]::IsNullOrEmpty($ScratchRoot)) {
            if (-not [IO.Path]::IsPathFullyQualified($ScratchRoot)) { throw 'Mutation scratch root must be absolute.' }
            $scratchPath = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($ScratchRoot))
            $storagePath = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($StorageRoot))
            $runPath = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RunRoot))
            $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
            $separator = [IO.Path]::DirectorySeparatorChar
            if ([string]::Equals($scratchPath, $storagePath, $comparison) -or
                $storagePath.StartsWith($scratchPath + $separator, $comparison) -or
                [string]::Equals($scratchPath, $runPath, $comparison) -or
                $scratchPath.StartsWith($runPath + $separator, $comparison) -or
                $runPath.StartsWith($scratchPath + $separator, $comparison)) {
                throw 'Mutation scratch root must be disjoint from the run root and must not contain mutation storage.'
            }
            if (-not [IO.Directory]::Exists($scratchPath)) { throw 'Mutation scratch root must be an existing owned directory.' }
            # Check links before accepting the root; rejected input must never be scanned at final accounting.
            [void](Get-MutationRunBytes $RunRoot $scratchPath)
            $validatedScratchRoot = $scratchPath
        }
        # Repeat preflight here so direct callers cannot bypass the storage guard.
        $peakRun = Get-MutationRunBytes $RunRoot $validatedScratchRoot
        $peakRetained = Get-MutationRetainedBytes $StorageRoot $validatedScratchRoot
        $existingParent = [IO.Path]::GetFullPath($RunRoot)
        while (-not [IO.Directory]::Exists($existingParent)) { $existingParent = [IO.Path]::GetDirectoryName($existingParent) }
        $drive = [IO.DriveInfo]::new($existingParent)
        $scratchDrive = if ($null -ne $validatedScratchRoot) { [IO.DriveInfo]::new($validatedScratchRoot) } else { $drive }
        if ($peakRun -ge $MaximumRunBytes -or $peakRetained -ge $MaximumRetainedBytes) {
            $status = 'storage-limit'
        }
        elseif ($drive.AvailableFreeSpace -lt $MinimumFreeBytes -or $scratchDrive.AvailableFreeSpace -lt $MinimumFreeBytes) { $status = 'disk-space' }
        if ($status -ne 'exited') {
            return [pscustomobject]@{ status = $status; exitCode = $null; durationMilliseconds = $watch.ElapsedMilliseconds
                peakRunBytes = $peakRun; peakRetainedBytes = $peakRetained; outputTruncated = $false; error = $null }
        }
        [IO.Directory]::CreateDirectory($RunRoot) | Out-Null
        $library = Join-Path $PSScriptRoot '../src/Cis.Abstractions/bin/Release/net10.0/Cis.Abstractions.dll'
        if (-not [IO.File]::Exists($library)) { throw 'Build the current CIS Abstractions library before mutation.' }
        # Do not lock an output assembly that the native build/mutator needs to
        # replace. The guard's trusted helper bytes are loaded once in memory.
        [void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes([IO.Path]::GetFullPath($library)))
        $inputPath = Join-Path $RunRoot 'worker-input.json'
        $gatePath = Join-Path $RunRoot ('launch-' + [guid]::NewGuid().ToString('N') + '.gate')
        [IO.File]::WriteAllText($inputPath, (@{ FilePath = $FilePath; Arguments = @($Arguments); WorkingDirectory = $WorkingDirectory; Environment = $Environment } | ConvertTo-Json -Depth 4))
        $worker = Join-Path $PSScriptRoot 'MutationWorker.ps1'
        $workerArguments = @((Get-Process -Id $PID).Path,
            '-NoProfile', '-NonInteractive', '-File', $worker, '-InputPath', $inputPath, '-GatePath', $gatePath)
        if ($IsWindows) {
            $job = [Cis.Abstractions.CisProcessJob]::new()
            $info.FileName = $workerArguments[0]
            foreach ($argument in $workerArguments[1..($workerArguments.Length - 1)]) { $info.ArgumentList.Add($argument) }
        }
        elseif ($IsLinux) {
            $info.FileName = (Get-Command setsid -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
            $info.ArgumentList.Add('--wait')
            $info.ArgumentList.Add('--')
            foreach ($argument in $workerArguments) { $info.ArgumentList.Add($argument) }
        }
        else { throw 'The mutation execution guard currently supports Windows and Linux.' }
        foreach ($name in @('stdout', 'stderr')) {
            $streams += @{ name = $name; reader = $null; buffer = [char[]]::new(4096); task = $null
                writer = [IO.StreamWriter]::new((Join-Path $RunRoot "$name.log"), $false, [Text.UTF8Encoding]::new($false))
                retained = 0; truncated = $false; done = $false }
        }
        if (-not $process.Start()) { throw 'Mutation process did not start.' }
        if ($IsWindows) { $job.Assign($process) }
        else {
            $sessionId = $process.Id
            $sessionWait = [Diagnostics.Stopwatch]::StartNew()
            while (-not [Cis.Abstractions.CisProcessSession]::IsSessionLeader($sessionId)) {
                if ($process.HasExited -or $sessionWait.Elapsed.TotalSeconds -gt 2) { throw 'Mutation launcher did not establish its owned process session.' }
                Start-Sleep -Milliseconds 10
            }
        }
        $streams[0].reader = $process.StandardOutput
        $streams[1].reader = $process.StandardError
        foreach ($stream in $streams) { $stream.task = $stream.reader.ReadAsync($stream.buffer, 0, $stream.buffer.Length) }
        [IO.File]::WriteAllText($gatePath, 'ready')
        [long]$nextStorageCheck = 0
        $exitedAt = $null
        while ($true) {
            Receive-MutationStreams $streams
            if ($process.HasExited) {
                if ($null -eq $exitedAt) { $exitedAt = $watch.ElapsedMilliseconds }
                $remaining = if ($IsWindows) { $job.ActiveProcessCount } else { @([Cis.Abstractions.CisProcessSession]::GetLiveProcessIds($sessionId)).Count }
                if ($remaining -eq 0 -and @($streams | Where-Object { -not $_.done }).Count -eq 0) { break }
                if ($watch.ElapsedMilliseconds - $exitedAt -gt 2000) {
                    $status = if ($remaining -gt 0) { 'child-leak' } else { 'capture-incomplete' }
                    break
                }
            }
            if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { $status = 'timeout'; break }
            if ($watch.ElapsedMilliseconds -ge $nextStorageCheck) {
                foreach ($stream in $streams) { $stream.writer.Flush() }
                $peakRun = [Math]::Max($peakRun, (Get-MutationRunBytes $RunRoot $validatedScratchRoot))
                $peakRetained = [Math]::Max($peakRetained, (Get-MutationRetainedBytes $StorageRoot $validatedScratchRoot))
                if ($peakRun -ge $MaximumRunBytes -or $peakRetained -ge $MaximumRetainedBytes) { $status = 'storage-limit'; break }
                if ($drive.AvailableFreeSpace -lt $MinimumFreeBytes -or $scratchDrive.AvailableFreeSpace -lt $MinimumFreeBytes) { $status = 'disk-space'; break }
                $nextStorageCheck = $watch.ElapsedMilliseconds + 1000
            }
            Start-Sleep -Milliseconds 10
        }
        if ($status -ne 'exited') {
            if ($IsWindows) { $job.Terminate() }
            else { [Cis.Abstractions.CisProcessSession]::Terminate($sessionId) }
            if (-not $process.WaitForExit(2000)) { throw "Process cleanup did not finish after $status." }
            $cleanupWatch = [Diagnostics.Stopwatch]::StartNew()
            do {
                $remaining = if ($IsWindows) { $job.ActiveProcessCount } else { @([Cis.Abstractions.CisProcessSession]::GetLiveProcessIds($sessionId)).Count }
                if ($remaining -eq 0) { break }
                Start-Sleep -Milliseconds 10
            } while ($cleanupWatch.Elapsed.TotalSeconds -lt 2)
            if ($remaining -gt 0) { throw "Owned child process cleanup did not finish after $status." }
            # Termination completes pending reads too. Preserve output already
            # emitted before the stop rather than abandoning those final reads.
            $drainWatch = [Diagnostics.Stopwatch]::StartNew()
            do {
                Receive-MutationStreams $streams
                if (@($streams | Where-Object { -not $_.done }).Count -eq 0) { break }
                Start-Sleep -Milliseconds 10
            } while ($drainWatch.Elapsed.TotalSeconds -lt 2)
            if (@($streams | Where-Object { -not $_.done }).Count -gt 0) { $problem = 'Stopped process output could not be completely drained.' }
        }
        if ($process.HasExited) { $exitCode = $process.ExitCode }
    }
    catch {
        $problem = $_.Exception.Message
        $status = 'infrastructure'
        try {
            if ($null -ne $job) { $job.Terminate() }
            elseif ($sessionId -gt 1) { [Cis.Abstractions.CisProcessSession]::Terminate($sessionId) }
            elseif ($process.Id -gt 0 -and -not $process.HasExited) { $process.Kill($true) }
        } catch { $problem += "; cleanup: $($_.Exception.Message)" }
    }
    finally {
        foreach ($stream in $streams) { $stream.writer.Dispose() }
        if ($null -ne $job) { $job.Dispose() }
        $process.Dispose()
        $watch.Stop()
    }
    $peakRun = [Math]::Max($peakRun, (Get-MutationRunBytes $RunRoot $validatedScratchRoot))
    $peakRetained = [Math]::Max($peakRetained, (Get-MutationRetainedBytes $StorageRoot $validatedScratchRoot))
    if ($status -eq 'exited' -and ($peakRun -ge $MaximumRunBytes -or $peakRetained -ge $MaximumRetainedBytes)) { $status = 'storage-limit' }
    return [pscustomobject]@{ status = $status; exitCode = $exitCode; durationMilliseconds = $watch.ElapsedMilliseconds
        peakRunBytes = $peakRun; peakRetainedBytes = $peakRetained
        outputTruncated = @($streams | Where-Object { $_.truncated }).Count -gt 0; error = $problem }
}

Export-ModuleMember -Function Get-MutationTreeBytes, Invoke-BoundedMutationProcess
