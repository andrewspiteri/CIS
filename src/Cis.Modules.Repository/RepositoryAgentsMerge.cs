using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cis.Modules.Repository;

public sealed record RepositoryFileMerge(
    string RepositoryPath,
    string RelativePath,
    string CurrentContent,
    string ProposedContent,
    string ReviewHash)
{
    public IReadOnlyList<RepositoryGuidanceSource> GuidanceSources { get; init; } = [];
    public RepositoryGuidanceReview? GuidanceReview { get; init; }
    public IReadOnlyList<RepositoryGuidanceReference> ContextSources { get; init; } = [];
    public IReadOnlyList<string> ContextWarnings { get; init; } = [];
    public bool Retire { get; init; }
    internal IReadOnlyList<RepositoryGuidanceReference> AutomationSources { get; init; } = [];
}

public sealed record RepositoryGuidanceSource(string Path, string Content);
public sealed record RepositoryGuidanceReference(string Path, string Content, string ContentHash, bool IsExcerpt);
public sealed record RepositoryGuidanceRemoval(int StartLine, int EndLine, string Kind, string Reason,
    string ReplacementPath, string ReplacementText, string Method);
public sealed record RepositoryGuidanceFinding(string Kind, string Path, string Detail);
public sealed record RepositoryGuidanceReview(string Status, string? Provider, string? Model,
    IReadOnlyList<RepositoryGuidanceRemoval> Removals, IReadOnlyList<string> Warnings,
    int CandidatePairs = 0, int ReviewedPairs = 0)
{
    public int TotalInstructions { get; init; }
    public int ReviewedInstructions { get; init; }
    public int CompletedPasses { get; init; }
    public IReadOnlyList<RepositoryGuidanceFinding> Findings { get; init; } = [];
}

public sealed record RepositoryGuidanceModel(string Provider, string Model, bool AllowRemote = false);

internal sealed record RepositoryFileMergeEdit(string RepositoryPath, string RelativePath, string Content, bool Retire = false);

internal static class RepositoryAgentsMerge
{
    internal const string Start = "<!-- cis:repository-guidance:start -->";
    internal const string End = "<!-- cis:repository-guidance:end -->";
    internal const string CopilotPath = ".github/copilot-instructions.md";
    internal static bool IsEntryPoint(string path) => path is "AGENTS.md" or CopilotPath;
    internal static bool IsEditablePath(string path) => IsEntryPoint(path) || Regex.IsMatch(path,
        @"^\.github/(?:(?:instructions/(?!cis-)[A-Za-z0-9_-]+\.instructions\.md)|(?:skills/(?!cis-)[A-Za-z0-9_-]+/SKILL\.md)|(?:agents/(?!cis-)[A-Za-z0-9_-]+\.agent\.md)|(?:prompts/(?!cis-)[A-Za-z0-9_-]+\.prompt\.md))$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    internal static IReadOnlyList<RepositoryFileMergeEdit>? ReadEdits(string? path, ICollection<string> errors)
    {
        if (path is null) return null;
        try
        {
            var edits = JsonSerializer.Deserialize<RepositoryFileMergeEdit[]>(File.ReadAllText(path),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (edits is null || edits.Length is < 1 or > 200 || edits.Any(edit => edit is null
                || string.IsNullOrWhiteSpace(edit.RepositoryPath) || !IsEditablePath(edit.RelativePath)
                || edit.Content is null || edit.Content.Contains('\0') || edit.Retire && IsEntryPoint(edit.RelativePath)))
                errors.Add("The merge edits file must contain 1–200 reviewed guidance entries with repositoryPath, relativePath, and content. Only proposed entry points and non-CIS instructions, skills, agents, and prompts are editable; entry points cannot be retired.");
            return edits;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or NotSupportedException)
        {
            errors.Add($"Cannot read the AGENTS.md merge edits file: {exception.Message}");
            return null;
        }
    }

    internal static bool TryCreate(string repositoryPath, string current, string generated,
        out RepositoryFileMerge? proposal, IReadOnlyList<RepositoryGuidanceSource>? guidanceSources = null,
        string relativePath = "AGENTS.md")
    {
        proposal = null;
        var start = current.IndexOf(Start, StringComparison.Ordinal);
        var end = current.IndexOf(End, StringComparison.Ordinal);
        if ((start < 0) != (end < 0) || end < start
            || start >= 0 && (current.IndexOf(Start, start + Start.Length, StringComparison.Ordinal) >= 0
                || current.IndexOf(End, end + End.Length, StringComparison.Ordinal) >= 0))
            return false;

        var newline = current.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var section = Start + newline + generated.Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\r', '\n').Replace("\n", newline, StringComparison.Ordinal) + newline + End;
        var merged = start >= 0
            ? current[..start] + section + current[(end + End.Length)..]
            : current + (current.EndsWith(newline + newline, StringComparison.Ordinal) ? ""
                : current.EndsWith(newline, StringComparison.Ordinal) ? newline : newline + newline)
                + section + newline;
        if (!IsEntryPoint(relativePath)) merged = current;
        var sources = guidanceSources ?? [];
        proposal = new(repositoryPath, relativePath, current, merged,
            Hash(JsonSerializer.Serialize(new[] { repositoryPath, relativePath, current, merged, TemplateHash(generated, sources) })))
        {
            GuidanceSources = [new("AGENTS.md", generated), .. sources],
        };
        return true;
    }

    internal static string TemplateHash(string generated, IReadOnlyList<RepositoryGuidanceSource> sources) =>
        Hash(JsonSerializer.Serialize(new { policy = "connected-guidance-v2", generated, sources }));

    internal static string? ReviewHash(IReadOnlyList<RepositoryFileMerge> proposals) => proposals.Count == 0
        ? null : Hash(JsonSerializer.Serialize(proposals.OrderBy(item => item.RepositoryPath, StringComparer.Ordinal)
            .ThenBy(item => item.RelativePath, StringComparer.Ordinal)
            .Select(item => item.ReviewHash).ToArray()));

    internal static string Hash(string value) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
