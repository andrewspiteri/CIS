using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Repository;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Cis.Modules.Standards;

public sealed partial class StandardImportService
{
    private const long MaximumDownloadBytes = 100 * 1024 * 1024;
    private const long MaximumExpandedBytes = 250 * 1024 * 1024;
    private const int MaximumArchiveEntries = 10_000;
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".cis", ".codex-tmp", "node_modules", "bin", "obj", "standards-quarantine",
    };

    private readonly ICisRepositoryContextResolver _resolver;
    private readonly DocumentationCatalogMerger _catalogMerger;
    private readonly HttpClient _httpClient;
    private readonly StandardsGovernanceService _governance;
    private readonly IDeserializer _deserializer = new DeserializerBuilder().Build();

    public StandardImportService(
        ICisRepositoryContextResolver resolver,
        DocumentationCatalogMerger catalogMerger,
        HttpClient? httpClient = null)
    {
        _resolver = resolver;
        _catalogMerger = catalogMerger;
        _governance = new StandardsGovernanceService(resolver, new Cis.Modules.Docs.DocumentationCatalogReader());
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public StandardImportResult Import(StandardImportRequest request)
    {
        var resolution = _resolver.Resolve(request.RepositoryPath);
        if (!resolution.IsSuccess)
            return Result("invalid", 2, null, request, [], [], [], resolution.Errors);
        if (request.Sources.Count == 0)
            return Result("invalid", 2, resolution.Context!.RepositoryPath, request, [], [], [], ["At least one --source path or URL is required."]);

        var context = resolution.Context!;
        var stagingRoot = Path.Combine(Path.GetTempPath(), "cis-standard-import", Guid.NewGuid().ToString("N"));
        var warnings = new List<string>(); var conflicts = new List<string>(); var errors = new List<string>();
        try
        {
            Directory.CreateDirectory(stagingRoot);
            var candidates = new List<ImportCandidate>();
            foreach (var source in request.Sources.Distinct(StringComparer.OrdinalIgnoreCase))
                Materialize(source, context.RepositoryPath, stagingRoot, candidates, errors);
            if (errors.Count > 0) return Result("invalid", 2, context.RepositoryPath, request, [], warnings, conflicts, errors);

            var prepared = new List<PreparedStandard>();
            foreach (var candidate in candidates)
            {
                try
                {
                    prepared.Add(Prepare(candidate, context.RepositoryId, request.Fix, warnings));
                }
                catch (Exception exception) when (exception is YamlException or FormatException or IOException)
                {
                    errors.Add($"{candidate.Path}: {exception.Message}");
                }
            }
            foreach (var duplicate in prepared.GroupBy(item => item.Slug, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
                errors.Add($"Several sources resolve to standard slug '{duplicate.Key}'.");
            foreach (var duplicate in prepared.GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
                errors.Add($"Several sources declare standard ID '{duplicate.Key}'.");
            foreach (var duplicate in prepared.SelectMany(item => item.RuleIds.Select(rule => (item.Id, Rule: rule))).GroupBy(item => item.Rule, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
                errors.Add($"Imported standards reuse stable rule ID '{duplicate.Key}': {string.Join(", ", duplicate.Select(item => item.Id))}.");
            var existing = _governance.Inventory(context.RepositoryPath);
            if (existing.ExitCode != 0) errors.AddRange(existing.Errors);
            var existingRules = existing.Standards.SelectMany(item => item.RuleIds.Select(rule => (item.Id, Rule: rule))).ToArray();
            foreach (var item in prepared)
            foreach (var rule in item.RuleIds)
            {
                var owner = existingRules.FirstOrDefault(existingRule => existingRule.Rule.Equals(rule, StringComparison.OrdinalIgnoreCase));
                if (owner != default && !owner.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Imported rule ID '{rule}' already belongs to standard '{owner.Id}'.");
            }
            if (request.Strict && warnings.Count > 0)
                errors.Add("Strict import rejected warnings; review the repaired or inferred standard metadata.");
            if (errors.Count > 0) return Result("invalid", 2, context.RepositoryPath, request, [], warnings, conflicts, errors);

            var items = new List<StandardImportItem>();
            foreach (var item in prepared)
            {
                var relative = $"{context.DocumentationRoot}/standards/{item.Slug}-standard.md";
                var destination = Path.Combine(context.RepositoryPath, relative.Replace('/', Path.DirectorySeparatorChar));
                var status = !File.Exists(destination) ? "create" : Sha256(File.ReadAllText(destination)) == item.Hash ? "unchanged" : "conflict";
                if (status == "conflict") conflicts.Add($"Standard destination already exists with different content: {relative}");
                items.Add(new StandardImportItem(item.Id, item.Title, item.Source, relative, item.Hash, status, item.RuleIds.Count));
            }

            var catalogContent = File.ReadAllText(context.CatalogPath);
            var entries = prepared.Select(item => new CatalogArtifactEntry(
                item.Id, $"{context.DocumentationRoot}/standards/{item.Slug}-standard.md", "standard", item.Status.ToLowerInvariant(), "canonical")).ToArray();
            var catalogMerge = _catalogMerger.Merge(context.RepositoryId, catalogContent, entries);
            conflicts.AddRange(catalogMerge.Collisions);
            var matrixPath = Path.Combine(context.DocumentationPath, StandardsGovernanceService.ConformanceMatrixPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(matrixPath)) errors.Add($"Standards conformance matrix is missing: {context.DocumentationRoot}/{StandardsGovernanceService.ConformanceMatrixPath}");
            var matrixContent = errors.Count == 0 ? File.ReadAllText(matrixPath) : string.Empty;
            var matrixMerge = MergeConformance(matrixContent, prepared, conflicts);
            if (conflicts.Count > 0) return Result("conflict", 4, context.RepositoryPath, request, items, warnings, conflicts, errors);
            if (errors.Count > 0) return Result("invalid", 2, context.RepositoryPath, request, items, warnings, conflicts, errors);
            if (request.DryRun) return Result("dry-run", 0, context.RepositoryPath, request, items, warnings, conflicts, errors);

            var creates = items.Where(item => item.Status == "create").ToArray();
            var changesCanonicalState = creates.Length > 0 || catalogMerge.Changed || matrixMerge.Changed;
            if (changesCanonicalState && !request.Confirmed)
                return Result("confirmation-required", 3, context.RepositoryPath, request, items, warnings, conflicts, errors, confirmationRequired: true);

            var createdPaths = new List<string>();
            try
            {
                foreach (var item in prepared.Where(item => creates.Any(create => create.Id == item.Id)))
                {
                    var destination = Path.Combine(context.DocumentationPath, "standards", item.Slug + "-standard.md");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    Write(destination, item.Content); createdPaths.Add(destination);
                }
                if (catalogMerge.Changed) Write(context.CatalogPath, catalogMerge.Content);
                if (matrixMerge.Changed) Write(matrixPath, matrixMerge.Content);
            }
            catch
            {
                foreach (var path in createdPaths) if (File.Exists(path)) File.Delete(path);
                Write(context.CatalogPath, catalogContent); Write(matrixPath, matrixContent); throw;
            }

            WriteImportRecord(context, items);
            return Result(changesCanonicalState ? "imported" : "unchanged", 0, context.RepositoryPath, request, items, warnings, conflicts, errors, applied: changesCanonicalState);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            errors.Add(exception.Message);
            return Result("invalid", 2, context.RepositoryPath, request, [], warnings, conflicts, errors);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
                try { Directory.Delete(stagingRoot, recursive: true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private PreparedStandard Prepare(ImportCandidate candidate, string repositoryId, bool fix, ICollection<string> warnings)
    {
        var content = File.ReadAllText(candidate.Path).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!TryFrontMatter(content, out var yaml, out var body))
        {
            if (!fix) throw new FormatException("YAML front matter is required; use --fix to add it.");
            yaml = string.Empty; body = content;
            warnings.Add($"Added YAML front matter to staged source '{candidate.Path}'.");
        }
        var metadata = _deserializer.Deserialize<Dictionary<object, object?>>(yaml) ?? [];
        var title = Scalar(metadata, "title");
        if (title.Length == 0) title = HeadingRegex().Match(body) is { Success: true } heading ? heading.Groups[1].Value.Trim() : TitleFromPath(candidate.Path);
        var slug = SlugRegex().Replace(Path.GetFileNameWithoutExtension(candidate.Path).Replace("-standard", string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant(), "-").Trim('-');
        if (slug.Length == 0) throw new FormatException("A stable filename slug could not be derived.");
        var id = NestedScalar(metadata, "cis", "stable_id");
        if (id.Length == 0) id = $"{repositoryId}:standard:{slug}";

        if (fix)
        {
            var additions = new List<string>();
            AddIfMissing(metadata, additions, "title", $"title: {YamlScalar(title)}");
            AddIfMissing(metadata, additions, "type", "type: standard");
            AddIfMissing(metadata, additions, "status", "status: Draft");
            AddIfMissing(metadata, additions, "targets", "targets:\n  - repository-governance");
            AddIfMissing(metadata, additions, "owner", "owner: Repository maintainer");
            AddIfMissing(metadata, additions, "last_reviewed", $"last_reviewed: {DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}");
            AddIfMissing(metadata, additions, "review_cadence", "review_cadence: on change");
            AddIfMissing(metadata, additions, "source_of_truth", "source_of_truth: This file");
            if (NestedScalar(metadata, "cis", "stable_id").Length == 0) additions.Add($"cis:\n  stable_id: {id}");
            if (additions.Count > 0)
            {
                yaml = yaml.TrimEnd() + "\n" + string.Join("\n", additions);
                warnings.Add($"Repaired missing standard metadata in staged source '{candidate.Path}'.");
            }
            var missing = new[] { "Purpose", "Scope", "Normative language", "Rules", "Verification", "Exceptions" }
                .Where(name => !SectionRegex(name).IsMatch(body)).ToArray();
            if (missing.Length > 0)
            {
                body = body.TrimEnd() + "\n\n" + string.Join("\n\n", missing.Select(name => $"## {name}\n\nImported content requires maintainer review.")) + "\n";
                warnings.Add($"Added missing standard sections to staged source '{candidate.Path}': {string.Join(", ", missing)}.");
            }
            if (StandardRuleParser.Parse(body).Count == 0)
            {
                body = AssignRuleIds(body, slug, out var assigned);
                if (assigned > 0)
                    warnings.Add($"Assigned {assigned} stable rule ID(s) to existing normative statements in staged source '{candidate.Path}'; no rule semantics were generated.");
            }
            content = $"---\n{yaml}\n---\n{body.TrimStart()}";
            metadata = _deserializer.Deserialize<Dictionary<object, object?>>(yaml) ?? [];
        }

        var type = Scalar(metadata, "type"); var status = Scalar(metadata, "status"); var targets = Sequence(metadata, "targets");
        var required = new Dictionary<string, string>
        {
            ["title"] = Scalar(metadata, "title"), ["type"] = type, ["status"] = status,
            ["targets"] = string.Join(',', targets), ["owner"] = Scalar(metadata, "owner"),
            ["last_reviewed"] = Scalar(metadata, "last_reviewed"), ["review_cadence"] = Scalar(metadata, "review_cadence"),
            ["source_of_truth"] = Scalar(metadata, "source_of_truth"), ["cis.stable_id"] = NestedScalar(metadata, "cis", "stable_id"),
        };
        var missingMetadata = required.Where(item => string.IsNullOrWhiteSpace(item.Value)).Select(item => item.Key).ToArray();
        if (missingMetadata.Length > 0) throw new FormatException($"Missing required metadata: {string.Join(", ", missingMetadata)}. Use --fix for safe structural repairs.");
        if (!type.Equals("standard", StringComparison.OrdinalIgnoreCase)) throw new FormatException("Front matter type must be 'standard'.");
        if (status is not ("Active" or "Draft" or "Deprecated" or "Archived")
            && status.ToLowerInvariant() is not ("active" or "draft" or "deprecated" or "archived"))
            throw new FormatException($"Unsupported lifecycle status '{status}'.");
        if (!StableIdRegex().IsMatch(id)) throw new FormatException($"Stable ID '{id}' contains unsupported characters.");
        foreach (var heading in new[] { "Purpose", "Scope", "Normative language", "Rules", "Verification", "Exceptions" })
            if (!SectionRegex(heading).IsMatch(body)) throw new FormatException($"Required section '{heading}' is missing; use --fix to scaffold it.");
        var rules = StandardRuleParser.Parse(body).Select(rule => rule.Id).ToArray();
        if (rules.Length == 0) throw new FormatException("At least one stable rule ID is required; no existing normative statement was available for deterministic ID assignment.");
        var finalContent = content.EndsWith('\n') ? content : content + "\n";
        return new PreparedStandard(id, slug, title, status, candidate.Source, finalContent, Sha256(finalContent), rules);
    }

    private void Materialize(string source, string repositoryPath, string stagingRoot, ICollection<ImportCandidate> candidates, ICollection<string> errors)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == "http")
            throw new InvalidDataException("Remote standard sources must use HTTPS.");
        if (uri is not null && uri.Scheme == "https")
        {
            if (!string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("Remote standard source URLs cannot contain credentials.");
            var remote = ResolveRemote(uri); var archive = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N") + ".zip"); Download(remote.Archive, archive);
            var expanded = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N")); Extract(archive, expanded); Discover(ResolveScanRoot(expanded, remote.Subpath), source, candidates, errors); return;
        }
        var path = Path.GetFullPath(Path.IsPathRooted(source) ? source : Path.Combine(repositoryPath, source));
        if (File.Exists(path) && Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var expanded = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N")); Extract(path, expanded); Discover(expanded, source, candidates, errors); return;
        }
        if (File.Exists(path) && Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { errors.Add($"Standard source is a symbolic link or reparse point and was rejected: {source}"); return; }
            candidates.Add(new ImportCandidate(path, source)); return;
        }
        if (!Directory.Exists(path)) { errors.Add($"Standard source does not exist or is unsupported: {source}"); return; }
        Discover(path, source, candidates, errors);
    }

    private static void Discover(string root, string source, ICollection<ImportCandidate> candidates, ICollection<string> errors)
    {
        var files = EnumerateMarkdown(root).Where(IsStandardDocument).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) { errors.Add($"No Markdown standards were found in source '{source}'."); return; }
        foreach (var file in files) candidates.Add(new ImportCandidate(file, source));
    }

    private static IEnumerable<string> EnumerateMarkdown(string root)
        => CisPathSafety.EnumerateFiles(root, "*.md")
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(ExcludedDirectories.Contains));

    private static bool IsStandardDocument(string path)
    {
        if (Path.GetFileName(path).EndsWith("-standard.md", StringComparison.OrdinalIgnoreCase)) return true;
        using var reader = new StreamReader(path); for (var count = 0; count < 80 && reader.ReadLine() is { } line; count++)
            if (line.Trim().Equals("type: standard", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static MatrixMerge MergeConformance(string content, IReadOnlyList<PreparedStandard> standards, ICollection<string> conflicts)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal); var additions = new StringBuilder();
        foreach (var standard in standards)
        foreach (var rule in standard.RuleIds)
        {
            var matching = normalized.Split('\n').Where(line => line.TrimStart().StartsWith('|') && line.Contains(standard.Id, StringComparison.OrdinalIgnoreCase) && line.Contains(rule, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matching.Length > 0) continue;
            var source = standard.Source.Replace("|", "¦", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
            additions.AppendLine($"| {standard.Id} | {rule} | imported standard targets | manual-review | imported source `{source}` | {standard.Status} | Maintainer must strengthen enforcement where practical. |");
        }
        if (additions.Length == 0) return new MatrixMerge(normalized, false);
        return new MatrixMerge(normalized.TrimEnd() + "\n" + additions, true);
    }

    private static RemoteSource ResolveRemote(Uri source)
    {
        if (!source.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            if (!source.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Remote standard sources must be GitHub repository URLs or direct HTTP(S) ZIP archives.");
            return new RemoteSource(source, null);
        }
        var parts = source.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries); if (parts.Length < 2) throw new InvalidDataException("GitHub standard URLs must include an owner and repository.");
        if (source.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return new RemoteSource(source, null);
        var owner = parts[0]; var repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1]; var reference = "HEAD"; string? subpath = null;
        if (!SafeGitHubSegment(owner) || !SafeGitHubSegment(repo)) throw new InvalidDataException("GitHub standard owner and repository names contain unsupported characters.");
        if (parts.Length >= 4 && parts[2].Equals("tree", StringComparison.OrdinalIgnoreCase)) { reference = Uri.UnescapeDataString(parts[3]); subpath = parts.Length > 4 ? string.Join('/', parts.Skip(4)) : null; }
        else if (parts.Length > 2) throw new InvalidDataException("Use a GitHub repository URL, tree/<ref>/<path> URL, or direct ZIP URL.");
        return new RemoteSource(new Uri($"https://github.com/{owner}/{repo}/archive/{Uri.EscapeDataString(reference)}.zip"), subpath);
    }

    private void Download(Uri uri, string destination)
    {
        using var response = _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        var finalUri = response.RequestMessage?.RequestUri ?? uri;
        if (!finalUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Standard download redirected to a non-HTTPS endpoint.");
        if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices) throw new HttpRequestException($"Standard download failed with HTTP {(int)response.StatusCode} from {uri}.");
        if (response.Content.Headers.ContentLength > MaximumDownloadBytes) throw new InvalidDataException("Standard archive exceeds the download limit.");
        using var input = response.Content.ReadAsStream(); using var output = File.Create(destination); CopyLimit(input, output, MaximumDownloadBytes);
    }

    private static void Extract(string archivePath, string destination)
        => CisArchiveSafety.ExtractZip(archivePath, destination, MaximumArchiveEntries, MaximumExpandedBytes);

    private static string ResolveScanRoot(string extraction, string? subpath)
    {
        if (string.IsNullOrWhiteSpace(subpath)) return extraction; var roots = Directory.EnumerateDirectories(extraction).ToArray(); var root = roots.Length == 1 ? roots[0] : extraction;
        if (!CisPathSafety.TryResolveUnderRoot(root, subpath, out var resolved) || !Directory.Exists(resolved)) throw new InvalidDataException($"GitHub archive does not contain requested standard path '{subpath}'."); return resolved;
    }

    private static void CopyLimit(Stream input, Stream output, long limit)
    {
        var buffer = new byte[81920]; long copied = 0; int read; while ((read = input.Read(buffer)) > 0) { copied += read; if (copied > limit) throw new InvalidDataException("Standard download exceeds the size limit."); output.Write(buffer, 0, read); }
    }

    private static bool TryFrontMatter(string content, out string yaml, out string body)
    {
        yaml = string.Empty; body = content; using var reader = new StringReader(content); if (reader.ReadLine()?.TrimStart('\uFEFF') != "---") return false;
        var lines = new List<string>(); string? line; while ((line = reader.ReadLine()) is not null) { if (line == "---") { yaml = string.Join('\n', lines); body = reader.ReadToEnd(); return true; } lines.Add(line); } return false;
    }

    private static string Scalar(IReadOnlyDictionary<object, object?> metadata, string key) => metadata.FirstOrDefault(item => item.Key.ToString()?.Equals(key, StringComparison.OrdinalIgnoreCase) == true).Value?.ToString()?.Trim() ?? string.Empty;
    private static string NestedScalar(IReadOnlyDictionary<object, object?> metadata, string parent, string child) => metadata.FirstOrDefault(item => item.Key.ToString()?.Equals(parent, StringComparison.OrdinalIgnoreCase) == true).Value is IReadOnlyDictionary<object, object?> nested ? Scalar(nested, child) : string.Empty;
    private static IReadOnlyList<string> Sequence(IReadOnlyDictionary<object, object?> metadata, string key) => metadata.FirstOrDefault(item => item.Key.ToString()?.Equals(key, StringComparison.OrdinalIgnoreCase) == true).Value is IEnumerable<object> values ? values.Select(value => value.ToString()?.Trim() ?? string.Empty).Where(value => value.Length > 0).ToArray() : [];
    private static void AddIfMissing(IReadOnlyDictionary<object, object?> metadata, ICollection<string> additions, string key, string rendered) { if (Scalar(metadata, key).Length == 0 && Sequence(metadata, key).Count == 0) additions.Add(rendered); }
    private static Regex SectionRegex(string name) => new($@"(?im)^##\s+{Regex.Escape(name)}\s*$", RegexOptions.CultureInvariant);
    private static string TitleFromPath(string path) => string.Join(' ', Path.GetFileNameWithoutExtension(path).Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    private static string AssignRuleIds(string body, string slug, out int assigned)
    {
        var initials = string.Concat(slug.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0])));
        if (initials.Length == 0) initials = "STD";
        if (initials.Length > 6) initials = initials[..6];
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(slug)))[..4];
        var prefix = $"STD-{initials}-H{hash}";
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var fenced = false; assigned = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)) { fenced = !fenced; continue; }
            if (fenced || trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith('|') || trimmed.StartsWith('>') || !NormativeStatementRegex().IsMatch(lines[index])) continue;
            assigned++; var id = $"{prefix}-{assigned:000}"; var bullet = BulletPrefixRegex().Match(lines[index]);
            lines[index] = bullet.Success
                ? lines[index][..bullet.Length] + $"**{id}** " + lines[index][bullet.Length..]
                : $"- **{id}** " + lines[index].Trim();
        }
        return string.Join('\n', lines);
    }
    private static string YamlScalar(string value) => '"' + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
    private static string Sha256(string content) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    private static bool SafeGitHubSegment(string value) => value.Length is > 0 and <= 100
        && value is not "." and not ".."
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
    private static void Write(string path, string content) { var temporary = path + ".tmp"; File.WriteAllText(temporary, content); File.Move(temporary, path, overwrite: true); }
    private static void WriteImportRecord(CisRepositoryContext context, IReadOnlyList<StandardImportItem> items) { var root = Path.Combine(context.RepositoryPath, ".cis", "local", "standards"); Directory.CreateDirectory(root); Write(Path.Combine(root, "imports.json"), System.Text.Json.JsonSerializer.Serialize(items, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true }) + "\n"); }
    private static StandardImportResult Result(string status, int exit, string? repo, StandardImportRequest request, IReadOnlyList<StandardImportItem> items, IReadOnlyList<string> warnings, IReadOnlyList<string> conflicts, IReadOnlyList<string> errors, bool confirmationRequired = false, bool applied = false) => new(status, exit, repo, request.DryRun, confirmationRequired, applied, items, warnings.Distinct().ToArray(), conflicts.Distinct().ToArray(), errors.Distinct().ToArray());

    private sealed record ImportCandidate(string Path, string Source);
    private sealed record PreparedStandard(string Id, string Slug, string Title, string Status, string Source, string Content, string Hash, IReadOnlyList<string> RuleIds);
    private sealed record RemoteSource(Uri Archive, string? Subpath);
    private sealed record MatrixMerge(string Content, bool Changed);
    [GeneratedRegex(@"(?m)^#\s+(.+?)\s*$", RegexOptions.CultureInvariant)] private static partial Regex HeadingRegex();
    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)] private static partial Regex SlugRegex();
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9:._-]+$", RegexOptions.CultureInvariant)] private static partial Regex StableIdRegex();
    [GeneratedRegex(@"\b(?:MUST(?:\s+NOT)?|SHOULD|MAY)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex NormativeStatementRegex();
    [GeneratedRegex(@"^\s*[-*]\s+(?:\[[ xX]\]\s+)?", RegexOptions.CultureInvariant)] private static partial Regex BulletPrefixRegex();
}
