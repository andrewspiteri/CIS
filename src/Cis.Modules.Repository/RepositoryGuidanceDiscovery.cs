using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

internal static class RepositoryGuidanceDiscovery
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    // Instructions and skills can be loaded without a link from AGENTS.md.
    // Custom agents and prompts also carry policy and must not escape review.
    internal static string[] Discover(string root, IReadOnlySet<string> generated, ICollection<string> warnings)
    {
        var files = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var directory in new[] { ".github/instructions", ".github/skills", ".github/agents", ".github/prompts" })
        {
            if (!CisPathSafety.TryResolveUnderRoot(root, directory, out var absolute)
                || !Directory.Exists(absolute) || CisPathSafety.ContainsReparsePoint(root, absolute)) continue;
            Visit(absolute);
        }
        if (files.Count > 198)
            warnings.Add($"Guidance discovery found {files.Count} files; only the first 198 can be edited in this import. The remaining guidance requires a separate review.");
        return files.Take(198).ToArray();

        void Visit(string directory)
        {
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
                {
                    if (RepositoryScanExclusions.IsPath(Path.GetRelativePath(root, path))) continue;
                    if (CisPathSafety.ContainsReparsePoint(root, path))
                    { warnings.Add($"Guidance path excluded because it is symbolic: {Path.GetRelativePath(root, path)}"); continue; }
                    if (Directory.Exists(path)) { Visit(path); continue; }
                    var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                    if (generated.Contains(relative)) continue;
                    if (!RepositoryAgentsMerge.IsEditablePath(relative))
                    {
                        if (!Path.GetFileName(path).StartsWith("cis-", StringComparison.OrdinalIgnoreCase)
                            && !relative.StartsWith(".github/skills/cis-", StringComparison.OrdinalIgnoreCase)
                            && (path.EndsWith(".instructions.md", StringComparison.OrdinalIgnoreCase)
                                || path.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase)
                                || path.EndsWith(".agent.md", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".prompt.md", StringComparison.OrdinalIgnoreCase)))
                            warnings.Add($"Guidance path is outside the supported editable layout and requires separate review: {relative}");
                        continue;
                    }
                    if (new FileInfo(path).Length > 256_000)
                    { warnings.Add($"Guidance exceeds the 256 KB review limit and was retained: {relative}"); continue; }
                    files.Add(relative);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { warnings.Add($"Guidance directory could not be fully inspected: {Path.GetRelativePath(root, directory)}"); }
        }
    }

    internal static IEnumerable<string> References(string root, string path, string text)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(text, @"(?:\.github|docs)/[A-Za-z0-9_./-]+\.(?:md|py|ps1|sh|ya?ml)\b", RegexOptions.CultureInvariant, Timeout))
            candidates.Add(match.Value);
        foreach (Match match in Regex.Matches(text, @"\]\(([^)\s]+)\)", RegexOptions.CultureInvariant, Timeout))
        {
            var link = match.Groups[1].Value.Split('#')[0];
            if (link.Length == 0 || link.Contains(':') || link.StartsWith('/')) continue;
            try
            {
                var combined = Path.GetFullPath(Path.Combine(root, Path.GetDirectoryName(path) ?? "", link));
                candidates.Add(Path.GetRelativePath(root, combined).Replace('\\', '/'));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException) { }
        }
        foreach (Match match in Regex.Matches(text, @"`([a-z][a-z0-9-]+)`", RegexOptions.CultureInvariant, Timeout))
        {
            var name = match.Groups[1].Value;
            candidates.Add($".github/skills/{name}/SKILL.md");
            candidates.Add($".github/agents/{name}.agent.md");
            candidates.Add($".github/prompts/{name}.prompt.md");
        }
        return candidates.Where(candidate => !CisPathSafety.TryResolveUnderRoot(root, candidate, out var absolute)
            || !RepositoryScanExclusions.IsPath(Path.GetRelativePath(root, absolute))
                && (CisPathSafety.ContainsReparsePoint(root, absolute) || File.Exists(absolute))).Order(StringComparer.Ordinal);
    }
}
