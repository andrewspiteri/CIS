using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class MutationRunService
{
    internal static MutationNativeExecution Execute(string repository, string output, string storage,
        MutationRunProfile profile, string[] command)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "cis-mutation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        string? nativeTemporary = null;
        var ownsNativeTemporary = false;
        string? scratchMarker = null;
        string? cleanupError = null;
        MutationNativeExecution? execution = null;
        try
        {
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
            var arguments = command.Select(value => value.Replace("{output}", output, StringComparison.Ordinal)).ToArray();
            Directory.CreateDirectory(output);
            // Unix-domain sockets include TMPDIR in their 108-byte path limit. A repository
            // path is unbounded, so Linux scratch must use the host's short temporary root.
            // The guard accounts for this owned scratch in both budgets and checks both disks.
            var segment = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var nativeTemporaryRoot = OperatingSystem.IsLinux() ? Path.GetTempPath() : storage;
            if (OperatingSystem.IsLinux())
            {
                scratchMarker = Resolve(storage, "native-scratch.json");
                RecoverNativeScratch(nativeTemporaryRoot, storage, scratchMarker);
            }
            nativeTemporary = OperatingSystem.IsLinux()
                ? Path.Combine(nativeTemporaryRoot, NativeScratchPrefix(storage) + segment[..15])
                : Resolve(storage, ".tmp/" + segment);
            Require(!OperatingSystem.IsLinux() || Encoding.UTF8.GetByteCount(nativeTemporary) <= 40,
                "Native temporary path is too long for Unix-domain sockets. Set TMPDIR to a short, owned temporary root.");
            Require(!CisPathSafety.ContainsReparsePoint(Path.GetPathRoot(nativeTemporary)!, nativeTemporary),
                "Native temporary storage crosses a symbolic link.");
            Require(!Directory.Exists(nativeTemporary) && !File.Exists(nativeTemporary), "Native temporary output must be fresh.");
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
            AtomicJson(request, new
            {
                filePath = arguments[0], arguments = arguments.Skip(1).ToArray(),
                workingDirectory = Resolve(repository, profile.WorkingDirectory, allowRoot: true),
                runRoot = output, storageRoot = storage, scratchRoot = nativeTemporary,
                timeoutSeconds = profile.TimeoutSeconds, maximumRunBytes = profile.MaxRunBytes,
                maximumRetainedBytes = profile.MaxRetainedBytes, minimumFreeBytes = profile.MinFreeBytes,
                environment,
            });
            var driver = Path.Combine(tools, "Invoke.ps1");
            File.WriteAllText(driver, """
                param([string]$RequestPath)
                $ErrorActionPreference = 'Stop'
                Set-StrictMode -Version Latest
                Import-Module (Join-Path $PSScriptRoot 'MutationRunner.psm1') -Force
                $request = Get-Content -LiteralPath $RequestPath -Raw | ConvertFrom-Json -AsHashtable
                Set-Location -LiteralPath $request.workingDirectory
                $executable = (Get-Command -Name $request.filePath -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
                $arguments = @{
                    FilePath = [string]$executable; Arguments = [string[]]$request.arguments
                    WorkingDirectory = [string]$request.workingDirectory; RunRoot = [string]$request.runRoot
                    StorageRoot = [string]$request.storageRoot; TimeoutSeconds = [int]$request.timeoutSeconds
                    ScratchRoot = [string]$request.scratchRoot
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
            var result = CisProcessSafety.Run(start, TimeSpan.FromSeconds(profile.TimeoutSeconds + 15), 65536);
            if (result.TimedOut || result.OutputTruncated || result.ExitCode != 0)
            {
                execution = new("infrastructure", result.ExitCode, 0, 0, 0, result.OutputTruncated,
                    result.TimedOut ? "Mutation guard exceeded its own deadline." : "Mutation guard failed: " + result.StandardError);
            }
            else
            {
                execution = JsonSerializer.Deserialize<MutationNativeExecution>(result.StandardOutput, Json)
                    ?? throw new InvalidDataException("Mutation guard returned no execution result.");
                execution = execution with
                {
                    CisToolAssemblySha256 = FileHash(helper),
                    Command = new[] { execution.ExecutablePath ?? arguments[0] }.Concat(arguments.Skip(1)).ToArray(),
                };
            }
        }
        finally
        {
            if (ownsNativeTemporary)
            {
                try { DeleteOwnedNativeTemporary(OperatingSystem.IsLinux() ? Path.GetTempPath() : storage, nativeTemporary!); }
                catch (Exception error) when (Expected(error)) { cleanupError = "Owned native temporary cleanup failed: " + error.Message; }
            }
            if (scratchMarker is not null && cleanupError is null && ownsNativeTemporary) File.Delete(scratchMarker);
            // Remove the separately extracted trusted guard files.
            if (Directory.Exists(temporary) && !CisPathSafety.IsReparsePoint(temporary)
                && !CisPathSafety.ContainsReparsePointInTree(temporary)) Directory.Delete(temporary, recursive: true);
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
