using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

// References provide evidence; discovered guidance has its own editable proposal.
internal static class RepositoryGuidanceContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
    private const int MaxFiles = 16;
    private const int ExcerptCharacters = 2_000;

    internal static RepositoryFileMerge[] Attach(IReadOnlyList<RepositoryFileMerge> merges)
    {
        return merges.Select(merge =>
        {
            var warnings = new List<string>();
            var candidates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in RepositoryGuidanceDiscovery.References(merge.RepositoryPath, merge.RelativePath, merge.CurrentContent))
                candidates.Add(path);
            foreach (var sibling in merges.Where(item => RepositoryAgentsMerge.IsEntryPoint(item.RelativePath)))
            {
                candidates.Add(sibling.RelativePath);
                foreach (var path in RepositoryGuidanceDiscovery.References(merge.RepositoryPath, sibling.RelativePath, sibling.CurrentContent))
                    candidates.Add(path);
            }
            candidates.Remove(merge.RelativePath);
            var sources = new List<RepositoryGuidanceReference>();
            var categoryCounts = new Dictionary<int, int>();
            var omitted = false;
            foreach (var path in candidates.OrderBy(Priority).ThenBy(path => path, StringComparer.Ordinal))
            {
                if (!CisPathSafety.TryResolveUnderRoot(merge.RepositoryPath, path, out var absolute)
                    || CisPathSafety.ContainsReparsePoint(merge.RepositoryPath, absolute))
                {
                    warnings.Add($"Reference not read because its path is outside the repository or symbolic: {path}");
                    continue;
                }
                if (!File.Exists(absolute)) continue;
                var category = Priority(path);
                if (sources.Count == MaxFiles || categoryCounts.GetValueOrDefault(category) >= (category is 1 or 2 ? 4 : 3))
                {
                    omitted = true;
                    continue;
                }
                try
                {
                    if (new FileInfo(absolute).Length > 256_000)
                    {
                        warnings.Add($"Reference not read because it exceeds 256 KB: {path}");
                        continue;
                    }
                    var content = File.ReadAllText(absolute);
                    categoryCounts[category] = categoryCounts.GetValueOrDefault(category) + 1;
                    if (RepositoryGuidanceReconciler.Sensitive(content))
                    {
                        warnings.Add($"Reference excluded because it may contain credentials: {path}");
                        sources.Add(new(path, "[Content excluded: possible credentials.]", RepositoryAgentsMerge.Hash(content), true));
                        continue;
                    }
                    var excerpt = content.Length > ExcerptCharacters;
                    sources.Add(new(path, excerpt ? Excerpt(content) : content, RepositoryAgentsMerge.Hash(content), excerpt));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Reference could not be read: {path}");
                }
            }
            if (omitted) warnings.Add($"Reference context is limited to {MaxFiles} files with category limits. Other linked guidance has not been inspected.");
            if (sources.Any(source => source.IsExcerpt))
                warnings.Add("Read-only reference excerpts are bounded evidence, not a full review of linked files or their dependencies.");
            return merge with
            {
                ContextSources = sources,
                ContextWarnings = warnings,
                ReviewHash = RepositoryAgentsMerge.Hash(JsonSerializer.Serialize(new { merge.ReviewHash, sources, warnings })),
            };
        }).ToArray();
    }

    private static int Priority(string path) => RepositoryAgentsMerge.IsEntryPoint(path) ? 0
        : path.StartsWith(".github/scripts/", StringComparison.Ordinal) ? 1
        : path.StartsWith(".github/workflows/", StringComparison.Ordinal) ? 1
        : path.StartsWith(".github/agents/", StringComparison.Ordinal) ? 2
        : path.Contains("/SKILL.md", StringComparison.Ordinal)
            && Regex.IsMatch(path, @"(?i)contract|domain|dictionar|govern|workflow|context|policy", RegexOptions.CultureInvariant, Timeout) ? 2
        : path.EndsWith("brd-spec.md", StringComparison.Ordinal) || path.EndsWith("technical-intent-spec.md", StringComparison.Ordinal) ? 3
        : path.StartsWith(".github/instructions/", StringComparison.Ordinal) ? 4
        : path.StartsWith("docs/", StringComparison.Ordinal) ? 5 : 6;

    private static string Excerpt(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var selected = new SortedSet<int>(Enumerable.Range(0, Math.Min(12, lines.Length)));
        for (var index = 12; index < lines.Length; index++)
            if (Regex.IsMatch(lines[index], @"(?i)\b(first|routing|graph|preflight|source.of.truth|canonical|authority|must|required|catalogue|governance)\b",
                RegexOptions.CultureInvariant, Timeout)) selected.Add(index);
        var excerpt = string.Join("\n", selected.Select(index => $"L{index + 1}: {lines[index]}"));
        return excerpt[..Math.Min(ExcerptCharacters, excerpt.Length)] + "\n[Excerpt only; omitted lines are not reviewed.]";
    }
}
