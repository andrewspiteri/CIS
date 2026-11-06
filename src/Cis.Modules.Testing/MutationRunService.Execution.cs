using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class MutationRunService
{
    internal static MutationNativeExecution Execute(string repository, string output, string storage,
        MutationRunProfile profile, string[] command, ICisNativeWorkScope? scope = null,
        CancellationToken cancellationToken = default, ICisOwnedStorageController? ownedStorage = null,
        NativeStorageReservation? storageReservation = null)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "cis-mutation-" + Guid.NewGuid().ToString("N"));
        var ownsReservation = storageReservation is null;
        storageReservation ??= BeginNativeStorage(repository, output, storage, profile, scope, ownedStorage);
        profile = StorageLimits(profile, storageReservation);
        string? nativeTemporary = null;
        var ownsNativeTemporary = false;
        string? scratchMarker = null;
        string? cleanupError = null;
        MutationNativeExecution? execution = null;
        var launched = false;
        IReadOnlyList<string> ownedPaths = storageReservation?.Admission.Result.Storage?.OwnedPaths ?? [];
        // The owner union includes build outputs outside the run subtree. Bound
        // its growth to this operation's actual grant, preserving restoration
        // and other reservations even while the global physical ceiling fits.
        var ownedMaximum = storageReservation?.Admission.Result.Storage is { } owned
            ? Math.Min(owned.MaximumBytes, checked(owned.OwnedBytes + storageReservation.Admission.MaximumGrowthBytes))
            : long.MaxValue;
        var arguments = command.Select(value => value.Replace("{output}", output, StringComparison.Ordinal)).ToArray();
        var segment = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var nativeTemporaryRoot = OperatingSystem.IsLinux() ? Path.GetTempPath() : storage;
        nativeTemporary = OperatingSystem.IsLinux()
            ? Path.Combine(nativeTemporaryRoot, NativeScratchPrefix(storage) + segment[..15])
            : Resolve(storage, ".tmp/" + segment);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (storageReservation is not null)
            {
                var claim = ownedStorage!.Reserve(storageReservation.AuthorityPath, storageReservation.Reference,
                    storageReservation.Reference.ExecutionId, storageReservation.Admission.MaximumGrowthBytes,
                    [new(temporary, Scratch: true)], clampToRemaining: true);
                Require(claim.Allowed, string.Join(" ", claim.Result.Errors));
                ownedPaths = claim.Result.Storage?.OwnedPaths ?? ownedPaths;
                foreach (var configured in profile.Environment.Where(item => new[] { "TEMP", "TMP", "TMPDIR" }
                    .Contains(item.Key, StringComparer.OrdinalIgnoreCase)))
                {
                    Require(Path.IsPathFullyQualified(configured.Value)
                        && ownedPaths.Any(root => CisPathSafety.IsUnderRoot(root, configured.Value, allowRoot: true))
                        && !CisPathSafety.ContainsReparsePoint(Path.GetPathRoot(configured.Value)!, configured.Value),
                        "Governed native temporary override " + configured.Key
                        + " must name an unlinked path inside its declared owned storage; retain the explicit override or resolve its storage authority before launch.");
                }
                var environmentEstimate = new Dictionary<string, string>(profile.Environment);
                foreach (var name in new[] { "TEMP", "TMP", "TMPDIR" }) environmentEstimate.TryAdd(name, nativeTemporary);
                if (scope is not null)
                {
                    environmentEstimate[CisNativeWorkEnvironment.Authority] = scope.AuthorityPath;
                    environmentEstimate[CisNativeWorkEnvironment.ParentExecution] = scope.Reference.ExecutionId;
                    environmentEstimate[CisNativeWorkEnvironment.Change] = scope.Reference.ChangeId;
                    environmentEstimate[CisNativeWorkEnvironment.Task] = scope.Reference.TaskId;
                    environmentEstimate[CisNativeWorkEnvironment.Actor] = scope.Actor;
                }
                var preparationBytes = new FileInfo(typeof(CisProcessJob).Assembly.Location).Length + 16384L
                    + JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        arguments,
                        environmentEstimate,
                        output,
                        storage,
                        working = Resolve(repository, profile.WorkingDirectory, true)
                    }, Json).LongLength;
                foreach (var name in new[] { "MutationRunner.psm1", "MutationWorker.ps1" })
                {
                    using var resource = typeof(MutationRunService).Assembly.GetManifestResourceStream("Cis.Modules.Testing." + name)
                        ?? throw new InvalidOperationException("Installed mutation guard resource is missing: " + name);
                    preparationBytes = checked(preparationBytes + resource.Length);
                }
                Require(checked(TreeBytes(storage) - storageReservation.InitialNativeBytes + preparationBytes)
                    <= storageReservation.Admission.MaximumGrowthBytes,
                    "Owned storage cannot fit the bounded trusted guard/request preparation; native work has not started.");
            }
            Directory.CreateDirectory(temporary);
            var tools = Path.Combine(temporary, "tools");
            Directory.CreateDirectory(tools);
            foreach (var name in new[] { "MutationRunner.psm1", "MutationWorker.ps1" })
            {
                using var resource = typeof(MutationRunService).Assembly.GetManifestResourceStream("Cis.Modules.Testing." + name)
                    ?? throw new InvalidOperationException("Installed mutation guard resource is missing: " + name);
                using var destination = File.Create(Path.Combine(tools, name));
                resource.CopyTo(destination);
            }
            // Only executing CIS bytes satisfy the guard's fixed helper location.
            // Neither the profile nor target repository selects an assembly to load.
            var installed = typeof(CisProcessJob).Assembly.Location;
            Require(!string.IsNullOrEmpty(installed) && File.Exists(installed), "Mutation execution requires the installed CIS helper assembly; this single-file layout is unsupported.");
            var helper = Path.Combine(temporary, "src", "Cis.Abstractions", "bin", "Release", "net10.0", "Cis.Abstractions.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(helper)!);
            File.Copy(installed, helper);
            Require(FileHash(installed) == FileHash(helper), "Installed mutation helper copy failed hash verification.");
            var request = Path.Combine(temporary, "request.json");
            var launchGate = Path.Combine(temporary, "controller.ready");
            Directory.CreateDirectory(output);
            var cancellationPath = Resolve(output, ".cancel-native");
            // Unix-domain sockets include TMPDIR in their 108-byte path limit. A repository
            // path is unbounded, so Linux scratch must use the host's short temporary root.
            // The guard accounts for this owned scratch in both budgets and checks both disks.
            if (OperatingSystem.IsLinux())
            {
                scratchMarker = Resolve(storage, "native-scratch.json");
                RecoverNativeScratch(nativeTemporaryRoot, storage, scratchMarker);
            }
            Require(!OperatingSystem.IsLinux() || Encoding.UTF8.GetByteCount(nativeTemporary) <= 40,
                "Native temporary path is too long for Unix-domain sockets. Set TMPDIR to a short, owned temporary root.");
            Require(!CisPathSafety.ContainsReparsePoint(Path.GetPathRoot(nativeTemporary)!, nativeTemporary),
                "Native temporary storage crosses a symbolic link.");
            Require(!Directory.Exists(nativeTemporary) && !File.Exists(nativeTemporary), "Native temporary output must be fresh.");
            if (storageReservation is not null)
            {
                var claim = ownedStorage!.Reserve(storageReservation.AuthorityPath, storageReservation.Reference,
                    storageReservation.Reference.ExecutionId, storageReservation.Admission.MaximumGrowthBytes,
                    [new(nativeTemporary, Scratch: true)], clampToRemaining: true);
                Require(claim.Allowed, string.Join(" ", claim.Result.Errors));
                ownedPaths = claim.Result.Storage?.OwnedPaths ?? ownedPaths;
            }
            if (scratchMarker is not null)
                AtomicJson(scratchMarker, new NativeScratch(nativeTemporary, DateTimeOffset.UtcNow.AddSeconds(profile.TimeoutSeconds + 45)));
            Directory.CreateDirectory(nativeTemporary);
            ownsNativeTemporary = true;
            var environment = new Dictionary<string, string>(profile.Environment,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var name in new[] { "TEMP", "TMP", "TMPDIR" })
            {
                environment.TryAdd(name, nativeTemporary);
            }
            if (scope is not null)
            {
                environment[CisNativeWorkEnvironment.Authority] = scope.AuthorityPath;
                environment[CisNativeWorkEnvironment.ParentExecution] = scope.Reference.ExecutionId;
                environment[CisNativeWorkEnvironment.Change] = scope.Reference.ChangeId;
                environment[CisNativeWorkEnvironment.Task] = scope.Reference.TaskId;
                environment[CisNativeWorkEnvironment.Actor] = scope.Actor;
            }
            var nativeSeconds = profile.TimeoutSeconds;
            void WriteRequest() => AtomicJson(request, new
            {
                filePath = arguments[0],
                arguments = arguments.Skip(1).ToArray(),
                workingDirectory = Resolve(repository, profile.WorkingDirectory, allowRoot: true),
                runRoot = output,
                storageRoot = storage,
                scratchRoot = nativeTemporary,
                cancellationPath,
                ownedPaths,
                maximumOwnedBytes = ownedMaximum,
                timeoutSeconds = nativeSeconds,
                maximumRunBytes = profile.MaxRunBytes,
                maximumRetainedBytes = profile.MaxRetainedBytes,
                minimumFreeBytes = profile.MinFreeBytes,
                environment,
            });
            WriteRequest();
            var driver = Path.Combine(tools, "Invoke.ps1");
            File.WriteAllText(driver, """
                param([string]$RequestPath)
                $ErrorActionPreference = 'Stop'
                Set-StrictMode -Version Latest
                $controllerGate = Join-Path (Split-Path -Parent $RequestPath) 'controller.ready'
                $gateWait = [Diagnostics.Stopwatch]::StartNew()
                while (-not [IO.File]::Exists($controllerGate)) {
                    if ($gateWait.Elapsed.TotalSeconds -ge 30) { throw 'The native controller did not admit target launch.' }
                    Start-Sleep -Milliseconds 10
                }
                Import-Module (Join-Path $PSScriptRoot 'MutationRunner.psm1') -Force
                $request = Get-Content -LiteralPath $RequestPath -Raw | ConvertFrom-Json -AsHashtable
                Set-Location -LiteralPath $request.workingDirectory
                $executable = (Get-Command -Name $request.filePath -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
                $arguments = @{
                    FilePath = [string]$executable; Arguments = [string[]]$request.arguments
                    WorkingDirectory = [string]$request.workingDirectory; RunRoot = [string]$request.runRoot
                    StorageRoot = [string]$request.storageRoot; TimeoutSeconds = [int]$request.timeoutSeconds
                    ScratchRoot = [string]$request.scratchRoot
                    CancellationPath = [string]$request.cancellationPath
                    OwnedPaths = [string[]]$request.ownedPaths
                    MaximumOwnedBytes = [long]$request.maximumOwnedBytes
                    MaximumRunBytes = [long]$request.maximumRunBytes; MaximumRetainedBytes = [long]$request.maximumRetainedBytes
                    MinimumFreeBytes = [long]$request.minimumFreeBytes
                    Environment = $request.environment
                }
                $result = Invoke-BoundedMutationProcess @arguments
                $result | Add-Member -NotePropertyName executablePath -NotePropertyValue $executable
                $result | ConvertTo-Json -Compress -Depth 8
                """, new UTF8Encoding(false));
            var start = new ProcessStartInfo("pwsh") { WorkingDirectory = repository };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", driver, "-RequestPath", request }) start.ArgumentList.Add(argument);
            var guardTimeout = TimeSpan.FromSeconds(profile.TimeoutSeconds + 15);
            if (scope is not null)
            {
                var remaining = scope.Remaining;
                Require(remaining >= TimeSpan.FromSeconds(1), "Native preparation exhausted the usable admitted window before launch.");
                nativeSeconds = Math.Min(nativeSeconds, Math.Max(1, (int)Math.Floor(remaining.TotalSeconds - Math.Min(3, remaining.TotalSeconds - 1))));
                profile = StorageLimits(profile, storageReservation, TreeBytes(temporary));
                WriteRequest();
                cancellationToken.ThrowIfCancellationRequested();
                scope.Launch();
                remaining = scope.Remaining;
                if (remaining < TimeSpan.FromSeconds(1))
                {
                    scope.Returned(true);
                    throw new InvalidDataException("Durable native launch preparation exhausted the usable window before process invocation.");
                }
                nativeSeconds = Math.Min(nativeSeconds, (int)Math.Floor(remaining.TotalSeconds));
                WriteRequest();
                guardTimeout = remaining < guardTimeout ? remaining : guardTimeout;
            }
            using var emergencyStop = new CancellationTokenSource();
            void RequestStop()
            {
                try
                {
                    if (!File.Exists(cancellationPath))
                    {
                        using var signal = new FileStream(cancellationPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    }
                }
                catch (Exception error) when (Expected(error)) { }
                emergencyStop.CancelAfter(TimeSpan.FromSeconds(Math.Max(0.05, Math.Min(5, scope?.Remaining.TotalSeconds ?? 5))));
            }
            using var cancellation = cancellationToken.Register(RequestStop);
            var driverJobName = OperatingSystem.IsWindows() ? @"Local\cis-native-" + Guid.NewGuid().ToString("N") : null;
            using var driverJob = OperatingSystem.IsWindows() ? new CisProcessJob(driverJobName) : null;
            if (scope is not null)
            {
                var remaining = scope.Remaining;
                Require(remaining >= TimeSpan.FromSeconds(1), "Native preparation exhausted its usable window before driver invocation.");
                if (remaining < guardTimeout) guardTimeout = remaining;
            }
            Exception? admissionFailure = null;
            var result = CisProcessSafety.Run(start, guardTimeout, 65536, process =>
            {
                launched = true;
                if (storageReservation is not null) storageReservation.SafelyStopped = false;
                try
                {
                    CisNativeWorkContainment? containment = null;
                    if (OperatingSystem.IsWindows())
                    {
                        driverJob!.Assign(process);
                        containment = new(CisNativeWorkContainment.WindowsOuterJob, driverJobName!, process.SessionId);
                    }
                    scope?.Started(process.Id, process.StartTime.ToUniversalTime().ToString("O"), containment);
                    if (scope is not null)
                    {
                        var remaining = scope.Remaining;
                        Require(remaining >= TimeSpan.FromSeconds(1), "Durable native process bookkeeping exhausted its admitted window before target launch.");
                        nativeSeconds = Math.Min(nativeSeconds,
                            Math.Max(1, (int)Math.Floor(remaining.TotalSeconds - Math.Min(3, remaining.TotalSeconds - 1))));
                        WriteRequest();
                    }
                }
                catch (Exception error) when (Expected(error)) { admissionFailure = error; RequestStop(); }
                File.WriteAllText(launchGate, "ready");
            }, emergencyStop.Token);
            if (result.TimedOut || result.Cancelled || result.OutputTruncated || result.ExitCode != 0)
            {
                execution = new(result.Cancelled ? "cancelled" : "infrastructure", result.ExitCode, 0, 0, 0, result.OutputTruncated,
                    result.TimedOut ? "Mutation guard exceeded its own deadline." : result.Cancelled ? "Native work was cancelled." : "Mutation guard failed: " + result.StandardError)
                { SafelyStopped = !launched && result.SafelyStopped };
            }
            else
            {
                execution = JsonSerializer.Deserialize<MutationNativeExecution>(result.StandardOutput, Json)
                    ?? throw new InvalidDataException("Mutation guard returned no execution result.");
                execution = execution with
                {
                    CisToolAssemblySha256 = FileHash(helper),
                    Command = new[] { execution.ExecutablePath ?? arguments[0] }.Concat(arguments.Skip(1)).ToArray(),
                    SafelyStopped = execution.SafelyStopped && result.SafelyStopped,
                };
            }
            execution = execution with { WorkReference = scope?.Reference, WorkCoverage = scope is null ? "unaccounted-non-adopted" : "governed-native" };
            if (admissionFailure is not null)
                execution = execution with { Status = "infrastructure", Error = "Native target launch was withheld: " + admissionFailure.Message };
            if (storageReservation is not null) storageReservation.SafelyStopped = execution.SafelyStopped;
            if (execution.SafelyStopped) scope?.Returned(true);
            else scope?.MarkUncertain("Native driver or its owned process job/session did not provide complete stopped evidence.");
        }
        finally
        {
            if (!launched) scope?.Returned(true);
            if (ownsNativeTemporary && (execution?.SafelyStopped == true || !launched))
            {
                try { DeleteOwnedNativeTemporary(OperatingSystem.IsLinux() ? Path.GetTempPath() : storage, nativeTemporary!); }
                catch (Exception error) when (Expected(error)) { cleanupError = "Owned native temporary cleanup failed: " + error.Message; }
            }
            if (scratchMarker is not null && cleanupError is null && ownsNativeTemporary
                && (execution?.SafelyStopped == true || !launched)) File.Delete(scratchMarker);
            // Remove the separately extracted trusted guard files.
            if ((execution?.SafelyStopped == true || !launched) && Directory.Exists(temporary) && !CisPathSafety.IsReparsePoint(temporary)
                && !CisPathSafety.ContainsReparsePointInTree(temporary)) Directory.Delete(temporary, recursive: true);
            if (ownsReservation)
                SettleNativeStorage(storageReservation, ownedStorage, execution?.SafelyStopped == true || !launched);
        }
        Require(execution is not null, "Mutation execution returned no result.");
        return cleanupError is null ? execution : execution with
        {
            Status = "infrastructure",
            Error = execution.Error is null ? cleanupError : execution.Error + "; " + cleanupError,
        };
    }

    private sealed record NativeScratch(string Path, DateTimeOffset UntilUtc);

    private static string NativeScratchPrefix(string storage)
        => "cis-" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(storage))))[..6] + "-";

    private static void RecoverNativeScratch(string root, string storage, string marker)
    {
        if (!File.Exists(marker)) return;
        Require(!CisPathSafety.IsReparsePoint(marker) && new FileInfo(marker).Length <= 4096, "Invalid native scratch recovery marker.");
        var prior = JsonSerializer.Deserialize<NativeScratch>(File.ReadAllText(marker), Json)
            ?? throw new InvalidDataException("Invalid native scratch recovery marker.");
        Require(prior.UntilUtc <= DateTimeOffset.UtcNow, "Previous native scratch may still be active until " + prior.UntilUtc.ToString("O") + ".");
        Require(string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(prior.Path) ?? ""),
            Path.TrimEndingDirectorySeparator(root), StringComparison.Ordinal)
            && Path.GetFileName(prior.Path).StartsWith(NativeScratchPrefix(storage), StringComparison.Ordinal)
            && System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(prior.Path), @"\Acis-[a-f0-9]{6}-[A-Za-z0-9_-]{15}\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
            "Native scratch recovery requires its original short temporary root.");
        DeleteOwnedNativeTemporary(root, prior.Path);
        File.Delete(marker);
    }

    private static void DeleteOwnedNativeTemporary(string storage, string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return; }
        Require(attributes.HasFlag(FileAttributes.Directory) && !attributes.HasFlag(FileAttributes.ReparsePoint)
            && !CisPathSafety.IsReparsePoint(storage) && !CisPathSafety.ContainsReparsePoint(storage, path)
            && !CisPathSafety.ContainsReparsePointInTree(path), "Owned native temporary output is no longer an unlinked directory inside mutation storage.");
        Directory.Delete(path, recursive: true);
    }

    private static string FileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
    }

    private static void AtomicJson<T>(string path, T value)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
