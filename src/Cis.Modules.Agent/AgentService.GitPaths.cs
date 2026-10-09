using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed partial class AgentService
{
    private static bool TryReadChangedFiles(string path, out IReadOnlyList<string> changes)
    {
        changes = [];
        var status = Git(path, ["status", "--porcelain=v1", "--untracked-files=all", "-z"]);
        if (status.ExitCode != 0 || status.TimedOut || status.OutputTruncated) return false;
        var records = status.StandardOutput.Split('\0');
        if (records[^1].Length != 0) return false;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < records.Length - 1; index++)
        {
            var record = records[index];
            if (record.Length < 4 || record[2] != ' ' || !SafeRelative(record[3..])) return false;
            paths.Add(record[3..]);
            // Porcelain -z emits destination first, then the original path for renames/copies.
            if (record[..2].Any(character => character is 'R' or 'C'))
            {
                if (++index >= records.Length - 1 || !SafeRelative(records[index])) return false;
                paths.Add(records[index]); // Both sides belong to the write scope.
            }
        }
        changes = paths.Order(StringComparer.Ordinal).ToArray();
        return true;
    }
    private IReadOnlyList<CisRepositoryInput>? CaptureUnversionedInputs(string path)
    {
        // An existing (even broken) Git repository must use Git verification, never this fallback.
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (Path.Exists(Path.Combine(directory.FullName, ".git"))) return null;
        try
        {
            var context = _resolver.Resolve(path).Context;
            return context is null ? null : CisExecutionIdentity.CaptureInputs(context, includeDossiers: true);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { return null; }
    }

    private bool TryReadExecutionChanges(string path, IReadOnlyList<CisRepositoryInput>? baseline,
        out IReadOnlyList<string> changes)
    {
        if (baseline is null) return TryReadChangedFiles(path, out changes);
        changes = [];
        var current = CaptureUnversionedInputs(path);
        if (current is null) return false;
        var before = baseline.ToDictionary(input => input.Path, input => input.Digest, StringComparer.Ordinal);
        var after = current.ToDictionary(input => input.Path, input => input.Digest, StringComparer.Ordinal);
        changes = before.Keys.Union(after.Keys, StringComparer.Ordinal)
            .Where(path => !before.TryGetValue(path, out var oldDigest)
                || !after.TryGetValue(path, out var newDigest) || oldDigest != newDigest)
            .Order(StringComparer.Ordinal).ToArray();
        return true;
    }

}
