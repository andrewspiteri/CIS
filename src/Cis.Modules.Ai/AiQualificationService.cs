using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Ai;

public sealed record AiQualificationCase(
    string Id,
    string Status,
    long DurationMilliseconds,
    string OutputHash,
    IReadOnlyList<string> MissingTerms);

public sealed record AiQualificationEvidence(
    string EvidenceId,
    string Kind,
    string TimestampUtc,
    string Provider,
    string Model,
    string TaskClass,
    string Status,
    string ProfileDigest,
    string DatasetDigest,
    IReadOnlyList<AiQualificationCase> Cases,
    string? Detail);

public sealed record AiModelApproval(
    string Provider,
    string Model,
    string TaskClass,
    string State,
    string Reviewer,
    string Reason,
    string EvidenceDigest,
    string ApprovedUtc);

public sealed record AiRouteExplanation(
    string Capability,
    string Provider,
    string Model,
    bool AllowRemote,
    bool Cache,
    string ApprovalState,
    string Decision,
    IReadOnlyList<string> Reasons);

public sealed record AiQualificationResult(
    string Status,
    string? RepositoryPath,
    IReadOnlyList<AiQualificationEvidence> Evidence,
    IReadOnlyList<AiModelApproval> Approvals,
    IReadOnlyList<AiRouteExplanation> Routes,
    IReadOnlyList<string> Errors)
{
    public int ExitCode => Errors.Count == 0 && !Status.Equals("failed", StringComparison.OrdinalIgnoreCase) ? 0 : 4;
}

internal sealed record AiRegressionDataset(string TaskClass, IReadOnlyList<AiRegressionCase> Cases);
internal sealed record AiRegressionCase(string Id, string Prompt, IReadOnlyList<string> RequiredTerms);

public sealed class AiQualificationService
{
    public const string EvidenceRoot = ".cis/local/ai/qualification";
    public const string RegistryPath = "references/ai-model-registry.md";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly IReadOnlyList<ICisAiProvider> _providers;
    private readonly ICisRepositoryContextResolver _resolver;
    private readonly Func<DateTimeOffset> _clock;

