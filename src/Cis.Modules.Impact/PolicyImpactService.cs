using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Impact;

public sealed record PolicyImpactCandidate(string Path, IReadOnlyList<string> MatchedTargets, string Reason);
public sealed record PolicyImpactResult(string Status, string? RepositoryPath, string? PolicyPath, string? PolicyDigest,
    IReadOnlyList<string> Targets, IReadOnlyList<PolicyImpactCandidate> Candidates, IReadOnlyList<string> Diagnostics,
    string? JsonPath, string? MarkdownPath, bool Applied, bool Strict)
{
    public int ExitCode => Diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal)) ||
        Strict && Diagnostics.Any(item => item.StartsWith("WARNING:", StringComparison.Ordinal)) ? 5 : 0;
}

public sealed class PolicyImpactService(ICisRepositoryContextResolver resolver)
{
    private static readonly string[] SupportedTargets = ["backend", "frontend", "documentation", "infrastructure", "security", "api", "testing", "verification", "release", "repository-governance"];
    private static readonly string[] Excluded = [".git", ".cis", ".codex-tmp", "node_modules", "bin", "obj", "dist", "build", ".next", ".nuxt", "coverage"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public PolicyImpactResult Analyse(string repositoryPath, string policyPath, bool strict)
    {
        var resolution = resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return new("invalid-repository", null, policyPath, null, [], [], resolution.Errors.Select(item => "ERROR: " + item).ToArray(), null, null, false, strict);
        var context = resolution.Context;
        var diagnostics = new List<string>();
        if (!CisPathSafety.TryResolveUnderRoot(context.RepositoryPath, policyPath, out var absolute))
            diagnostics.Add("ERROR: Policy path must be repository-relative and cannot traverse parents.");
        if (absolute.Length > 0 && (!File.Exists(absolute) || CisPathSafety.ContainsReparsePoint(context.RepositoryPath, absolute)))
            diagnostics.Add($"ERROR: Policy document does not exist or crosses a symbolic link: {policyPath}");
        if (diagnostics.Count > 0) return new("invalid", context.RepositoryPath, policyPath, null, [], [], diagnostics, null, null, false, strict);

        var text = File.ReadAllText(absolute);
        var targets = ReadTargets(text);
        foreach (var target in targets.Where(target => !SupportedTargets.Contains(target, StringComparer.Ordinal)))
            diagnostics.Add($"ERROR: Unsupported policy target '{target}'. Supported targets: {string.Join(", ", SupportedTargets)}.");
        if (targets.Count == 0)
        {
            targets = InferTargets(policyPath, text);
            diagnostics.Add($"WARNING: Policy has no `targets:` front matter; inferred {string.Join(", ", targets)}. Add explicit governed-surface targets.");
        }
        if (diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal)))
            return new("invalid", context.RepositoryPath, policyPath, Sha(text), targets, [], diagnostics, null, null, false, strict);

