using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

/// <summary>Reads a reviewed C# production scope and asks Git for actual changed lines, including new files.</summary>
internal sealed partial class ChangedProductionScope
{
    public const string RelativePath = ".cis/coverage-scope.json";
    public string BaseRevision { get; }
    public IReadOnlyDictionary<string, HashSet<int>> Lines { get; }

    private ChangedProductionScope(string revision, Dictionary<string, HashSet<int>> lines)
        => (BaseRevision, Lines) = (revision, lines);

    public static ChangedProductionScope Read(string repository)
    {
        var path = Path.Combine(repository, RelativePath);
        if (CisPathSafety.ContainsReparsePoint(repository, path) || new FileInfo(path).Length > 64 * 1024)
            throw new InvalidDataException("Changed coverage scope must be a bounded regular repository file.");
        var scope = JsonSerializer.Deserialize<Scope>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (scope is not { SchemaVersion: 1, ProductionPaths.Count: > 0 } || scope.ProductionPaths.Count > 100
            || scope.BaseRevision is null || !RevisionPattern().IsMatch(scope.BaseRevision))
            throw new InvalidDataException("Coverage scope requires schemaVersion=1, an exact Git commit ID and explicit productionPaths.");
        var roots = scope.ProductionPaths.Select(relative =>
            CisPathSafety.TryResolveUnderRoot(repository, relative, out var root) && !CisPathSafety.ContainsReparsePoint(repository, root)
                ? root : throw new InvalidDataException("Production scope must stay inside the repository without linked paths.")).ToArray();
        if (Git(repository, "rev-parse", "--verify", scope.BaseRevision + "^{commit}").Trim() != scope.BaseRevision)
            throw new InvalidDataException("Coverage base revision must identify a retained commit.");
        var tracked = Git(repository, "diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--name-only", "-z", "--diff-filter=ACM", scope.BaseRevision, "--");
        var untracked = Git(repository, "ls-files", "--others", "--exclude-standard", "-z").Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var files = tracked.Split('\0', StringSplitOptions.RemoveEmptyEntries).Concat(untracked).Distinct(StringComparer.Ordinal).ToArray();
        if (files.Length > 2_000) throw new InvalidDataException("Changed coverage exceeds the 2,000-file assessment bound.");
        var result = new Dictionary<string, HashSet<int>>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var relative in files.Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            if (!CisPathSafety.TryResolveUnderRoot(repository, relative, out var source)
                || CisPathSafety.ContainsReparsePoint(repository, source)) throw new InvalidDataException("Changed source path is unsafe.");
            if (!roots.Any(root => source == root || CisPathSafety.IsUnderRoot(root, source))) continue;
            if (!File.Exists(source) || new FileInfo(source).Length > 4 * 1024 * 1024)
                throw new InvalidDataException("Changed source is missing or exceeds 4 MiB.");
            var lines = new HashSet<int>();
            if (untracked.Contains(relative)) lines.UnionWith(Enumerable.Range(1, File.ReadLines(source).Count()));
            else
            {
                var diff = Git(repository, "diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--unified=0", scope.BaseRevision, "--", ":(literal)" + relative);
                foreach (Match hunk in HunkPattern().Matches(diff))
                {
                    var first = int.Parse(hunk.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    var count = hunk.Groups[2].Success ? int.Parse(hunk.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : 1;
                    if (count > 100_000) throw new InvalidDataException("Changed coverage exceeds the per-file line bound.");
                    lines.UnionWith(Enumerable.Range(first, count));
                }
            }
            if (lines.Count > 0) result[source] = lines;
        }
        return new(scope.BaseRevision, result);
    }

    private static string Git(string repository, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = repository };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        try
        {
            var result = CisProcessSafety.Run(start, TimeSpan.FromSeconds(15));
            if (result.TimedOut || result.OutputTruncated || result.ExitCode != 0)
                throw new InvalidDataException("Git could not establish complete changed-production coverage inputs.");
            return result.StandardOutput;
        }
        catch (System.ComponentModel.Win32Exception exception) { throw new InvalidDataException("Git is required for changed-production coverage.", exception); }
    }

    private sealed record Scope(int SchemaVersion, string BaseRevision, IReadOnlyList<string> ProductionPaths);
    [GeneratedRegex("^[a-f0-9]{40}$|^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex RevisionPattern();
    [GeneratedRegex(@"(?m)^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@", RegexOptions.CultureInvariant)]
    private static partial Regex HunkPattern();
}
