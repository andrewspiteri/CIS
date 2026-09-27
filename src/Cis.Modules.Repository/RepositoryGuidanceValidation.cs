using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

internal static class RepositoryGuidanceValidation
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    internal static RepositoryFileMerge[] AttachAutomation(IReadOnlyList<RepositoryFileMerge> merges)
    {
        return merges.GroupBy(merge => merge.RepositoryPath).SelectMany(group =>
        {
            var sources = new List<RepositoryGuidanceReference>();
            var warnings = new List<string>();
            var size = 0L;
            foreach (var directory in new[] { ".github/workflows", ".github/scripts" })
            {
                if (!CisPathSafety.TryResolveUnderRoot(group.Key, directory, out var absolute)
                    || !Directory.Exists(absolute) || CisPathSafety.ContainsReparsePoint(group.Key, absolute)) continue;
                Visit(absolute);
            }
            return group.Select(merge => merge with
            {
                AutomationSources = sources,
                ContextWarnings = [.. merge.ContextWarnings, .. warnings.Distinct()],
                ReviewHash = RepositoryAgentsMerge.Hash(JsonSerializer.Serialize(new { merge.ReviewHash,
                    automation = sources.Select(source => new { source.Path, source.ContentHash }), warnings })),
            });

            void Visit(string directory)
            {
                try
                {
                    foreach (var path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
                    {
                        if (CisPathSafety.ContainsReparsePoint(group.Key, path))
                        { warnings.Add("Automation dependency coverage is incomplete: symbolic paths were excluded."); continue; }
                        if (Directory.Exists(path)) { Visit(path); continue; }
                        if (Path.GetExtension(path) is not (".yml" or ".yaml" or ".py" or ".ps1" or ".sh" or ".js" or ".json")) continue;
                        var length = new FileInfo(path).Length;
                        if (length > 256_000 || size + length > 4_000_000 || sources.Count >= 256)
                        { warnings.Add("Automation dependency coverage is incomplete: file or total size limit reached."); continue; }
                        size += length;
                        var content = File.ReadAllText(path);
                        var relative = Path.GetRelativePath(group.Key, path).Replace('\\', '/');
                        // Read locally for dependency matches only; never send this inventory to a model.
                        sources.Add(new(relative, RepositoryGuidanceReconciler.Sensitive(content) ? "" : content,
                            RepositoryAgentsMerge.Hash(content), false));
                        if (sources[^1].Content.Length == 0)
                            warnings.Add($"Automation dependency content excluded because it may contain credentials: {relative}");
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { warnings.Add("Automation dependency coverage is incomplete: a directory could not be read."); }
            }
        }).ToArray();
    }

    internal static RepositoryFileMerge[] Validate(IReadOnlyList<RepositoryFileMerge> merges) => merges.Select(merge =>
    {
        var findings = new List<RepositoryGuidanceFinding>(merge.GuidanceReview?.Findings ?? []);
        if (!merge.Retire)
        {
            foreach (var retired in merges.Where(item => item.RepositoryPath == merge.RepositoryPath && item.Retire))
                if (RepositoryGuidanceDiscovery.References(merge.RepositoryPath, merge.RelativePath, merge.ProposedContent).Contains(retired.RelativePath))
                    findings.Add(new("dependency", retired.RelativePath, "This proposal still links to guidance selected for retirement. Remove the reference or retain the target before importing."));
            if (HasBrokenEncoding(merge.ProposedContent) && !HasBrokenEncoding(merge.CurrentContent))
                findings.Add(new("coverage", merge.RelativePath, "The proposal introduced corrupted Unicode text. Correct it before importing."));
        }
        var proposed = merge.Retire ? "" : merge.ProposedContent;
        var removed = Commands(merge.CurrentContent).Where(command => !proposed.Contains(command, StringComparison.Ordinal)).ToArray();
        foreach (var source in merge.AutomationSources)
        {
            var matches = removed.Where(command => source.Content.Contains(command, StringComparison.Ordinal)).Take(5).ToArray();
            if (matches.Length > 0)
                findings.Add(new("enforcement", source.Path, "Automation still references commands removed from this guidance: "
                    + string.Join(", ", matches) + ". Inspect or migrate the dependent gate and its evidence inputs. Import does not change automation; this text match is a review candidate, not proof of execution."));
        }
        var review = merge.GuidanceReview ?? new RepositoryGuidanceReview("unavailable", null, null, [], merge.ContextWarnings);
        return merge with { GuidanceReview = review with { Findings = findings.Distinct().ToArray() } };
    }).ToArray();

    internal static bool HasBrokenEncoding(string text) => text.Contains('\uFFFD')
        || Regex.IsMatch(text, "(?:ΓÇ[öôÖ£¥]|â€[”“™œ]|Ã[©¨±])", RegexOptions.CultureInvariant, Timeout);

    private static IEnumerable<string> Commands(string text)
    {
        var fragments = Regex.Matches(text, @"`([^`\r\n]+)`", RegexOptions.CultureInvariant, Timeout)
            .Select(match => match.Groups[1].Value).ToList();
        var fenced = false;
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)) { fenced = !fenced; continue; }
            if (fenced) fragments.Add(trimmed);
        }
        return fragments.Select(fragment => Regex.Match(fragment,
                @"^[A-Za-z][A-Za-z0-9_.-]* [A-Za-z0-9_./-]+(?: [A-Za-z0-9_./-]+)?", RegexOptions.CultureInvariant, Timeout))
            .Where(match => match.Success).Select(match => match.Value).Distinct(StringComparer.Ordinal);
    }
}
