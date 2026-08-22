using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Generate;

public sealed record GenerateTemplate(string Id, string Path, string Digest, IReadOnlyList<string> Variables, long Bytes);
public sealed record GenerateResult(string Status, string? RepositoryPath, GenerateTemplate? Template,
    string? OutputPath, string? OutputDigest, IReadOnlyList<GenerateTemplate> Templates,
    IReadOnlyList<string> Diagnostics, bool Applied)
{
    public int ExitCode => Diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal)) ? 4 : 0;
}

public sealed partial class GenerateService
{
    public const string ManifestPath = ".cis/local/generate/renders.jsonl";
    private readonly ICisRepositoryContextResolver _resolver;
    private readonly Func<DateTimeOffset> _clock;
    public GenerateService(ICisRepositoryContextResolver resolver, Func<DateTimeOffset>? clock = null)
    { _resolver = resolver; _clock = clock ?? (() => DateTimeOffset.UtcNow); }

    public GenerateResult Templates(string repositoryPath)
    {
        var context = Resolve(repositoryPath, out var errors);
        var templates = context is null ? [] : Discover(context);
        return Result(context, errors.Count == 0 ? "listed" : "invalid-repository", null, null, null, templates, errors, false);
    }

    public GenerateResult Describe(string repositoryPath, string id) => Find(repositoryPath, id, "described");
    public GenerateResult Validate(string repositoryPath, string id) => Find(repositoryPath, id, "valid");

    public GenerateResult Render(string repositoryPath, string id, string output, IReadOnlyList<string> assignments)
    {
        var context = Resolve(repositoryPath, out var errors);
        if (context is null) return Result(null, "invalid-repository", null, null, null, [], errors, false);
        var templates = Discover(context); var template = templates.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (template is null) errors.Add($"ERROR: Unknown template '{id}'.");
        var values = Parse(assignments, errors);
        if (template is null || errors.Count > 0) return Result(context, "invalid", template, null, null, templates, errors, false);
        var source = File.ReadAllText(Path.Combine(context.RepositoryPath, template.Path.Replace('/', Path.DirectorySeparatorChar)));
        var missing = template.Variables.Where(variable => !values.ContainsKey(variable)).ToArray();
        foreach (var variable in missing) errors.Add($"ERROR: Missing template value '{variable}'.");
        foreach (var pair in values) source = source.Replace("{{" + pair.Key + "}}", pair.Value, StringComparison.Ordinal);
        if (TokenRegex().IsMatch(source)) errors.Add("ERROR: Rendered output still contains unresolved template variables.");
        var absolute = Path.GetFullPath(Path.Combine(context.RepositoryPath, output.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithin(context.RepositoryPath, absolute)) errors.Add("ERROR: Output must remain inside the repository.");
        if (errors.Count > 0) return Result(context, "invalid", template, output, null, templates, errors, false);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        var digest = Sha256(source); var applied = !File.Exists(absolute) || Sha256(File.ReadAllText(absolute)) != digest;
        if (applied)
        {
            var temporary = absolute + ".cis-tmp"; File.WriteAllText(temporary, source); File.Move(temporary, absolute, true);
        }
        AppendManifest(context, new { timestampUtc = _clock().ToUniversalTime().ToString("O"), template = template.Id,
            templateDigest = template.Digest, output = Relative(context.RepositoryPath, absolute), outputDigest = digest,
            variables = values.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray(), applied });
        return Result(context, applied ? "rendered" : "unchanged", template, Relative(context.RepositoryPath, absolute), digest, templates,
            [$"INFO: deterministic template reuse may save approximately {Math.Max(1, source.Length / 4)} generated tokens."], applied);
    }

    private GenerateResult Find(string repositoryPath, string id, string success)
    {
        var context = Resolve(repositoryPath, out var errors); var templates = context is null ? [] : Discover(context);
        var template = templates.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (context is not null && template is null) errors.Add($"ERROR: Unknown template '{id}'.");
        return Result(context, errors.Count == 0 ? success : "invalid", template, null, null, templates, errors, false);
    }

    private static IReadOnlyList<GenerateTemplate> Discover(CisRepositoryContext context)
    {
        var root = Path.Combine(context.DocumentationPath, "templates"); if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".md" or ".txt" or ".json" or ".yml" or ".yaml")
            .Select(path => { var text = File.ReadAllText(path); var relative = Relative(context.RepositoryPath, path);
                return new GenerateTemplate(Relative(root, path), relative, Sha256(text), TokenRegex().Matches(text).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).Order().ToArray(), new FileInfo(path).Length); })
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }

    private static Dictionary<string, string> Parse(IReadOnlyList<string> assignments, List<string> errors)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var assignment in assignments)
        {
            var split = assignment.IndexOf('='); if (split <= 0) { errors.Add($"ERROR: Template value must use key=value: {assignment}"); continue; }
            var key = assignment[..split].Trim(); if (!values.TryAdd(key, assignment[(split + 1)..])) errors.Add($"ERROR: Duplicate template value '{key}'.");
        }
        return values;
    }
    private static bool IsWithin(string root, string path) => path.Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static GenerateResult Result(CisRepositoryContext? context, string status, GenerateTemplate? template, string? output, string? digest,
        IReadOnlyList<GenerateTemplate> templates, IReadOnlyList<string> diagnostics, bool applied)
        => new(status, context?.RepositoryPath, template, output, digest, templates, diagnostics, applied);
    private static GenerateResult Result(CisRepositoryContext? context, string status, GenerateTemplate? template, string? output, string? digest,
        IReadOnlyList<GenerateTemplate> templates, List<string> diagnostics, bool applied)
        => Result(context, status, template, output, digest, templates, diagnostics.ToArray(), applied);
    private CisRepositoryContext? Resolve(string path, out List<string> errors) { var result = _resolver.Resolve(path); errors = result.Errors.Select(x => "ERROR: " + x).ToList(); return result.Context; }
    private static void AppendManifest(CisRepositoryContext context, object record)
    { var path = Path.Combine(context.RepositoryPath, ManifestPath.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.AppendAllText(path, JsonSerializer.Serialize(record) + Environment.NewLine); }
    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9_.-]*)\s*\}\}")]
    private static partial Regex TokenRegex();
}
