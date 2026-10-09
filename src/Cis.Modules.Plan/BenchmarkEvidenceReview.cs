using System.Security.Cryptography;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Plan;

public sealed record BenchmarkBudget(string FullName, double MaximumMeanNanoseconds, double MaximumAllocatedBytes);
public sealed record BenchmarkBaselinePolicy(int SchemaVersion, EngineeringEvidenceArtifact Baseline,
    string Workload, string ResourceLimits, string BaselineDecision, int MinimumSamples,
    double MaximumRegressionPercent, double MaximumStandardErrorPercent, IReadOnlyList<BenchmarkBudget> Benchmarks);

/// <summary>Compares native BenchmarkDotNet reports against an explicitly adopted baseline; never updates it.</summary>
public static class BenchmarkEvidenceReview
{
    public const string PolicyPath = ".cis/performance-policy.json";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly string[] EnvironmentFields = ["BenchmarkDotNetVersion", "OsVersion", "ProcessorName",
        "PhysicalProcessorCount", "PhysicalCoreCount", "LogicalCoreCount", "RuntimeVersion", "Architecture",
        "HasAttachedDebugger", "HasRyuJit", "Configuration", "DotNetCliVersion", "ChronometerFrequency"];

    public static EngineeringPerformanceResult Inspect(CisRepositoryContext context, string candidate, string workflow)
    {
        try
        {
            var identity = CisExecutionIdentity.CaptureIfAdopted(context);
            if (identity is null) return new(1, "missing", ["Adopt engineering defaults before assessing current performance execution."]);
            EngineeringEvidenceArtifact Artifact(string relative, string selector)
            {
                var content = ReadFile(context.RepositoryPath, relative, 16 * 1024 * 1024);
                return new(relative, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(content)), selector);
            }
            var errors = Review(context.RepositoryPath, [Artifact(candidate, "benchmark-candidate"), Artifact(workflow, "performance")], identity);
            return new(1, errors.Count == 0 ? "passed" : "failed", errors);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return new(1, "invalid", [exception.Message]); }
    }

    public static IReadOnlyList<string> Review(string repository, IReadOnlyList<EngineeringEvidenceArtifact> artifacts, string inputDigest)
    {
        try
        {
            var policyFile = ReadFile(repository, PolicyPath, 64 * 1024);
            var policy = JsonSerializer.Deserialize<BenchmarkBaselinePolicy>(policyFile, Options);
            if (policy is not { SchemaVersion: 1, Baseline: not null, Benchmarks.Count: > 0 }
                || policy.Benchmarks.Any(item => item is null || string.IsNullOrWhiteSpace(item.FullName)
                    || !Positive(item.MaximumMeanNanoseconds) || !double.IsFinite(item.MaximumAllocatedBytes) || item.MaximumAllocatedBytes < 0)
                || policy.Benchmarks.Select(item => item.FullName).Distinct(StringComparer.Ordinal).Count() != policy.Benchmarks.Count
                || policy.MinimumSamples < 3 || !Positive(policy.MaximumStandardErrorPercent)
                || !double.IsFinite(policy.MaximumRegressionPercent) || policy.MaximumRegressionPercent is < 0 or > 100
                || !Meaningful(policy.Workload) || !Meaningful(policy.ResourceLimits) || !Meaningful(policy.BaselineDecision))
                return ["Performance policy requires a reviewed baseline, explicit workload/resources, unique budgets and valid sample/error/regression limits."];
            var candidates = artifacts.Where(item => item.Selector == "benchmark-candidate").ToArray();
            if (candidates.Length != 1) return ["Performance evidence requires exactly one native report with selector benchmark-candidate."];
            var candidate = candidates[0];
            using var baseline = JsonDocument.Parse(ReadArtifact(repository, policy.Baseline));
            using var current = JsonDocument.Parse(ReadArtifact(repository, candidate));
            var errors = CompareReports(baseline.RootElement, current.RootElement, policy).ToList();
            if (!HasCurrentExecution(repository, artifacts, candidate, inputDigest))
                errors.Add("Performance report lacks a current successful performance workflow step; an old report or zero-result process is insufficient.");
            return errors;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or FormatException or OverflowException)
        { return [$"Performance evidence is missing or invalid: {exception.Message}"]; }
    }

    private static IReadOnlyList<string> CompareReports(JsonElement baseline, JsonElement current, BenchmarkBaselinePolicy policy)
    {
        var errors = new List<string>();
        var previousEnvironment = baseline.GetProperty("HostEnvironmentInfo");
        var currentEnvironment = current.GetProperty("HostEnvironmentInfo");
        foreach (var field in EnvironmentFields)
            if (!previousEnvironment.TryGetProperty(field, out var previous) || !currentEnvironment.TryGetProperty(field, out var next)
                || !JsonElement.DeepEquals(previous, next))
                errors.Add($"Performance baseline is incompatible: {field} differs or is missing.");
        if (currentEnvironment.GetProperty("HasAttachedDebugger").ValueKind != JsonValueKind.False
            || currentEnvironment.GetProperty("Configuration").GetString() != "RELEASE")
            errors.Add("Performance evidence requires a Release build without an attached debugger.");
        foreach (var budget in policy.Benchmarks)
        {
            var previous = FindBenchmark(baseline, budget.FullName);
            var next = FindBenchmark(current, budget.FullName);
            if (previous is null || next is null) { errors.Add($"Required benchmark is absent or duplicated: {budget.FullName}."); continue; }
            if (previous.Value.GetProperty("DisplayInfo").GetString() != next.Value.GetProperty("DisplayInfo").GetString())
                errors.Add($"Benchmark job, workload or instrumentation parameters changed: {budget.FullName}.");
            var oldMean = ValidateMeasurements(previous.Value, policy, errors);
            var newMean = ValidateMeasurements(next.Value, policy, errors);
            if (newMean > budget.MaximumMeanNanoseconds || newMean > oldMean * (1 + policy.MaximumRegressionPercent / 100))
                errors.Add($"Performance regression or absolute time budget exceeded: {budget.FullName}.");
            var allocation = next.Value.GetProperty("Memory").GetProperty("BytesAllocatedPerOperation").GetDouble();
            if (!double.IsFinite(allocation) || allocation < 0 || allocation > budget.MaximumAllocatedBytes)
                errors.Add($"Allocation budget exceeded or invalid: {budget.FullName}.");
        }
        return errors;
    }

    private static double ValidateMeasurements(JsonElement benchmark, BenchmarkBaselinePolicy policy, ICollection<string> errors)
    {
        var stats = benchmark.GetProperty("Statistics");
        var mean = stats.GetProperty("Mean").GetDouble();
        var standardError = stats.GetProperty("StandardError").GetDouble();
        var count = stats.GetProperty("N").GetInt32();
        var measurements = benchmark.GetProperty("Measurements").EnumerateArray().Count(item =>
            item.GetProperty("IterationMode").GetString() == "Workload" && item.GetProperty("IterationStage").GetString() == "Result"
            && item.GetProperty("Operations").GetInt64() > 0 && Positive(item.GetProperty("Nanoseconds").GetDouble()));
        if (!Positive(mean) || !double.IsFinite(standardError) || standardError < 0
            || count < policy.MinimumSamples || measurements != count
            || standardError / mean * 100 > policy.MaximumStandardErrorPercent)
            errors.Add($"Benchmark measurement is incomplete or too noisy: {benchmark.GetProperty("FullName").GetString()}.");
        return mean;
    }

    private static JsonElement? FindBenchmark(JsonElement report, string fullName)
    {
        var matches = report.GetProperty("Benchmarks").EnumerateArray()
            .Where(item => item.GetProperty("FullName").GetString() == fullName).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool HasCurrentExecution(string repository, IReadOnlyList<EngineeringEvidenceArtifact> artifacts,
        EngineeringEvidenceArtifact candidate, string inputDigest)
    {
        var written = File.GetLastWriteTimeUtc(Path.Combine(repository, candidate.Path));
        foreach (var artifact in artifacts.Where(item => item.Selector == "performance"))
        {
            using var document = JsonDocument.Parse(ReadArtifact(repository, artifact));
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 2 || root.GetProperty("inputDigest").GetString() != inputDigest
                || root.GetProperty("status").GetString() != "succeeded") continue;
            if (root.GetProperty("steps").EnumerateArray().Any(step => step.GetProperty("id").GetString() == "performance"
                && step.GetProperty("status").GetString() == "succeeded" && step.GetProperty("exitCode").GetInt32() == 0
                && written >= step.GetProperty("startedAtUtc").GetDateTime().ToUniversalTime()
                && written <= step.GetProperty("completedAtUtc").GetDateTime().ToUniversalTime())) return true;
        }
        return false;
    }

    private static byte[] ReadArtifact(string repository, EngineeringEvidenceArtifact artifact)
    {
        var content = ReadFile(repository, artifact.Path, 16 * 1024 * 1024);
        var hash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(content));
        if (hash != artifact.Sha256) throw new InvalidDataException("Benchmark artifact changed after selection.");
        return content;
    }

    private static byte[] ReadFile(string repository, string relative, long maximum)
    {
        if (!CisPathSafety.TryResolveUnderRoot(repository, relative, out var path) || CisPathSafety.ContainsReparsePoint(repository, path)
            || !File.Exists(path) || new FileInfo(path).Length > maximum)
            throw new InvalidDataException("Performance evidence must be a bounded regular repository file.");
        var content = File.ReadAllBytes(path);
        if (content.LongLength > maximum) throw new InvalidDataException("Performance evidence grew beyond the file limit.");
        return content;
    }

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
    private static bool Meaningful(string? value) => value?.Trim().Length >= 12;
}