    public AiQualificationService(
        IEnumerable<ICisAiProvider> providers,
        ICisRepositoryContextResolver resolver,
        Func<DateTimeOffset>? clock = null)
    {
        _providers = providers.ToArray();
        _resolver = resolver;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public AiQualificationResult Probe(string repositoryPath, string provider, string model, bool allowRemote)
    {
        var context = Resolve(repositoryPath, out var errors);
        if (context is null) return Failed(errors);
        var selected = SelectProvider(provider, allowRemote, errors);
        if (selected is null) return Result(context, "failed", errors: errors);

        var timer = Stopwatch.StartNew();
        var generated = selected.Generate(new(
            "Capability probe. Reply with the exact token CIS_PROBE_OK and nothing else.",
            provider, model, allowRemote, 60, 32), model);
        timer.Stop();
        var passed = generated.IsSuccess && string.Equals(generated.Text?.Trim(), "CIS_PROBE_OK", StringComparison.OrdinalIgnoreCase);
        var evidence = CreateEvidence(context, "probe", provider, model, "text-generation",
            passed ? "passed" : "failed", "built-in:text-generation:v1", "built-in:probe:v1",
            [Case("probe-text", generated, timer.ElapsedMilliseconds, passed ? [] : ["CIS_PROBE_OK"])],
            passed ? null : generated.Detail ?? "The provider did not return the exact probe token.");
        Persist(context, evidence);
        return Result(context, passed ? "passed" : "failed", [evidence], errors: passed ? [] : [evidence.Detail!]);
    }

    public AiQualificationResult Benchmark(string repositoryPath, string provider, string model, string taskClass, bool allowRemote)
    {
        var context = Resolve(repositoryPath, out var errors);
        if (context is null) return Failed(errors);
        var selected = SelectProvider(provider, allowRemote, errors);
        if (selected is null) return Result(context, "failed", errors: errors);
        if (string.IsNullOrWhiteSpace(taskClass)) return Result(context, "failed", errors: ["A task class is required."]);

        var prompts = new[]
        {
            ("plain-text", "Reply with the exact token CIS_TEXT_OK.", new[] { "CIS_TEXT_OK" }),
            ("structured-json", "Return only this JSON object: {\"cis\":\"ok\"}", new[] { "\"cis\"", "\"ok\"" }),
            ("instruction-following", "Reply with exactly three words: CIS MODEL READY", new[] { "CIS", "MODEL", "READY" }),
        };
        var cases = new List<AiQualificationCase>();
        foreach (var item in prompts)
        {
            var timer = Stopwatch.StartNew();
            var generated = selected.Generate(new(item.Item2, provider, model, allowRemote, 90, 64), model);
            timer.Stop();
            var missing = item.Item3.Where(term => !(generated.Text ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
            cases.Add(Case(item.Item1, generated, timer.ElapsedMilliseconds, missing));
        }
        var passed = cases.All(item => item.Status == "passed");
        var evidence = CreateEvidence(context, "benchmark", provider, model, taskClass,
            passed ? "passed" : "failed", "built-in:qualification:v1", "built-in:benchmark:v1", cases,
            passed ? null : "One or more deterministic benchmark cases failed.");
        Persist(context, evidence);
        return Result(context, passed ? "passed" : "failed", [evidence], errors: passed ? [] : [evidence.Detail!]);
    }

    public AiQualificationResult PromptRegression(
        string repositoryPath,
        string provider,
        string model,
        string taskClass,
        string datasetPath,
        bool allowRemote)
    {
        var context = Resolve(repositoryPath, out var errors);
        if (context is null) return Failed(errors);
        var selected = SelectProvider(provider, allowRemote, errors);
        if (selected is null) return Result(context, "failed", errors: errors);
        var fullDatasetPath = SafeDatasetPath(context, datasetPath, errors);
        var dataset = fullDatasetPath is null ? null : ReadDataset(fullDatasetPath, errors);
        if (dataset is null) return Result(context, "failed", errors: errors);
        if (!dataset.TaskClass.Equals(taskClass, StringComparison.OrdinalIgnoreCase))
            errors.Add($"Dataset task class '{dataset.TaskClass}' does not match requested task class '{taskClass}'.");
        if (dataset.Cases.Count == 0) errors.Add("The prompt-regression dataset contains no cases.");
        if (errors.Count > 0) return Result(context, "failed", errors: errors);

        var cases = new List<AiQualificationCase>();
        foreach (var item in dataset.Cases)
        {
            var timer = Stopwatch.StartNew();
            var generated = selected.Generate(new(item.Prompt, provider, model, allowRemote, 120, 512), model);
            timer.Stop();
            var missing = item.RequiredTerms.Where(term => !(generated.Text ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
            cases.Add(Case(item.Id, generated, timer.ElapsedMilliseconds, missing));
        }
        var passed = cases.All(item => item.Status == "passed");
        var datasetDigest = Sha256(File.ReadAllBytes(fullDatasetPath!));
        var evidence = CreateEvidence(context, "prompt-regression", provider, model, taskClass,
            passed ? "passed" : "failed", RegistryDigest(context), datasetDigest, cases,
            passed ? null : "One or more deterministic prompt-regression cases failed.");
        Persist(context, evidence);
        return Result(context, passed ? "passed" : "failed", [evidence], errors: passed ? [] : [evidence.Detail!]);
    }

    public AiQualificationResult Approve(
        string repositoryPath,
        string provider,
        string model,
        string taskClass,
        string reviewer,
        string reason,
        bool confirmed)
    {
        var context = Resolve(repositoryPath, out var errors);
        if (context is null) return Failed(errors);
        if (!confirmed) errors.Add("Approval changes canonical governance; pass --yes after reviewing the evidence.");
        if (string.IsNullOrWhiteSpace(reviewer)) errors.Add("A reviewer is required.");
        if (string.IsNullOrWhiteSpace(reason)) errors.Add("A reason is required.");
        var evidence = ReadEvidence(context)
            .Where(item => item.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase)
                && item.Model.Equals(model, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.TimestampUtc, StringComparer.Ordinal)
            .ToArray();
        var required = new[] { "probe", "benchmark", "prompt-regression" };
        foreach (var kind in required)
        {
            var match = evidence.FirstOrDefault(item => item.Kind == kind
                && (kind == "probe" || item.TaskClass.Equals(taskClass, StringComparison.OrdinalIgnoreCase)));
            if (match is null || match.Status != "passed") errors.Add($"A passed runtime {kind} evidence record is required.");
        }
        if (errors.Count > 0) return Result(context, "failed", evidence, errors: errors);

        var qualifying = evidence.Where(item => item.Status == "passed" && required.Contains(item.Kind)
            && (item.Kind == "probe" || item.TaskClass.Equals(taskClass, StringComparison.OrdinalIgnoreCase))).ToArray();
        var digest = ApprovalDigest(context, qualifying, taskClass);
        var approval = new AiModelApproval(provider.Trim(), model.Trim(), taskClass.Trim(), "approved",
            reviewer.Trim(), reason.Trim(), digest, _clock().ToUniversalTime().ToString("O"));
        var approvals = ReadApprovals(context).Where(item => !(item.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase)
            && item.Model.Equals(model, StringComparison.OrdinalIgnoreCase)
            && item.TaskClass.Equals(taskClass, StringComparison.OrdinalIgnoreCase))).Append(approval).ToArray();
        WriteApprovals(context, approvals);
        return Result(context, "approved", qualifying, approvals, []);
    }

    public AiQualificationResult ExplainRoute(string repositoryPath, string capability, bool allowRemote)
    {
        var context = Resolve(repositoryPath, out var errors);
        if (context is null) return Failed(errors);
        var route = AiGovernanceService.ReadRoutes(context).FirstOrDefault(item => item.Capability.Equals(capability, StringComparison.OrdinalIgnoreCase));
        if (route is null) return Result(context, "failed", errors: [$"No AI route is configured for capability '{capability}'."]);
        var model = ResolveRouteModel(route);
        var approval = ReadApprovals(context).FirstOrDefault(item => item.Provider.Equals(route.Provider, StringComparison.OrdinalIgnoreCase)
            && item.Model.Equals(model, StringComparison.OrdinalIgnoreCase)
            && item.TaskClass.Equals(route.Capability, StringComparison.OrdinalIgnoreCase));
        var reasons = new List<string> { "The route is declared in the canonical AI routing profile." };
        var decision = "selected";
        if (route.AllowRemote && !allowRemote && !route.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            decision = "blocked";
            reasons.Add("Remote transmission requires explicit --allow-remote authorization.");
        }
        var currentEvidence = ReadEvidence(context).Where(item => item.Status == "passed"
            && item.Provider.Equals(route.Provider, StringComparison.OrdinalIgnoreCase)
            && item.Model.Equals(model, StringComparison.OrdinalIgnoreCase)
            && (item.Kind == "probe" || item.TaskClass.Equals(route.Capability, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(item => item.Kind, StringComparer.OrdinalIgnoreCase).Select(group => group.OrderByDescending(item => item.TimestampUtc, StringComparer.Ordinal).First()).ToArray();
        var evidenceCurrent = approval is not null && approval.EvidenceDigest.Equals(
            ApprovalDigest(context, currentEvidence, route.Capability), StringComparison.OrdinalIgnoreCase);
        if (approval?.State != "approved" || !evidenceCurrent)
        {
            decision = "unqualified";
            reasons.Add(approval is null
                ? "No task-class approval matches the selected provider and model."
                : "The task-class approval is stale against current route, dataset, or runtime evidence.");
        }
        else reasons.Add($"Task-class use was approved by {approval.Reviewer}.");
        var explanation = new AiRouteExplanation(route.Capability, route.Provider, model, route.AllowRemote,
            route.Cache, approval?.State ?? "unapproved", decision, reasons);
        return Result(context, decision, approvals: approval is null ? [] : [approval], routes: [explanation], errors: []);
    }

    public AiQualificationResult Status(string repositoryPath)
    {
        var context = Resolve(repositoryPath, out var errors);
        return context is null ? Failed(errors) : Result(context, "reported", ReadEvidence(context), ReadApprovals(context), [], errors);
    }

    private ICisAiProvider? SelectProvider(string provider, bool allowRemote, List<string> errors)
    {
        var matches = _providers.Where(item => item.Name.Equals(provider, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) { errors.Add($"AI provider '{provider}' is not loaded."); return null; }
        if (matches.Length > 1) { errors.Add($"AI provider '{provider}' is registered more than once; resolve the provider conflict."); return null; }
        var status = matches[0].GetStatus();
        if (!status.IsAvailable) { errors.Add(status.Detail ?? $"AI provider '{provider}' is unavailable."); return null; }
        if (!status.IsLocal && !allowRemote) { errors.Add("Remote qualification requires explicit --allow-remote authorization."); return null; }
        return matches[0];
    }

    private AiQualificationEvidence CreateEvidence(CisRepositoryContext context, string kind, string provider,
        string model, string taskClass, string status, string profileDigest, string datasetDigest,
        IReadOnlyList<AiQualificationCase> cases, string? detail)
    {
        var timestamp = _clock().ToUniversalTime().ToString("O");
        var evidenceId = $"AI-{kind.ToUpperInvariant()}-{Sha256($"{timestamp}\n{provider}\n{model}\n{taskClass}\n{datasetDigest}")[..12]}";
        return new(evidenceId, kind, timestamp, provider, model, taskClass, status, profileDigest,
            datasetDigest, cases, detail);
    }

    private static AiQualificationCase Case(string id, CisTextGenerationResult result, long milliseconds, IReadOnlyList<string> missingTerms)
    {
        var passed = result.IsSuccess && missingTerms.Count == 0;
        return new(id, passed ? "passed" : "failed", milliseconds, Sha256(result.Text ?? string.Empty), missingTerms);
    }

    private void Persist(CisRepositoryContext context, AiQualificationEvidence evidence)
    {
        var directory = Path.Combine(context.RepositoryPath, EvidenceRoot.Replace('/', Path.DirectorySeparatorChar), evidence.Kind);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, evidence.EvidenceId + ".json"), JsonSerializer.Serialize(evidence, JsonOptions));
    }

    private static IReadOnlyList<AiQualificationEvidence> ReadEvidence(CisRepositoryContext context)
    {
        var root = Path.Combine(context.RepositoryPath, EvidenceRoot.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(root)) return [];
        return CisPathSafety.EnumerateFiles(root, "*.json")
            .Select(path => { try { return JsonSerializer.Deserialize<AiQualificationEvidence>(File.ReadAllText(path), JsonOptions); } catch { return null; } })
            .Where(item => item is not null).Cast<AiQualificationEvidence>()
            .OrderByDescending(item => item.TimestampUtc, StringComparer.Ordinal).ToArray();
    }

    private static AiRegressionDataset? ReadDataset(string path, List<string> errors)
    {
        try
        {
            if (Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase))
                return ReadMarkdownDataset(path, errors);
            return JsonSerializer.Deserialize<AiRegressionDataset>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            errors.Add($"Prompt-regression dataset is unreadable: {exception.Message}");
            return null;
        }
    }

    private static AiRegressionDataset? ReadMarkdownDataset(string path, List<string> errors)
    {
        var lines = File.ReadAllLines(path);
        var taskClass = lines.Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith("task_class:", StringComparison.OrdinalIgnoreCase))?
            .Split(':', 2)[1].Trim().Trim('"', '\'');
        var cases = new List<AiRegressionCase>();
        foreach (var line in lines)
        {
            if (!line.TrimStart().StartsWith('|')) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length < 3 || cells[0].Equals("Case ID", StringComparison.OrdinalIgnoreCase) || cells.All(cell => cell.All(ch => ch is '-' or ':' or ' '))) continue;
            cases.Add(new(cells[0], cells[1].Replace("<pipe>", "|", StringComparison.OrdinalIgnoreCase),
                cells[2].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));
        }
        if (string.IsNullOrWhiteSpace(taskClass)) errors.Add("Markdown prompt-regression dataset requires a task_class front-matter field.");
        return string.IsNullOrWhiteSpace(taskClass) ? null : new(taskClass, cases);
    }

    private static string? SafeDatasetPath(CisRepositoryContext context, string path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path)) { errors.Add("A repository-relative dataset path is required."); return null; }
        if (!CisPathSafety.TryResolveUnderRoot(context.RepositoryPath, path, out var candidate)
            || CisPathSafety.ContainsReparsePoint(context.RepositoryPath, candidate))
        {
            errors.Add("Dataset path must remain inside the repository and cannot traverse a linked directory."); return null;
        }
        if (!File.Exists(candidate)) { errors.Add($"Dataset does not exist: {path}"); return null; }
        return candidate;
    }

    private static IReadOnlyList<AiModelApproval> ReadApprovals(CisRepositoryContext context)
    {
        var path = Path.Combine(context.DocumentationPath, RegistryPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return [];
        var results = new List<AiModelApproval>();
        foreach (var line in File.ReadLines(path))
        {
            if (!line.TrimStart().StartsWith('|')) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length < 8 || cells[0].Equals("Provider", StringComparison.OrdinalIgnoreCase) || cells.All(cell => cell.All(ch => ch is '-' or ':' or ' '))) continue;
            results.Add(new(cells[0], cells[1], cells[2], cells[3], cells[4], cells[5], cells[6], cells[7]));
        }
        return results;
    }

    private static void WriteApprovals(CisRepositoryContext context, IReadOnlyList<AiModelApproval> approvals)
    {
        var path = Path.Combine(context.DocumentationPath, RegistryPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var builder = new StringBuilder("""
            ---
            title: "AI Model Registry"
            type: ai-model-registry
            status: Active
            owner: "Repository maintainers"
            review_cadence: "on provider, model, prompt, task-class, privacy, or policy change"
            ---

            # AI model registry

            Availability is not approval. Each approved row requires current runtime probe,
            benchmark, and prompt-regression evidence under `.cis/local/ai/qualification/`.

            | Provider | Model | Task class | State | Reviewer | Reason | Evidence digest | Approved UTC |
            |---|---|---|---|---|---|---|---|
            """ + Environment.NewLine);
        foreach (var item in approvals.OrderBy(item => item.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Model, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.TaskClass, StringComparer.OrdinalIgnoreCase))
            builder.AppendLine($"| {Cell(item.Provider)} | {Cell(item.Model)} | {Cell(item.TaskClass)} | {Cell(item.State)} | {Cell(item.Reviewer)} | {Cell(item.Reason)} | {Cell(item.EvidenceDigest)} | {Cell(item.ApprovedUtc)} |");
        File.WriteAllText(path, builder.ToString());
    }

    private string ResolveRouteModel(AiRoute route)
    {
        if (!route.Model.Equals("repository-smallest-local", StringComparison.OrdinalIgnoreCase)) return route.Model;
        return _providers.FirstOrDefault(item => item.Name.Equals(route.Provider, StringComparison.OrdinalIgnoreCase))?
            .GetStatus().Models.OrderBy(item => item.SizeBytes ?? long.MaxValue).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault()?.Name
            ?? route.Model;
    }

    private static string RegistryDigest(CisRepositoryContext context)
    {
        var path = Path.Combine(context.DocumentationPath, RegistryPath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? Sha256(File.ReadAllBytes(path)) : Sha256("missing-registry");
    }

    private static string ApprovalDigest(CisRepositoryContext context, IReadOnlyList<AiQualificationEvidence> evidence, string taskClass)
    {
        var parts = evidence.Select(item => item.EvidenceId).Order(StringComparer.Ordinal).ToList();
        var route = Path.Combine(context.DocumentationPath, "references", "ai-routing-profile.md");
        parts.Add("route:" + (File.Exists(route) ? Sha256(File.ReadAllBytes(route)) : "missing"));
        var dataset = Path.Combine(context.DocumentationPath, "references", "ai-evaluation-datasets", taskClass + ".md");
        if (!File.Exists(dataset)) dataset = Path.ChangeExtension(dataset, ".json");
        parts.Add("dataset:" + (File.Exists(dataset) ? Sha256(File.ReadAllBytes(dataset)) : "missing"));
        return Sha256(string.Join('\n', parts));
    }

    private CisRepositoryContext? Resolve(string path, out List<string> errors)
    {
        var resolution = _resolver.Resolve(path); errors = resolution.Errors.ToList(); return resolution.Context;
    }

    private static AiQualificationResult Result(CisRepositoryContext context, string status,
        IReadOnlyList<AiQualificationEvidence>? evidence = null, IReadOnlyList<AiModelApproval>? approvals = null,
        IReadOnlyList<AiRouteExplanation>? routes = null, IReadOnlyList<string>? errors = null) =>
        new(status, context.RepositoryPath, evidence ?? [], approvals ?? [], routes ?? [], errors ?? []);
    private static AiQualificationResult Failed(IReadOnlyList<string> errors) => new("failed", null, [], [], [], errors);
    private static string Cell(string value) => value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();
    private static string Sha256(string value) => Sha256(Encoding.UTF8.GetBytes(value));
    private static string Sha256(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
