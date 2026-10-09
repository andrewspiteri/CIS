using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Cis.Modules.Plan;

namespace Cis.Modules.Delivery.Tests;

public sealed class BenchmarkEvidenceTests
{
    [Theory]
    [InlineData("current", true)]
    [InlineData("regression", false)]
    [InlineData("incompatible", false)]
    [InlineData("undersized", false)]
    [InlineData("incomplete", false)]
    [InlineData("noisy", false)]
    [InlineData("stale-source", false)]
    [InlineData("stale-report", false)]
    [InlineData("missing-baseline", false)]
    [InlineData("tampered-baseline", false)]
    [InlineData("fractional-count", false)]
    [InlineData("oversized-operations", false)]
    [InlineData("oversized-exit", false)]
    [InlineData("invalid-date", false)]
    public void NativeBenchmarkReportsRequireCompatibleCurrentMeasuredEvidence(string scenario, bool expectedPass)
    {
        using var fixture = new Fixture();
        var candidate = JsonNode.Parse(fixture.Native)!;
        var benchmark = candidate["Benchmarks"]![0]!;
        switch (scenario)
        {
            case "regression": benchmark["Statistics"]!["Mean"] = 100_000; break;
            case "incompatible": candidate["HostEnvironmentInfo"]!["Architecture"] = "Other"; break;
            case "undersized": benchmark["FullName"] = "Different workload"; break;
            case "incomplete": benchmark["Measurements"] = new JsonArray(); break;
            case "noisy": benchmark["Statistics"]!["StandardError"] = 5000; break;
            case "fractional-count": benchmark["Statistics"]!["N"] = 3.5; break;
            case "oversized-operations":
                foreach (var measurement in benchmark["Measurements"]!.AsArray())
                    measurement!["Operations"] = double.MaxValue;
                break;
        }
        File.WriteAllText(Path.Combine(fixture.Root, "candidate.json"), candidate.ToJsonString());
        var reportTime = File.GetLastWriteTimeUtc(Path.Combine(fixture.Root, "candidate.json"));
        fixture.Write("workflow.json", new
        {
            schemaVersion = 2,
            status = "succeeded",
            inputDigest = scenario == "stale-source" ? "old" : "current",
            steps = new[] { new { id = "performance", status = "succeeded", exitCode = 0,
                startedAtUtc = scenario == "stale-report" ? reportTime.AddSeconds(5) : reportTime.AddSeconds(-5), completedAtUtc = reportTime.AddSeconds(10) } }
        });
        if (scenario is "oversized-exit" or "invalid-date")
        {
            var workflow = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Root, "workflow.json")))!;
            if (scenario == "oversized-exit") workflow["steps"]![0]!["exitCode"] = long.MaxValue;
            else workflow["steps"]![0]!["startedAtUtc"] = "invalid date";
            File.WriteAllText(Path.Combine(fixture.Root, "workflow.json"), workflow.ToJsonString());
        }
        if (scenario == "missing-baseline") File.Delete(Path.Combine(fixture.Root, "baseline.json"));
        if (scenario == "tampered-baseline") File.AppendAllText(Path.Combine(fixture.Root, "baseline.json"), " ");
        var errors = BenchmarkEvidenceReview.Review(fixture.Root,
            [fixture.Artifact("candidate.json", "benchmark-candidate"), fixture.Artifact("workflow.json", "performance")], "current");
        Assert.Equal(expectedPass, errors.Count == 0);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"baseline\":{},\"benchmarks\":[]}")]
    public void MissingPolicyFieldsFailClosed(string policy)
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Root, BenchmarkEvidenceReview.PolicyPath), policy);
        Assert.NotEmpty(BenchmarkEvidenceReview.Review(fixture.Root, [], "current"));
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string Parent = Path.Combine(Path.GetTempPath(), "cis-benchmark-tests");
        public string Root { get; } = Path.Combine(Parent, Guid.NewGuid().ToString("N"));
        public string Native { get; } = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/benchmarkdotnet-0.15.8-native.json"));

        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(Root, ".cis"));
            File.WriteAllText(Path.Combine(Root, "baseline.json"), Native);
            Write(BenchmarkEvidenceReview.PolicyPath, new BenchmarkBaselinePolicy(1, Artifact("baseline.json"),
                "Synthetic validation of 500 readings", "One local Release process; no debugger; same native host metadata",
                "Protocol fixture baseline selected for comparison-contract tests", 3, 30, 10,
                [new("Example.Performance.ReadingBenchmarks.ValidateReadings(Records: 500, Instrumented: False)", 20_000, 0),
                 new("Example.Performance.ReadingBenchmarks.ValidateReadings(Records: 500, Instrumented: True)", 20_000, 512)]));
        }

        public void Write(string path, object value) => File.WriteAllText(Path.Combine(Root, path), JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        public EngineeringEvidenceArtifact Artifact(string path, string? selector = null)
            => new(path, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Root, path)))), selector);
        public void Dispose()
        {
            if (!CisPathSafety.IsUnderRoot(Parent, Root)) throw new InvalidOperationException();
            Directory.Delete(Root, true);
        }
    }
}