        var candidates = CisPathSafety.EnumerateFiles(context.RepositoryPath)
            .Where(path => !Excluded.Any(segment => Relative(context.RepositoryPath, path).Split('/').Contains(segment, StringComparer.OrdinalIgnoreCase)))
            .Where(path => !path.Equals(absolute, StringComparison.OrdinalIgnoreCase))
            .Select(path => Match(Relative(context.RepositoryPath, path), targets))
            .Where(item => item is not null).Cast<PolicyImpactCandidate>()
            .OrderBy(item => item.Path, StringComparer.Ordinal).Take(5000).ToArray();
        if (candidates.Length == 5000) diagnostics.Add("WARNING: Candidate impact set reached the 5,000-file safety bound.");
        var digest = Sha(text);
        var key = Path.GetFileNameWithoutExtension(policyPath).ToLowerInvariant().Replace(' ', '-');
        var root = Path.Combine(context.RepositoryPath, ".cis", "local", "impact", "policy", SafeName(key), digest[..12]);
        Directory.CreateDirectory(root);
        var jsonPath = Path.Combine(root, "impact-analysis.json");
        var markdownPath = Path.Combine(root, "impact-analysis.md");
        var result = new PolicyImpactResult(diagnostics.Count == 0 ? "analysed" : "warnings", context.RepositoryPath,
            Relative(context.RepositoryPath, absolute), digest, targets, candidates, diagnostics,
            Relative(context.RepositoryPath, jsonPath), Relative(context.RepositoryPath, markdownPath), true, strict);
        WriteAtomic(jsonPath, JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine);
        WriteAtomic(markdownPath, RenderMarkdown(result));
        return result;
    }

    private static PolicyImpactCandidate? Match(string path, IReadOnlyList<string> targets)
    {
        var normalized = path.ToLowerInvariant(); var extension = Path.GetExtension(normalized);
        var matches = targets.Where(target => target switch
        {
            "documentation" => normalized.StartsWith("docs/"),
            "frontend" => extension is ".tsx" or ".jsx" or ".vue" or ".html" or ".css" or ".scss" or ".swift" or ".kt" or ".gd",
            "backend" => normalized.StartsWith("src/") && extension is ".cs" or ".ts" or ".java" or ".go" or ".py" && !normalized.Contains("frontend"),
            "infrastructure" => normalized.Contains("terraform") || normalized.Contains("docker") || extension is ".tf" or ".hcl" || normalized.StartsWith("infra/"),
            "security" => normalized.StartsWith("src/") || normalized.StartsWith("infra/") || normalized.StartsWith(".github/"),
            "api" => normalized.Contains("api") || normalized.Contains("controller") || normalized.Contains("endpoint") || normalized.Contains("openapi"),
            "testing" => normalized.Contains("test") || normalized.Contains("spec") || normalized.Contains("playwright") || normalized.Contains("cucumber"),
            "verification" => normalized.Contains("test") || normalized.Contains("verify") || normalized.Contains("workflow") || normalized.Contains("security"),
            "release" => normalized.StartsWith(".github/") || normalized.Contains("release") || normalized.Contains("package") || extension is ".csproj" or ".props" or ".targets",
            "repository-governance" => normalized.StartsWith("docs/") || normalized.StartsWith(".github/") || normalized.StartsWith(".cis/"),
            _ => false,
        }).ToArray();
        return matches.Length == 0 ? null : new(path, matches, $"Path and file kind match policy target(s): {string.Join(", ", matches)}.");
    }
    private static IReadOnlyList<string> ReadTargets(string text)
    {
        var front = text.StartsWith("---", StringComparison.Ordinal) ? text.Split("---", 3, StringSplitOptions.None).ElementAtOrDefault(1) ?? "" : "";
        var inline = Regex.Match(front, @"(?m)^targets:\s*\[(?<v>[^\]]+)\]");
        if (inline.Success) return inline.Groups["v"].Value.Split(',').Select(Normalize).Where(item => item.Length > 0).Distinct().ToArray();
        var block = Regex.Match(front, @"(?ms)^targets:\s*\r?\n(?<v>(?:\s+-\s*[^\r\n]+\r?\n?)+)");
        return block.Success ? Regex.Matches(block.Groups["v"].Value, @"(?m)^\s+-\s*(?<v>[^\r\n#]+)").Select(match => Normalize(match.Groups["v"].Value)).Distinct().ToArray() : [];
    }
    private static IReadOnlyList<string> InferTargets(string path, string text)
    {
        var value = (path + " " + text[..Math.Min(text.Length, 2000)]).ToLowerInvariant(); var output = new List<string>();
        if (value.Contains("frontend") || value.Contains("ui ")) output.Add("frontend");
        if (value.Contains("api") || value.Contains("endpoint")) output.Add("api");
        if (value.Contains("security") || value.Contains("permission")) output.Add("security");
        if (value.Contains("terraform") || value.Contains("infrastructure")) output.Add("infrastructure");
        if (value.Contains("test")) output.Add("testing");
        if (value.Contains("verification")) output.Add("verification");
        if (value.Contains("release")) output.Add("release");
        if (value.Contains("repository governance")) output.Add("repository-governance");
        if (value.Contains("documentation") || path.Replace('\\', '/').StartsWith("docs/")) output.Add("documentation");
        if (output.Count == 0) output.Add("backend"); return output.Distinct().ToArray();
    }
    private static string RenderMarkdown(PolicyImpactResult result) => $"""
        # Policy impact analysis

        - Policy: `{result.PolicyPath}`
        - Digest: `{result.PolicyDigest}`
        - Targets: {string.Join(", ", result.Targets)}
        - Candidate files: {result.Candidates.Count}

        ## Candidate impacts

        | Path | Targets | Reason |
        |---|---|---|
        {string.Join(Environment.NewLine, result.Candidates.Select(item => $"| `{item.Path}` | {string.Join(", ", item.MatchedTargets)} | {item.Reason.Replace('|', '/')} |"))}

        ## Diagnostics

        {string.Join(Environment.NewLine, result.Diagnostics.Select(item => "- " + item))}
        """ + Environment.NewLine;
    private static string Normalize(string value) => value.Trim().Trim('\'', '"').ToLowerInvariant() switch { "docs" => "documentation", "infra" => "infrastructure", var item => item };
    private static string SafeName(string value) => new(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray());
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    private static string Sha(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void WriteAtomic(string path, string content) { var temporary = path + ".tmp"; File.WriteAllText(temporary, content); File.Move(temporary, path, true); }
}
