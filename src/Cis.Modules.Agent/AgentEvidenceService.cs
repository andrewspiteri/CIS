using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed record AgentEvidenceResult(string Status, string? RepositoryPath, string? EvidencePath,
    IReadOnlyList<string> Requirements, IReadOnlyList<string> Diagnostics, bool Strict)
{
    public int ExitCode => Diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal)) ||
        Strict && Diagnostics.Any(item => item.StartsWith("WARNING:", StringComparison.Ordinal)) ? 5 : 0;
}

public sealed class AgentEvidenceService(ICisRepositoryContextResolver resolver)
{
    public AgentEvidenceResult Validate(string repositoryPath, string evidencePath, IReadOnlyList<string> changedFiles, bool strict)
    {
        var resolution = resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return new("invalid-repository", null, null, [], resolution.Errors.Select(item => "ERROR: " + item).ToArray(), strict);
        var context = resolution.Context;
        var diagnostics = new List<string>();
        if (!CisPathSafety.TryResolveUnderRoot(context.RepositoryPath, evidencePath, out var absolute))
            diagnostics.Add("ERROR: Evidence path must be repository-relative and cannot traverse parents.");
        foreach (var file in changedFiles.Where(file => !CisPathSafety.TryResolveUnderRoot(context.RepositoryPath, file, out _)))
            diagnostics.Add($"ERROR: Changed file is unsafe: {file}");
        if (absolute.Length > 0 && (!File.Exists(absolute) || CisPathSafety.ContainsReparsePoint(context.RepositoryPath, absolute)))
            diagnostics.Add($"ERROR: Evidence file does not exist or crosses a symbolic link: {evidencePath}");
        if (diagnostics.Count > 0) return new("invalid", context.RepositoryPath, evidencePath, [], diagnostics, strict);

        var text = File.ReadAllText(absolute);
        var lower = text.ToLowerInvariant();
        var requirements = Infer(changedFiles);
        if (!lower.Contains("## cis tooling evidence", StringComparison.Ordinal))
            diagnostics.Add("WARNING: Missing `## CIS tooling evidence` section.");
        if (requirements.Contains("generation-decision") && !Regex.IsMatch(lower,
            @"cis generate (?:used|not applicable:\s*\S|unavailable:\s*\S)"))
            diagnostics.Add("WARNING: Record `cis generate used`, `cis generate not applicable: <reason>`, or `cis generate unavailable: <reason>`.");
        if (requirements.Contains("context-evidence") && !Regex.IsMatch(lower, @"cis (?:context|graph|frontend)\s+\S+"))
            diagnostics.Add("WARNING: Source changes require an exact CIS context, graph, or frontend routing command.");
        if (requirements.Contains("test-evidence") && !(lower.Contains("cis test reconcile", StringComparison.Ordinal) &&
            Regex.IsMatch(lower, @"run(?:-|\s*)id\s*[:=]\s*\S+")))
            diagnostics.Add("WARNING: Testable changes require `cis test reconcile` evidence and an exact run ID.");
        if (requirements.Contains("usage-ledger"))
        {
            var ledger = Path.Combine(context.RepositoryPath, ".cis", "local", "feedback", "tool-usage.jsonl");
            var bypass = Regex.IsMatch(lower, @"usage ledger bypass reason\s*:\s*\S+");
            if ((!File.Exists(ledger) || new FileInfo(ledger).Length == 0) && !bypass)
                diagnostics.Add("WARNING: Tool-assisted work requires the local usage ledger or a specific bypass reason.");
        }
        if (Regex.IsMatch(text, @"<specific reason>|<command>|\bTODO\b", RegexOptions.IgnoreCase))
            diagnostics.Add("WARNING: Evidence contains placeholder text.");
        return new(diagnostics.Count == 0 ? "valid" : "warnings", context.RepositoryPath,
            Path.GetRelativePath(context.RepositoryPath, absolute).Replace(Path.DirectorySeparatorChar, '/'), requirements, diagnostics, strict);
    }

    private static IReadOnlyList<string> Infer(IReadOnlyList<string> files)
    {
        var output = new HashSet<string>(StringComparer.Ordinal);
        if (files.Count > 0) { output.Add("generation-decision"); output.Add("usage-ledger"); }
        if (files.Any(file => Path.GetExtension(file).ToLowerInvariant() is ".cs" or ".ts" or ".tsx" or ".js" or ".jsx" or ".swift" or ".kt" or ".gd")) output.Add("context-evidence");
        if (files.Any(file => file.Replace('\\', '/').StartsWith("src/", StringComparison.OrdinalIgnoreCase) || file.Contains("test", StringComparison.OrdinalIgnoreCase))) output.Add("test-evidence");
        return output.Order(StringComparer.Ordinal).ToArray();
    }
}
