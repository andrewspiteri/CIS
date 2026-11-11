using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class IncrementalTestService
{
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static void PrepareReportMutations(Input input, IncrementalTestState state,
        IEnumerable<IncrementalTestBatch> batches, IncrementalTestOptions options)
    {
        if (input.Work is null) return;
        var selected = batches.ToArray();
        var active = selected.Select(batch => batch.Id).ToHashSet(StringComparer.Ordinal);
        var keep = state.Batches.Where(pair => active.Contains(pair.Key)).SelectMany(pair =>
                new[] { pair.Value.Unit?.ReportPath, pair.Value.Mutation?.ReportPath })
            .Where(path => !string.IsNullOrEmpty(path)).Select(path => Safe(input.Root, path!)).ToHashSet(PathComparer);
        var reports = Safe(input.Root, "reports");
        var retired = Directory.Exists(reports) ? CisPathSafety.EnumerateFiles(reports).Where(path => !keep.Contains(path)) : [];
        var replacement = selected.Where(batch => !batch.UnitCurrent
                || options.Layer != "unit" && input.Profile.MutationCommand is not null && !batch.MutationCurrent)
            .Take(options.MaximumBatches).SelectMany(batch => new[]
            {
                !batch.UnitCurrent ? Safe(input.Root, "reports/" + batch.Id + "-unit.xml") : null,
                options.Layer != "unit" && input.Profile.MutationCommand is not null
                    && (!batch.MutationCurrent || !batch.UnitCurrent && state.Batches.GetValueOrDefault(batch.Id)?.Unit is not null)
                    ? Safe(input.Root, "reports/" + batch.Id + "-mutation.json") : null
            }).Where(path => path is not null).Select(path => path!);
        foreach (var path in retired.Concat(replacement).Where(File.Exists).Distinct(PathComparer))
        {
            using var reservation = MutationRunService.BeginEvidenceMutation(input.Work, input.OwnedStorage, [path]);
            var settled = MutationRunService.SettleNativeStorage(reservation, input.OwnedStorage, true);
            Require(settled?.Allowed != false, string.Join(" ", settled?.Errors ?? []));
        }
    }

    private static void PruneRetired(Input input, IncrementalTestState state, IEnumerable<string> active)
    {
        var keep = active.ToHashSet(StringComparer.Ordinal);
        foreach (var id in state.Batches.Keys.Where(id => !keep.Contains(id)).ToArray()) state.Batches.Remove(id);
        var reports = Safe(input.Root, "reports");
        if (!Directory.Exists(reports)) return;
        Require(!CisPathSafety.ContainsReparsePointInTree(reports), "Linked result storage cannot be pruned.");
        var referenced = state.Batches.Values.SelectMany(batch => new[] { batch.Unit?.ReportPath, batch.Mutation?.ReportPath })
            .Where(path => !string.IsNullOrEmpty(path)).Select(path => Safe(input.Root, path!)).ToHashSet(PathComparer);
        foreach (var file in CisPathSafety.EnumerateFiles(reports))
        {
            if (referenced.Contains(file)) continue;
            using var reservation = MutationRunService.BeginEvidenceMutation(input.Work, input.OwnedStorage, [file]);
            File.Delete(file);
            var settled = MutationRunService.SettleNativeStorage(reservation, input.OwnedStorage, true,
                supersededRequiredPaths: [file]);
            Require(settled?.Allowed != false, string.Join(" ", settled?.Errors ?? []));
        }
    }
    private static void Save(Input input, IncrementalTestState state) => Write(input, input.StatePath, state);
    private static void SaveLatest(Input input, IncrementalTestResult result) => Write(input, Safe(input.Root, "latest-run.json"), result);

    private static void Write<T>(Input input, string path, T value)
    {
        Require(input.Work is null || input.Work.Remaining > TimeSpan.Zero,
            "The admitted metadata preparation window ended before publication; inspect the existing owner allowance.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        Require(bytes.Length <= 16 * 1024 * 1024, "Saved incremental metadata exceeds its size limit.");
        Directory.CreateDirectory(input.Root);
        var limit = input.ActiveStorage is { } active
            ? Math.Min(input.Profile.MaxRetainedBytes, active.InitialNativeBytes + active.Admission.MaximumGrowthBytes)
            : input.Profile.MaxRetainedBytes;
        Require(Bytes(input.Storage) + bytes.Length <= limit, "Incremental storage cap prevents publication.");
        Require(new DriveInfo(input.Storage).AvailableFreeSpace - bytes.Length >= input.Profile.MinFreeBytes, "Incremental free-space reserve prevents publication.");
        if (input.ActiveStorage is null && input.Work is not null)
        {
            var next = value as IncrementalTestState;
            var required = next is null ? [] : RequiredReports(input, next);
            var superseded = next is { RecoveryRequired: false, ActiveExecutionUntilUtc: null }
                ? input.PreviousRequiredReports.Except(required, PathComparer).ToArray() : [];
            MutationRunService.OwnedJson(path, value, input.Repository, input.Work, input.OwnedStorage, required, superseded,
                next is null ? null : RetainedWorkReferences(next));
        }
        else
        {
            var temporary = Safe(input.Root, Path.GetFileName(path) + ".tmp");
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }
    }

    private static CisOwnedStorageResult? FinishNativeStorage(Input input, IncrementalTestState? state)
    {
        var reservation = input.ActiveStorage;
        if (reservation is null) return null;
        var required = (state is null ? [] : RequiredReports(input, state))
            .Append(input.StatePath).Distinct(PathComparer).ToArray();
        var superseded = state is { RecoveryRequired: false, ActiveExecutionUntilUtc: null }
            ? input.PreviousRequiredReports.Except(required, PathComparer).ToArray() : [];
        try { return MutationRunService.SettleNativeStorage(reservation, input.OwnedStorage, reservation.SafelyStopped, required, superseded); }
        finally { input.ActiveStorage = null; reservation.Dispose(); }
    }

    private static string[] RequiredReports(Input input, IncrementalTestState state)
        => state.Batches.Values.SelectMany(batch => new[] { batch.Unit?.ReportPath, batch.Mutation?.ReportPath })
            .Where(path => !string.IsNullOrEmpty(path)).Select(path => Safe(input.Root, path!))
            .Distinct(PathComparer).ToArray();

    private static CisAgentWorkReference[] RetainedWorkReferences(IncrementalTestState state)
    {
        var references = new HashSet<CisAgentWorkReference>();
        if (state.WorkReference is { } original) references.Add(original);
        if (state.ActiveWorkReference is { } active) references.Add(active);
        foreach (var batch in state.Batches.Values)
            foreach (var stage in new[] { batch.Unit, batch.Mutation })
                if (stage is not null)
                    foreach (var execution in new[] { stage.Execution, stage.RecoveryExecution, stage.PostflightExecution })
                    {
                        if (execution?.WorkReference is { } work) references.Add(work);
                        if (execution?.SettlementReference is { } settlement) references.Add(settlement);
                    }
        return references.ToArray();
    }

    private static string Work(Input input)
    {
        var path = Safe(input.Root, "work/" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CleanWork(Input input, bool retainLogs = false)
    {
        var path = Safe(input.Root, "work");
        if (!Directory.Exists(path)) return;
        Require(!CisPathSafety.ContainsReparsePointInTree(path), "Linked native work cannot be removed safely.");
        if (retainLogs)
        {
            foreach (var name in new[] { "stdout.log", "stderr.log" })
            {
                var source = CisPathSafety.EnumerateFiles(path).FirstOrDefault(file => Path.GetFileName(file) == name);
                if (source is null) continue;
                using var stream = File.OpenRead(source);
                stream.Position = Math.Max(0, stream.Length - 128 * 1024);
                using var destination = File.Create(Safe(input.Root, "latest-failure-" + name));
                stream.CopyTo(destination);
            }
        }
        Directory.Delete(path, true);
    }

    private static long Bytes(string root)
    {
        if (!Directory.Exists(root)) return 0;
        Require(!CisPathSafety.ContainsReparsePointInTree(root), "Linked testing storage cannot be measured safely.");
        return CisPathSafety.EnumerateFiles(root).Sum(path => new FileInfo(path).Length);
    }
}
