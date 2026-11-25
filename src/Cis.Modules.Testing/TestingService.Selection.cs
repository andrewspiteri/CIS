using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class TestingService
{
    private static TestingResult PublishScope(CisRepositoryContext context, string root, string reference,
        IReadOnlyList<TestSuiteProfile> selected, TestRunManifest manifest, IReadOnlyList<TestSuiteExecution> executions,
        IReadOnlyList<TestArtifact> artifacts, List<string> diagnostics)
    {
        try
        {
            using var publication = CisBuildLock.Acquire(context.RepositoryPath, "testing", TimeSpan.FromSeconds(5));
            if (CisPathSafety.ContainsReparsePoint(context.RepositoryPath, root))
                throw new InvalidDataException("Selected-suite evidence must stay beneath unlinked local directories.");
            CheckScopePublicationPaths(root);
            if (!CanPublishScope(root, manifest, executions, artifacts, diagnostics, out var reused))
                return Result(context, null, reference, selected, null, [], diagnostics, false, "invalid-evidence");
            if (reused) return Result(context, null, reference, selected, manifest, [], diagnostics, false, "reconciled");
            Directory.CreateDirectory(root);
            WriteNewScopeFile(Path.Combine(root, "results.json"), JsonSerializer.Serialize(executions, JsonOptions));
            WriteNewScopeFile(Path.Combine(root, "artifacts.json"), JsonSerializer.Serialize(artifacts, JsonOptions));
            WriteNewScopeFile(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
            return Result(context, null, reference, selected, manifest, [], diagnostics, true, "reconciled");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            diagnostics.Add("ERROR: Selected-suite publication was not admitted: " + exception.Message);
            return Result(context, null, reference, selected, null, [], diagnostics, false, "invalid-evidence");
        }
    }

    private static void CheckScopePublicationPaths(string root)
    {
        if (!Directory.Exists(root)) return;
        var payloads = new[] { "manifest.json", "results.json", "artifacts.json" };
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            var name = Path.GetFileName(entry);
            if (payloads.Any(payload => name.StartsWith(payload + ".", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Retain the unexplained selected-suite temporary path before retrying: " + name);
            if (payloads.Contains(name, StringComparer.OrdinalIgnoreCase)
                && (File.GetAttributes(entry) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new InvalidDataException("A selected-suite payload is not an ordinary file: " + name);
        }
    }

    private static void WriteNewScopeFile(string path, string content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var created = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                var bytes = System.Text.Encoding.UTF8.GetBytes(content);
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (created && File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static IReadOnlyList<TestSuiteProfile> SelectSuites(IReadOnlyList<TestSuiteProfile> profile,
        IReadOnlyList<string>? requested, ICollection<string> diagnostics)
    {
        if (requested is null) return profile;
        if (requested.Count is 0 or > 100 || requested.Any(id => string.IsNullOrWhiteSpace(id) || !SafeIdPattern().IsMatch(id))
            || requested.Distinct(StringComparer.OrdinalIgnoreCase).Count() != requested.Count)
        {
            diagnostics.Add("ERROR: Select one through 100 distinct valid suite IDs; duplicate or blank selections are invalid.");
            return [];
        }
        var unknown = requested.Where(id => !profile.Any(suite => suite.Id.Equals(id, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (unknown.Length > 0)
            diagnostics.Add("ERROR: Selected suites are not in the current canonical profile: " + string.Join(", ", unknown));
        return profile.Where(suite => requested.Contains(suite.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    private static bool CanPublishScope(string root, TestRunManifest manifest, IReadOnlyList<TestSuiteExecution> executions,
        IReadOnlyList<TestArtifact> artifacts, ICollection<string> diagnostics, out bool reused)
    {
        reused = false;
        var payloads = new[] { ("manifest.json", JsonSerializer.Serialize(manifest, JsonOptions)),
            ("results.json", JsonSerializer.Serialize(executions, JsonOptions)),
            ("artifacts.json", JsonSerializer.Serialize(artifacts, JsonOptions)) };
        var existing = payloads.Where(item => File.Exists(Path.Combine(root, item.Item1))).ToArray();
        if (existing.Length == 0) return true;
        if (existing.Length == payloads.Length && existing.All(item => !CisPathSafety.ContainsReparsePoint(root, Path.Combine(root, item.Item1))
            && new FileInfo(Path.Combine(root, item.Item1)).Length <= 32 * 1024 * 1024
            && File.ReadAllText(Path.Combine(root, item.Item1)) == item.Item2))
        { reused = true; return true; }
        diagnostics.Add("ERROR: Selected-suite evidence already exists with different bytes at this identity; retain it and use a new actual workflow execution.");
        return false;
    }
}
