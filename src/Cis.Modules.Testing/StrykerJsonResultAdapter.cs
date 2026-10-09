using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed class StrykerJsonResultAdapter : ICisTestResultAdapter
{
    public string Format => "stryker-json";

    public TestSuiteExecution Read(TestResultAdapterContext context)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(context.ResultPath));
        var statuses = new List<string>();
        var root = document.RootElement;
        if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Object)
            foreach (var file in files.EnumerateObject())
                if (file.Value.TryGetProperty("mutants", out var mutants) && mutants.ValueKind == JsonValueKind.Array)
                    foreach (var mutant in mutants.EnumerateArray())
                        statuses.Add(mutant.TryGetProperty("status", out var state) && state.ValueKind == JsonValueKind.String
                            ? state.GetString()! : "Unknown");
        var killed = statuses.Count(item => item.Equals("Killed", StringComparison.OrdinalIgnoreCase));
        var survived = statuses.Count(item => item.Equals("Survived", StringComparison.OrdinalIgnoreCase));
        var timedOut = statuses.Count(item => item.Equals("Timeout", StringComparison.OrdinalIgnoreCase));
        var noCoverage = statuses.Count(item => item.Equals("NoCoverage", StringComparison.OrdinalIgnoreCase));
        var denominator = killed + survived + timedOut + noCoverage;
        var score = denominator == 0 ? 0 : (killed + timedOut) * 100d / denominator;
        var high = Threshold(root, "thresholds", "high");
        var low = Threshold(root, "thresholds", "low");
        var @break = Threshold(root, "cisGate", "break");
        var mutation = new TestMutationSummary(score, killed, survived, timedOut, noCoverage,
            high, low, @break, TestResultEvidence.Relative(context.RepositoryPath, context.ResultPath));
        var gatePassed = @break is not null && score >= @break && noCoverage == 0;
        var invalid = denominator == 0 || statuses.Any(item => !KnownStatuses.Contains(item));
        var status = invalid ? "invalid-evidence"
            : gatePassed || survived == 0 && noCoverage == 0 ? "passed"
            : "findings";
        return new TestSuiteExecution(context.Suite.Id, context.Suite.Layer, context.Suite.Framework, status,
            invalid ? TestFailureKind.InvalidEvidence : TestFailureKind.None,
            statuses.Count, killed + timedOut, survived, noCoverage, 0, [], null, mutation,
            [TestResultEvidence.Artifact(context.RepositoryPath, "mutation-result", context.ResultPath)],
            invalid ? ["Mutation result has no assessed mutants or contains an unknown disposition."] : []);
    }

    private static double? Threshold(JsonElement root, string container, string name)
    {
        if (!root.TryGetProperty(container, out var thresholds) || thresholds.ValueKind != JsonValueKind.Object
            || !thresholds.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;
    }

    private static readonly HashSet<string> KnownStatuses = new(StringComparer.OrdinalIgnoreCase)
        { "Killed", "Survived", "Timeout", "NoCoverage", "Ignored", "CompileError", "RuntimeError" };
}
