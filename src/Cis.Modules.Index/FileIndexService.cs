using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Index;

public sealed partial class FileIndexService
{
    private const string PromptVersion = "cis-file-index-card-v3";
    private const string IndexRoot = ".cis/local/index-cards";
    private const string StatusCacheName = "status.json";
    private const int StatusCacheSchemaVersion = 1;
    private const int ManifestCheckpointInterval = 10;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".codex-tmp", ".vs", ".idea", "bin", "obj", "node_modules", ".artifacts", "artifacts", "coverage",
        "TestResults", "dist", ".next", ".godot", ".gradle", "skills-quarantine",
    };
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".fs", ".vb", ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs",
        ".swift", ".kt", ".kts", ".py", ".sql", ".tf", ".tfvars", ".go", ".rs",
        ".java", ".cpp", ".c", ".h", ".hpp", ".gd", ".gdshader", ".shader",
        ".md", ".mdx", ".yml", ".yaml", ".json", ".xml", ".toml", ".ini",
        ".config", ".props", ".targets", ".csproj", ".fsproj", ".vbproj", ".sln",
        ".slnx", ".gradle", ".sh", ".ps1", ".bat", ".cmd", ".editorconfig",
    };
    private static readonly HashSet<string> SupportedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dockerfile", "Makefile", "Procfile", "global.json", "package.json",
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly ICisRepositoryContextResolver _resolver;
    private readonly ICisTextGenerationService _generation;

    public FileIndexService(
        ICisRepositoryContextResolver resolver,
        ICisTextGenerationService generation)
    {
        _resolver = resolver;
        _generation = generation;
    }

    public FileIndexBuildResult Build(FileIndexBuildRequest request)
    {
        var resolution = _resolver.Resolve(request.RepositoryPath);
        if (!resolution.IsSuccess)
        {
            return BuildFailure("invalid-repository", resolution.Errors, repositoryConfigurationValid: false);
        }

        if (request.Limit is < 0 or > 100_000 || request.MaxInputCharacters is < 1_000 or > 100_000)
        {
            return BuildFailure("invalid-request",
                ["Limit must be from 0 to 100000 and max-input-chars from 1000 to 100000."],
                repositoryConfigurationValid: true);
        }

        var context = resolution.Context!;
        var selection = ResolveSelection(context.RepositoryPath, request.Path);
        if (selection.Error is not null)
        {
            return BuildFailure("invalid-request", [selection.Error], repositoryConfigurationValid: true);
        }

        var candidates = DiscoverFiles(context.RepositoryPath, selection.AbsolutePath).ToArray();
        var outputRoot = Path.Combine(context.RepositoryPath, IndexRoot.Replace('/', Path.DirectorySeparatorChar));
        var manifestPath = Path.Combine(outputRoot, "index.json");
        var existing = ReadCards(manifestPath);
        var currentPaths = candidates.Select(candidate => candidate.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removedCards = existing
            .Where(card => IsWithinSelection(context.RepositoryPath, card.Path, selection.AbsolutePath)
                && !currentPaths.Contains(card.Path))
            .ToArray();
        var removedPaths = removedCards.Select(card => card.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retained = existing
            .Where(card => !removedPaths.Contains(card.Path))
            .ToDictionary(card => card.Path, StringComparer.OrdinalIgnoreCase);
        var sourceHashes = candidates.ToDictionary(
            candidate => candidate.RelativePath,
            candidate => HashFile(candidate.AbsolutePath),
            StringComparer.OrdinalIgnoreCase);
        var route = ResolveRoute(request.Provider, request.Model);
        var requiresProvider = candidates.Any(candidate => !IsSensitive(candidate.RelativePath));
        if (requiresProvider && route.Error is not null)
        {
            var reusable = !request.Refresh && candidates
                .Where(candidate => !IsSensitive(candidate.RelativePath))
                .All(candidate => retained.TryGetValue(candidate.RelativePath, out var cached)
                    && cached.SourceHash == sourceHashes[candidate.RelativePath]
                    && cached.PromptVersion == PromptVersion
                    && (string.IsNullOrWhiteSpace(request.Provider)
                        || string.Equals(cached.Provider, request.Provider, StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrWhiteSpace(request.Model)
                        || string.Equals(cached.Model, request.Model, StringComparison.OrdinalIgnoreCase))
                    && File.Exists(Path.Combine(context.RepositoryPath,
                        cached.CardPath.Replace('/', Path.DirectorySeparatorChar))));
            if (!reusable)
            {
                return new FileIndexBuildResult(
                    "provider-unavailable", context.RepositoryPath, ToRelative(context.RepositoryPath, outputRoot),
                    route.Provider, route.Model, candidates.Length, existing.Count, 0, 0, candidates.Length, 0,
                    [], [route.Error], false, true, true, false);
            }

            var cachedRoute = candidates
                .Where(candidate => !IsSensitive(candidate.RelativePath))
                .Select(candidate => retained[candidate.RelativePath])
                .FirstOrDefault();
            route = (cachedRoute?.Provider, cachedRoute?.Model, cachedRoute?.IsLocal, null);
        }

        if (route.IsLocal == false && !request.AllowRemote && requiresProvider)
        {
            return new FileIndexBuildResult(
                "remote-approval-required", context.RepositoryPath, ToRelative(context.RepositoryPath, outputRoot),
                route.Provider, route.Model, candidates.Length, existing.Count, 0, 0, candidates.Length, 0,
                [], ["Remote index-card generation requires --allow-remote."], false, true, true, false);
        }

        var generated = 0;
        var reused = 0;
        var pending = 0;
        var skipped = new List<string>();
        var errors = new List<string>();
        var applied = false;
        var modelCalls = 0;
        Directory.CreateDirectory(Path.Combine(outputRoot, "cards"));

        foreach (var candidate in candidates)
        {
            var sourceHash = sourceHashes[candidate.RelativePath];
            var sensitive = IsSensitive(candidate.RelativePath);
            var expectedProvider = sensitive ? "deterministic" : route.Provider!;
            var expectedModel = sensitive ? null : route.Model;
            if (!request.Refresh
                && retained.TryGetValue(candidate.RelativePath, out var cached)
                && cached.SourceHash == sourceHash
                && cached.Provider == expectedProvider
                && cached.Model == expectedModel
                && cached.PromptVersion == PromptVersion
                && File.Exists(Path.Combine(context.RepositoryPath, cached.CardPath.Replace('/', Path.DirectorySeparatorChar))))
            {
                reused++;
                continue;
            }

            if (!sensitive && request.Limit > 0 && modelCalls >= request.Limit)
            {
                pending++;
                continue;
            }

            var input = ReadTextPrefix(candidate.AbsolutePath, request.MaxInputCharacters);
            if (input.Binary)
            {
                skipped.Add($"{candidate.RelativePath}: binary content");
                continue;
            }

            string summary;
            bool isLocal;
            if (sensitive)
            {
                summary = "Potentially sensitive configuration or credential material; content was not submitted to a model. Open only when the task explicitly requires this file.";
                isLocal = true;
            }
            else
            {
                var result = _generation.Generate(new CisTextGenerationRequest(
                    CreatePrompt(candidate.RelativePath, input.Text), route.Provider, route.Model,
                    request.AllowRemote,
                    TimeoutSeconds: 60,
                    MaxOutputTokens: 160));
                if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Text))
                {
                    errors.Add($"{candidate.RelativePath}: {result.Detail ?? result.Status}");
                    continue;
                }

                summary = NormalizeSummary(candidate.RelativePath, input.Text, result.Text);
                isLocal = result.IsLocal;
                modelCalls++;
            }

            var cardRelativePath = $"{IndexRoot}/cards/{HashText(candidate.RelativePath)[7..23]}.card.md";
            var card = new FileIndexCard(
                candidate.RelativePath, sourceHash, summary, expectedProvider, expectedModel, PromptVersion, isLocal,
                input.Truncated, sensitive, cardRelativePath, DateTimeOffset.UtcNow.ToString("O"));
            var absoluteCardPath = Path.Combine(context.RepositoryPath,
                cardRelativePath.Replace('/', Path.DirectorySeparatorChar));
            applied |= WriteIfChanged(absoluteCardPath, RenderCard(card));
            retained[candidate.RelativePath] = card;
            generated++;
            if (generated % ManifestCheckpointInterval == 0)
            {
                applied |= WriteIfChanged(
                    manifestPath,
                    JsonSerializer.Serialize(
                        retained.Values.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase),
                        JsonOptions) + Environment.NewLine);
            }
        }

        foreach (var removed in removedCards)
        {
            var absoluteCardPath = Path.Combine(context.RepositoryPath,
                removed.CardPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(absoluteCardPath))
            {
                File.Delete(absoluteCardPath);
                applied = true;
            }
        }

        var cards = retained.Values.OrderBy(card => card.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        applied |= WriteIfChanged(manifestPath, JsonSerializer.Serialize(cards, JsonOptions) + Environment.NewLine);
        applied |= WriteIfChanged(Path.Combine(outputRoot, "README.md"), RenderIndex(cards));
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            var statusResult = CreateStatusResult(
                context.RepositoryPath, outputRoot, candidates, cards, sourceHashes, cached: false);
            WriteStatusCache(outputRoot, manifestPath, candidates, statusResult);
        }
        var status = errors.Count > 0 ? "partial" : pending > 0 ? "incomplete" : applied ? "built" : "unchanged";
        return new FileIndexBuildResult(
            status, context.RepositoryPath, ToRelative(context.RepositoryPath, outputRoot), route.Provider, route.Model,
            candidates.Length, cards.Length, generated, reused, pending, removedCards.Length, skipped, errors,
            applied, true, true, true);
    }

    public FileIndexStatusResult Status(string repositoryPath, string? path)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess)
        {
            return new("invalid-repository", null, null, 0, 0, 0, 0, 0, 0,
                resolution.Errors, false, false);
        }

        var context = resolution.Context!;
        var selection = ResolveSelection(context.RepositoryPath, path);
        if (selection.Error is not null)
        {
            return new("invalid-request", context.RepositoryPath, null, 0, 0, 0, 0, 0, 0,
                [selection.Error], true, false);
        }

        var outputRoot = Path.Combine(context.RepositoryPath, IndexRoot.Replace('/', Path.DirectorySeparatorChar));
        var manifestPath = Path.Combine(outputRoot, "index.json");
        if (!File.Exists(manifestPath))
        {
            return new("unavailable", context.RepositoryPath, ToRelative(context.RepositoryPath, outputRoot),
                0, 0, 0, 0, 0, 0, [], true, false);
        }

        var candidates = DiscoverFiles(context.RepositoryPath, selection.AbsolutePath).ToArray();
        var cards = ReadCards(manifestPath);
        if (string.IsNullOrWhiteSpace(path)
            && TryReadStatusCache(outputRoot, manifestPath, candidates, out var cached))
        {
            return cached! with { Cached = true };
        }

        var byPath = cards.ToDictionary(card => card.Path, StringComparer.OrdinalIgnoreCase);
        var fresh = 0;
        var stale = 0;
        var missing = 0;
        foreach (var candidate in candidates)
        {
            if (!byPath.TryGetValue(candidate.RelativePath, out var card))
            {
                missing++;
            }
            else if (card.SourceHash == HashFile(candidate.AbsolutePath))
            {
                fresh++;
            }
            else
            {
                stale++;
            }
        }

        var paths = candidates.Select(candidate => candidate.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = cards.Count(card =>
            IsWithinSelection(context.RepositoryPath, card.Path, selection.AbsolutePath)
            && !paths.Contains(card.Path));
        var status = stale == 0 && missing == 0 && removed == 0 ? "fresh" : "stale";
        var result = new FileIndexStatusResult(status, context.RepositoryPath, ToRelative(context.RepositoryPath, outputRoot), candidates.Length,
            cards.Count, fresh, stale, missing, removed, [], true, true);
        if (string.IsNullOrWhiteSpace(path)) WriteStatusCache(outputRoot, manifestPath, candidates, result);
        return result;
    }

    public FileIndexFindResult Find(string repositoryPath, string query, int limit)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess)
        {
            return new("invalid-repository", null, query, [], resolution.Errors, false, false, true);
        }

        if (string.IsNullOrWhiteSpace(query) || limit is < 1 or > 1000)
        {
            return new("invalid-request", resolution.Context!.RepositoryPath, query, [],
                ["A query is required and limit must be from 1 to 1000."], true, true, false);
        }

        var context = resolution.Context!;
        var manifestPath = Path.Combine(context.RepositoryPath,
            IndexRoot.Replace('/', Path.DirectorySeparatorChar), "index.json");
        if (!File.Exists(manifestPath))
        {
            return new("unavailable", context.RepositoryPath, query, [], [], true, false, true);
        }

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = ReadCards(manifestPath)
            .Select(card => (Card: card, Score: Score(card, terms)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Card.Path, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(candidate => candidate.Card)
            .ToArray();
        return new(matches.Length == 0 ? "no-match" : "found", context.RepositoryPath, query,
            matches, [], true, true, true);
    }

    private (string? Provider, string? Model, bool? IsLocal, string? Error) ResolveRoute(
        string? requestedProvider,
        string? requestedModel)
    {
        var providers = _generation.GetStatus().Providers;
        CisAiProviderStatus? provider;
        if (!string.IsNullOrWhiteSpace(requestedProvider))
        {
            provider = providers.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, requestedProvider.Trim(), StringComparison.OrdinalIgnoreCase)
                && candidate.IsAvailable);
        }
        else
        {
            provider = providers.FirstOrDefault(candidate => candidate.IsLocal && candidate.IsAvailable);
        }

        if (provider is null)
        {
            return (requestedProvider, requestedModel, null,
                "No selectable text-generation provider is available.");
        }

        var model = string.IsNullOrWhiteSpace(requestedModel)
            ? provider.Models.OrderBy(candidate => candidate.SizeBytes ?? long.MaxValue)
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault()?.Name
            : provider.Models.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, requestedModel.Trim(), StringComparison.OrdinalIgnoreCase))?.Name;
        return model is null
            ? (provider.Name, requestedModel, provider.IsLocal, "No selectable model is available for the provider.")
            : (provider.Name, model, provider.IsLocal, null);
    }

    private static (string AbsolutePath, string? Error) ResolveSelection(string repositoryPath, string? selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return (repositoryPath, null);
        }

        if (Path.IsPathRooted(selectedPath))
        {
            return (repositoryPath, "Path must be repository-relative.");
        }

        var absolute = Path.GetFullPath(Path.Combine(repositoryPath,
            selectedPath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = repositoryPath + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(prefix, PathComparison) || (!File.Exists(absolute) && !Directory.Exists(absolute)))
        {
            return (repositoryPath, "Path must resolve to an existing file or directory inside the repository.");
        }

        return (absolute, null);
    }

    private static IEnumerable<FileCandidate> DiscoverFiles(string repositoryPath, string selectedPath)
    {
        var files = File.Exists(selectedPath)
            ? [selectedPath]
            : EnumerateFiles(repositoryPath, selectedPath);
        return files
            .Where(path => IsEligible(repositoryPath, path))
            .Select(path => new FileCandidate(path, ToRelative(repositoryPath, path)))
            .OrderBy(candidate => candidate.RelativePath, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateFiles(string repositoryPath, string selectedPath)
    {
        var pending = new Stack<string>();
        if (!IsExcludedDirectory(repositoryPath, selectedPath))
        {
            pending.Push(selectedPath);
        }

        while (pending.TryPop(out var directory))
        {
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (!IsExcludedDirectory(repositoryPath, child)
                    && !new DirectoryInfo(child).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    pending.Push(child);
                }
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                yield return file;
            }
        }
    }

    private static bool IsExcludedDirectory(string repositoryPath, string path)
    {
        var segments = ToRelative(repositoryPath, path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            if (ExcludedDirectories.Contains(segments[index])
                || IsGeneratedBuildDirectory(segments, index)
                || (string.Equals(segments[index], ".cis", StringComparison.OrdinalIgnoreCase)
                    && index + 1 < segments.Length
                    && string.Equals(segments[index + 1], "local", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEligible(string repositoryPath, string path)
    {
        var relative = ToRelative(repositoryPath, path);
        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (ExcludedDirectories.Contains(segments[index])
                || IsGeneratedBuildDirectory(segments, index)
                || (string.Equals(segments[index], ".cis", StringComparison.OrdinalIgnoreCase)
                    && index + 1 < segments.Length
                    && string.Equals(segments[index + 1], "local", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        var name = Path.GetFileName(path);
        return name.StartsWith(".env", StringComparison.OrdinalIgnoreCase)
            || SupportedNames.Contains(name)
            || SupportedExtensions.Contains(Path.GetExtension(name));
    }

    private static bool IsGeneratedBuildDirectory(IReadOnlyList<string> segments, int index) =>
        index > 0
        && string.Equals(segments[index], "build", StringComparison.OrdinalIgnoreCase)
        && (string.Equals(segments[index - 1], "android", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segments[index - 1], "plugin", StringComparison.OrdinalIgnoreCase));

    private static bool IsSensitive(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        var extension = Path.GetExtension(name);
        return name.StartsWith(".env", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pem", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".key", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase)
            || SensitiveNamePattern().IsMatch(name);
    }

    private static TextPrefix ReadTextPrefix(string path, int maxCharacters)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[maxCharacters + 1];
        var count = reader.ReadBlock(buffer, 0, buffer.Length);
        var text = new string(buffer, 0, Math.Min(count, maxCharacters));
        return new(text, count > maxCharacters, text.Contains('\0'));
    }

    private static string CreatePrompt(string relativePath, string content) => $$"""
        Summarize this repository file for low-token engineering routing.
        Return one factual sentence of at most 240 characters.
        State the file's primary responsibility and the kind of change that should load it.
        Do not invent behavior, copy secrets, list every member, or use Markdown.

        Path: {{relativePath}}
        Content:
        {{content}}
        """;

    private static string NormalizeSummary(string relativePath, string input, string text)
    {
        var summary = WhitespacePattern().Replace(text.Trim().Trim('"', '\'', '`'), " ")
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .TrimStart('-', '*', ' ');
        var sentenceEnd = summary.IndexOf(". ", StringComparison.Ordinal);
        if (sentenceEnd is >= 40 and < 240)
        {
            summary = summary[..(sentenceEnd + 1)];
        }

        if (MentionsUnsupportedTechnology(summary, relativePath, input, "Unity"))
        {
            summary = SafeFallbackSummary(relativePath);
        }

        return summary.Length <= 240 ? summary : summary[..237].TrimEnd() + "...";
    }

    private static bool MentionsUnsupportedTechnology(
        string summary,
        string relativePath,
        string input,
        string technology) =>
        summary.Contains(technology, StringComparison.OrdinalIgnoreCase)
        && !relativePath.Contains(technology, StringComparison.OrdinalIgnoreCase)
        && !input.Contains(technology, StringComparison.OrdinalIgnoreCase);

    private static string SafeFallbackSummary(string relativePath)
    {
        var kind = Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".gd" => "Godot script",
            ".gdshader" or ".shader" => "shader source",
            ".csproj" or ".fsproj" or ".vbproj" => ".NET project file",
            ".cs" => "C# source file",
            _ => "repository file",
        };
        return $"{kind} at {relativePath}; open it to confirm its exact responsibility before related implementation, configuration, or dependency changes.";
    }

    private static string RenderCard(FileIndexCard card) => $$"""
        ---
        title: "{{YamlEscape(Path.GetFileName(card.Path))}} file index card"
        type: file-index-card
        status: Derived
        source_file: "{{YamlEscape(card.Path)}}"
        source_hash: "{{card.SourceHash}}"
        generator: "{{YamlEscape(card.Provider)}}"
        model: "{{YamlEscape(card.Model ?? "none")}}"
        prompt_version: "{{card.PromptVersion}}"
        generated_at: "{{card.GeneratedAt}}"
        ---

        # {{Path.GetFileName(card.Path)}} — file index card

        This is a non-authoritative, model-assisted routing aid. The source file remains authoritative.

        ## Source

        `{{card.Path}}`

        ## Routing summary

        {{card.Summary}}

        ## Generation notes

        - Provider: `{{card.Provider}}`
        - Model: `{{card.Model ?? "none"}}`
        - Local inference: `{{card.IsLocal.ToString().ToLowerInvariant()}}`
        - Input truncated: `{{card.InputTruncated.ToString().ToLowerInvariant()}}`
        - Sensitive-content safeguard: `{{card.Sensitive.ToString().ToLowerInvariant()}}`
        """ + Environment.NewLine;

    private static string RenderIndex(IReadOnlyList<FileIndexCard> cards)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# CIS file index cards");
        builder.AppendLine();
        builder.AppendLine("Derived, non-authoritative routing summaries. Open the source file for authoritative detail.");
        builder.AppendLine();
        builder.AppendLine("| Source | Summary | Card |");
        builder.AppendLine("| --- | --- | --- |");
        foreach (var card in cards)
        {
            var link = Path.GetRelativePath(IndexRoot, card.CardPath).Replace('\\', '/');
            builder.AppendLine($"| `{EscapeTable(card.Path)}` | {EscapeTable(card.Summary)} | [card]({link}) |");
        }

        return builder.ToString();
    }

    private static IReadOnlyList<FileIndexCard> ReadCards(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<FileIndexCard[]>(File.ReadAllText(manifestPath), JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static FileIndexStatusResult CreateStatusResult(
        string repositoryPath,
        string outputRoot,
        IReadOnlyList<FileCandidate> candidates,
        IReadOnlyList<FileIndexCard> cards,
        IReadOnlyDictionary<string, string> sourceHashes,
        bool cached)
    {
        var byPath = cards.ToDictionary(card => card.Path, StringComparer.OrdinalIgnoreCase);
        var fresh = candidates.Count(candidate => byPath.TryGetValue(candidate.RelativePath, out var card)
            && string.Equals(card.SourceHash, sourceHashes[candidate.RelativePath], StringComparison.Ordinal));
        var stale = candidates.Count(candidate => byPath.TryGetValue(candidate.RelativePath, out var card)
            && !string.Equals(card.SourceHash, sourceHashes[candidate.RelativePath], StringComparison.Ordinal));
        var missing = candidates.Count - fresh - stale;
        var paths = candidates.Select(candidate => candidate.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = cards.Count(card => !paths.Contains(card.Path));
        return new FileIndexStatusResult(
            stale == 0 && missing == 0 && removed == 0 ? "fresh" : "stale",
            repositoryPath,
            ToRelative(repositoryPath, outputRoot),
            candidates.Count,
            cards.Count,
            fresh,
            stale,
            missing,
            removed,
            [],
            RepositoryConfigurationValid: true,
            IndexAvailable: true,
            Cached: cached);
    }

    private static bool TryReadStatusCache(
        string outputRoot,
        string manifestPath,
        IReadOnlyList<FileCandidate> candidates,
        out FileIndexStatusResult? result)
    {
        result = null;
        var cachePath = Path.Combine(outputRoot, StatusCacheName);
        if (!File.Exists(cachePath)) return false;
        try
        {
            var cache = JsonSerializer.Deserialize<FileIndexStatusCache>(File.ReadAllText(cachePath), JsonOptions);
            if (cache is null || cache.SchemaVersion != StatusCacheSchemaVersion
                || cache.Manifest != Snapshot(manifestPath)
                || !cache.Sources.SequenceEqual(candidates.Select(Snapshot)))
            {
                return false;
            }

            result = cache.Result;
            return result is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static void WriteStatusCache(
        string outputRoot,
        string manifestPath,
        IReadOnlyList<FileCandidate> candidates,
        FileIndexStatusResult result)
    {
        try
        {
            var cache = new FileIndexStatusCache(
                StatusCacheSchemaVersion,
                DateTimeOffset.UtcNow,
                Snapshot(manifestPath),
                candidates.Select(Snapshot).ToArray(),
                result with { Cached = false });
            var path = Path.Combine(outputRoot, StatusCacheName);
            var content = JsonSerializer.Serialize(cache, JsonOptions) + Environment.NewLine;
            if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal)) return;
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A missing cache only makes the next status calculation slower.
        }
    }

    private static FileStatusSnapshot Snapshot(FileCandidate candidate) => Snapshot(candidate.AbsolutePath, candidate.RelativePath);

    private static FileStatusSnapshot Snapshot(string path, string? identity = null)
    {
        var info = new FileInfo(path);
        return new FileStatusSnapshot(identity ?? info.Name, info.Length, info.LastWriteTimeUtc.Ticks);
    }

    private static bool WriteIfChanged(string path, string content)
    {
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }

    private static int Score(FileIndexCard card, IReadOnlyList<string> terms)
    {
        var score = 0;
        foreach (var term in terms)
        {
            if (card.Path.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
            }

            if (card.Summary.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
            }
        }

        return score;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string HashText(string value) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string ToRelative(string repositoryPath, string path) =>
        Path.GetRelativePath(repositoryPath, path).Replace('\\', '/');

    private static bool IsWithinSelection(string repositoryPath, string cardPath, string selectionPath)
    {
        var absoluteCardSource = Path.GetFullPath(Path.Combine(repositoryPath,
            cardPath.Replace('/', Path.DirectorySeparatorChar)));
        if (File.Exists(selectionPath))
        {
            return string.Equals(absoluteCardSource, selectionPath, PathComparison);
        }

        if (string.Equals(selectionPath, repositoryPath, PathComparison))
        {
            return true;
        }

        return absoluteCardSource.StartsWith(
            Path.TrimEndingDirectorySeparator(selectionPath) + Path.DirectorySeparatorChar,
            PathComparison);
    }

    private static string YamlEscape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string EscapeTable(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private static FileIndexBuildResult BuildFailure(
        string status,
        IReadOnlyList<string> errors,
        bool repositoryConfigurationValid) =>
        new(status, null, null, null, null, 0, 0, 0, 0, 0, 0, [], errors,
            false, repositoryConfigurationValid, status != "invalid-request", false);

    private sealed record FileCandidate(string AbsolutePath, string RelativePath);

    private sealed record FileStatusSnapshot(string Path, long Length, long LastWriteUtcTicks);

    private sealed record FileIndexStatusCache(
        int SchemaVersion,
        DateTimeOffset GeneratedAt,
        FileStatusSnapshot Manifest,
        IReadOnlyList<FileStatusSnapshot> Sources,
        FileIndexStatusResult Result);

    private sealed record TextPrefix(string Text, bool Truncated, bool Binary);

    [GeneratedRegex("(?i)(^|[-_.])(secrets?|credentials?|passwords?|tokens?|private[-_.]?keys?)([-_.]|$)")]
    private static partial Regex SensitiveNamePattern();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespacePattern();
}
