using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Ai;

public sealed record AiRoute(string Capability, string Provider, string Model, bool AllowRemote, bool Cache);
public sealed record AiUsageRecord(string TimestampUtc, string Capability, string Provider, string Model, string PromptHash,
    int EstimatedInputTokens, int EstimatedOutputTokens, long DurationMilliseconds, bool Local, bool CacheHit, string Status);
public sealed record AiGovernanceResult(string Status, string? RepositoryPath, IReadOnlyList<AiRoute> Routes,
    IReadOnlyList<CisAiProviderStatus> Providers, IReadOnlyList<AiUsageRecord> Usage, int CacheEntries,
    CisTextGenerationResult? Generation, IReadOnlyList<string> Errors)
{
    public int ExitCode => Errors.Count > 0 || Generation is { IsSuccess: false } ? 4 : 0;
}

public sealed class AiGovernanceService
{
    public const string UsagePath = ".cis/local/ai/usage.jsonl";
    public const string CachePath = ".cis/local/ai/cache";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ICisTextGenerationService _generation;
    private readonly ICisRepositoryContextResolver _resolver;
    private readonly Func<DateTimeOffset> _clock;

    public AiGovernanceService(ICisTextGenerationService generation, ICisRepositoryContextResolver resolver, Func<DateTimeOffset>? clock = null)
    {
        _generation = generation; _resolver = resolver; _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public AiGovernanceResult Providers(string repositoryPath)
    {
        var context = Resolve(repositoryPath, out var errors);
        return new(errors.Count == 0 ? "available" : "invalid-repository", context?.RepositoryPath,
            context is null ? [] : ReadRoutes(context), _generation.GetStatus().Providers, [], context is null ? 0 : CountCache(context), null, errors);
    }

    public AiGovernanceResult Usage(string repositoryPath, int limit = 100)
    {
        var context = Resolve(repositoryPath, out var errors);
        var usage = context is null ? [] : ReadUsage(context).TakeLast(Math.Clamp(limit, 1, 1000)).ToArray();
        return new(errors.Count == 0 ? "available" : "invalid-repository", context?.RepositoryPath,
            context is null ? [] : ReadRoutes(context), _generation.GetStatus().Providers, usage,
            context is null ? 0 : CountCache(context), null, errors);
    }

    public AiGovernanceResult Evaluate(string repositoryPath, string capability, string prompt, bool allowRemote, bool useCache)
    {
        var context = Resolve(repositoryPath, out var errors);
        if (context is null) return new("invalid-repository", null, [], _generation.GetStatus().Providers, [], 0, null, errors);
        var routes = ReadRoutes(context);
        var route = routes.FirstOrDefault(item => item.Capability.Equals(capability, StringComparison.OrdinalIgnoreCase));
        if (route is null) errors.Add($"No AI route is configured for capability '{capability}'.");
        if (string.IsNullOrWhiteSpace(prompt)) errors.Add("A non-empty prompt is required.");
        if (route is not null && route.AllowRemote && !allowRemote && !route.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            errors.Add("This route uses a remote provider; pass --allow-remote only after approving the submitted content.");
        if (errors.Count > 0) return new("invalid-request", context.RepositoryPath, routes, _generation.GetStatus().Providers, [], CountCache(context), null, errors);

        var promptHash = Sha256(prompt);
        var key = Sha256($"{route!.Capability}\n{route.Provider}\n{route.Model}\n{promptHash}");
        var cacheFile = Path.Combine(context.RepositoryPath, CachePath.Replace('/', Path.DirectorySeparatorChar), key + ".json");
        if (useCache && route.Cache && File.Exists(cacheFile))
        {
            var cached = JsonSerializer.Deserialize<CisTextGenerationResult>(File.ReadAllText(cacheFile), JsonOptions);
            if (cached?.IsSuccess == true)
            {
                var record = Record(context, route, promptHash, prompt, cached, 0, true);
                return new("cached", context.RepositoryPath, routes, _generation.GetStatus().Providers, [record], CountCache(context), cached, []);
            }
        }

        var timer = Stopwatch.StartNew();
        var requestedModel = route.Model.Equals("repository-smallest-local", StringComparison.OrdinalIgnoreCase) ? null : route.Model;
        var generated = _generation.Generate(new(prompt, route.Provider, requestedModel,
            allowRemote && route.AllowRemote, 120));
        timer.Stop();
        if (generated.IsSuccess && useCache && route.Cache)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            File.WriteAllText(cacheFile, JsonSerializer.Serialize(generated, JsonOptions));
        }
        var usage = Record(context, route, promptHash, prompt, generated, timer.ElapsedMilliseconds, false);
        return new(generated.IsSuccess ? "generated" : "failed", context.RepositoryPath, routes,
            _generation.GetStatus().Providers, [usage], CountCache(context), generated,
            generated.IsSuccess ? [] : [generated.Detail ?? "Generation failed."]);
    }

    private AiUsageRecord Record(CisRepositoryContext context, AiRoute route, string promptHash, string prompt,
        CisTextGenerationResult result, long elapsed, bool cacheHit)
    {
        var record = new AiUsageRecord(_clock().ToUniversalTime().ToString("O"), route.Capability,
            result.Provider ?? route.Provider, result.Model ?? route.Model, promptHash,
            EstimateTokens(prompt), EstimateTokens(result.Text), elapsed, result.IsLocal, cacheHit, result.Status);
        var path = Path.Combine(context.RepositoryPath, UsagePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine);
        return record;
    }

    private static int EstimateTokens(string? value) => string.IsNullOrEmpty(value) ? 0 : Math.Max(1, (value.Length + 3) / 4);

    public static IReadOnlyList<AiRoute> ReadRoutes(CisRepositoryContext context)
    {
        var path = Path.Combine(context.DocumentationPath, "references", "ai-routing-profile.md");
        if (!File.Exists(path)) return [];
        var routes = new List<AiRoute>();
        foreach (var line in File.ReadLines(path))
        {
            if (!line.TrimStart().StartsWith('|')) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length < 5 || cells[0].Equals("Capability", StringComparison.OrdinalIgnoreCase) || cells.All(cell => cell.All(ch => ch is '-' or ':' or ' '))) continue;
            routes.Add(new(cells[0], cells[1], cells[2], Yes(cells[3]), Yes(cells[4])));
        }
        return routes;
    }

    private static bool Yes(string value) => value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    private static int CountCache(CisRepositoryContext context)
    {
        var path = Path.Combine(context.RepositoryPath, CachePath.Replace('/', Path.DirectorySeparatorChar));
        return Directory.Exists(path) ? Directory.EnumerateFiles(path, "*.json").Count() : 0;
    }
    private static IReadOnlyList<AiUsageRecord> ReadUsage(CisRepositoryContext context)
    {
        var path = Path.Combine(context.RepositoryPath, UsagePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return [];
        return File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => { try { return JsonSerializer.Deserialize<AiUsageRecord>(line, JsonOptions); } catch (JsonException) { return null; } })
            .Where(item => item is not null).Cast<AiUsageRecord>().ToArray();
    }
    private CisRepositoryContext? Resolve(string path, out List<string> errors)
    {
        var resolution = _resolver.Resolve(path); errors = resolution.Errors.ToList(); return resolution.Context;
    }
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
