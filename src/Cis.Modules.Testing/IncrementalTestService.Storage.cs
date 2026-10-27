using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class IncrementalTestService
{
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static void PruneRetired(Input input, IncrementalTestState state, IEnumerable<string> active)
    {
        var keep = active.ToHashSet(StringComparer.Ordinal);
        foreach (var id in state.Batches.Keys.Where(id => !keep.Contains(id)).ToArray()) state.Batches.Remove(id);
        var reports = Safe(input.Root, "reports");
        if (!Directory.Exists(reports)) return;
        Require(!CisPathSafety.ContainsReparsePointInTree(reports), "Linked result storage cannot be pruned.");
        var referenced = state.Batches.Values.SelectMany(batch => new[] { batch.Unit?.ReportPath, batch.Mutation?.ReportPath })
            .Where(path => !string.IsNullOrEmpty(path)).Select(path => Safe(input.Root, path!)).ToHashSet(PathComparer);
        foreach (var file in CisPathSafety.EnumerateFiles(reports)) if (!referenced.Contains(file)) File.Delete(file);
    }
    private static void Save(Input input, IncrementalTestState state) => Write(input, input.StatePath, state);
    private static void SaveLatest(Input input, IncrementalTestResult result) => Write(input, Safe(input.Root, "latest-run.json"), result);

    private static void Write<T>(Input input, string path, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        Require(bytes.Length <= 16 * 1024 * 1024, "Saved incremental metadata exceeds its size limit.");
        Directory.CreateDirectory(input.Root);
        Require(Bytes(input.Storage) + bytes.Length <= input.Profile.MaxRetainedBytes, "Incremental storage cap prevents publication.");
        Require(new DriveInfo(input.Storage).AvailableFreeSpace - bytes.Length >= input.Profile.MinFreeBytes, "Incremental free-space reserve prevents publication.");
        var temporary = Safe(input.Root, Path.GetFileName(path) + ".tmp");
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, true);
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
