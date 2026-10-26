using System.Text.Json;
using System.Text.Json.Nodes;
using System.CommandLine;
using Cis.Abstractions;
using Cis.Modules.Repository;
using Cis.Modules.Testing;
using Cis.Modules.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed partial class TestingServiceTests
{
    [Fact]
    public void MutationThreshold_PreservesTheExistingAdapterContextContract()
    {
        Assert.NotNull(typeof(TestResultAdapterContext).GetConstructor(
            [typeof(string), typeof(TestSuiteProfile), typeof(string), typeof(string), typeof(string)]));
        var suite = Suite("mutation", "mutation", "stryker-json", "report.json");
        var context = new TestResultAdapterContext("repository", suite, "report.json", null, null)
        { MutationBreakThreshold = 90 };
        var (repository, _, report, coverage, mutation) = context;
        Assert.Equal("repository", repository);
        Assert.Equal("report.json", report);
        Assert.Null(coverage);
        Assert.Null(mutation);
        var restored = JsonSerializer.Deserialize<TestResultAdapterContext>(JsonSerializer.Serialize(context));
        Assert.Equal(90, restored!.MutationBreakThreshold);
    }

    private static string MutationReport(params string[] statuses) => JsonSerializer.Serialize(new
    {
        schemaVersion = "2",
        thresholds = new { high = 85, low = 80 },
        files = new Dictionary<string, object>
        {
            ["src/example.cs"] = new
            {
                language = "cs",
                source = "return value > 0;",
                mutants = statuses.Select((status, index) => new
                {
                    id = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    mutatorName = "Equality mutation",
                    replacement = "false",
                    location = new { start = new { line = 1, column = 8 }, end = new { line = 1, column = 17 } },
                    status,
                }),
            },
        },
    });

    private static TestSuiteExecution Assess(TestRepository repository, string report, double? threshold = null)
    {
        const string relative = ".cis/local/results/stryker.json";
        repository.Write(relative, report);
        return new StrykerJsonResultAdapter().Read(new(repository.Path, Suite("api-unit", "mutation", "stryker-json", relative),
            Path.Combine(repository.Path, relative), null, null, threshold));
    }

    [Fact]
    public void Mutation_AllTimeoutsHaveZeroConservativeScoreAndCannotPass()
    {
        using var repository = TestRepository.Create();
        var result = Assess(repository, MutationReport("Timeout", "Timeout"), 0);
        Assert.Equal("findings", result.Status);
        Assert.Equal(TestFailureKind.Timeout, result.FailureKind);
        Assert.Equal(0, result.Passed);
        Assert.Equal(0, result.Mutation!.Score);
        Assert.Equal(100, result.Mutation.NativeScore);
        Assert.True(result.Mutation.ThresholdMet);
        Assert.False(result.Mutation.Qualified);
    }

    [Theory]
    [InlineData("RuntimeError")]
    [InlineData("Pending")]
    [InlineData("Unknown")]
    public void Mutation_UnresolvedOutcomesInvalidateEvenAnOtherwisePassingReport(string status)
    {
        using var repository = TestRepository.Create();
        var result = Assess(repository, MutationReport("Killed", status), 90);
        Assert.Equal("invalid-evidence", result.Status);
        Assert.Equal(TestFailureKind.InvalidEvidence, result.FailureKind);
        Assert.Equal(100, result.Mutation!.Score);
        Assert.False(result.Mutation.ValidEvidence);
        Assert.False(result.Mutation.Qualified);
        Assert.Equal(1, result.Mutation.RuntimeErrors + result.Mutation.Pending + result.Mutation.Unknown);
    }

    [Fact]
    public void Mutation_CompileErrorsAndIgnoredAreReportedOutsideTheDenominator()
    {
        using var repository = TestRepository.Create();
        var result = Assess(repository, MutationReport("Killed", "CompileError", "Ignored"), 90);
        Assert.Equal("passed", result.Status);
        Assert.Equal(1, result.Mutation!.Denominator);
        Assert.Equal(1, result.Mutation.CompileErrors);
        Assert.Equal(1, result.Mutation.Ignored);
        Assert.Equal(2, result.Skipped);
    }

    [Theory]
    [InlineData(88.88, true)]
    [InlineData(88.89, false)]
    public void Mutation_ThresholdUsesTheUnroundedConservativeScore(double threshold, bool passed)
    {
        using var repository = TestRepository.Create();
        var result = Assess(repository, MutationReport(Enumerable.Repeat("Killed", 8).Append("Survived").ToArray()), threshold);
        Assert.Equal(passed, result.Mutation!.Qualified);
        Assert.Equal(8 * 100d / 9, result.Mutation.Score);
    }

    [Fact]
    public void Mutation_NoCoverageStillBlocksQualificationEvenWhenThresholdPasses()
    {
        using var repository = TestRepository.Create();
        var result = Assess(repository, MutationReport("Killed", "NoCoverage"), 50);
        Assert.True(result.Mutation!.ThresholdMet);
        Assert.False(result.Mutation.Qualified);
        Assert.Equal(2, result.Mutation.Denominator);
    }

    [Fact]
    public void Mutation_NativeFailureAndEarlyBailNeedNoCustomAssertionOrCaseInventory()
    {
        using var repository = TestRepository.Create();
        var report = JsonNode.Parse(MutationReport("Killed"))!;
        var mutant = report["files"]!["src/example.cs"]!["mutants"]![0]!;
        mutant["statusReason"] = "A product exception failed the native test.";
        mutant["coveredBy"] = new JsonArray("first", "second");
        mutant["killedBy"] = new JsonArray("first");
        mutant["testsCompleted"] = 1;
        var before = report.ToJsonString();
        var result = Assess(repository, before, 100);
        Assert.True(result.Mutation!.Qualified);
        Assert.Empty(result.Cases);
        Assert.Equal(before, File.ReadAllText(Path.Combine(repository.Path, result.Mutation.SourcePath)));
        Assert.Contains(result.Mutation.Limitations, item => item.Contains("complete mutation scope", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Mutation_InvalidExplicitThresholdNeverQualifies(double threshold)
    {
        using var repository = TestRepository.Create();
        var result = Assess(repository, MutationReport("Killed"), threshold);
        Assert.Equal("invalid-evidence", result.Status);
        Assert.Null(result.Mutation!.BreakThreshold);
        Assert.False(result.Mutation.Qualified);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("files")]
    [InlineData("mutants")]
    [InlineData("file-source")]
    [InlineData("id")]
    [InlineData("status")]
    [InlineData("location")]
    [InlineData("threshold-string")]
    [InlineData("threshold-range")]
    public void Mutation_MalformedRequiredShapeIsRejected(string defect)
    {
        using var repository = TestRepository.Create();
        var report = JsonNode.Parse(MutationReport("Killed"))!;
        var file = report["files"]!["src/example.cs"]!;
        var mutant = file["mutants"]![0]!;
        switch (defect)
        {
            case "schema": report["schemaVersion"] = "99"; break;
            case "files": report["files"] = new JsonArray(); break;
            case "mutants": file["mutants"] = new JsonObject(); break;
            case "file-source": file.AsObject().Remove("source"); break;
            case "id": mutant.AsObject().Remove("id"); break;
            case "status": mutant["status"] = 42; break;
            case "location": mutant["location"]!["start"]!["line"] = 0; break;
            case "threshold-string": report["thresholds"]!["high"] = "90"; break;
            case "threshold-range": report["thresholds"]!["high"] = 101; break;
        }
        Assert.Equal("invalid-evidence", Assess(repository, report.ToJsonString(), 90).Status);
    }

    [Fact]
    public void Mutation_DuplicateIdsAreFileLocalAndDuplicateJsonPropertiesAreRejected()
    {
        using var repository = TestRepository.Create();
        var report = JsonNode.Parse(MutationReport("Killed"))!;
        var files = report["files"]!.AsObject();
        files["src/other.cs"] = files["src/example.cs"]!.DeepClone();
        Assert.True(Assess(repository, report.ToJsonString(), 90).Mutation!.Qualified);
        files["src/example.cs"]!["mutants"]!.AsArray().Add(files["src/example.cs"]!["mutants"]![0]!.DeepClone());
        Assert.Equal("invalid-evidence", Assess(repository, report.ToJsonString(), 90).Status);
        Assert.Equal("invalid-evidence", Assess(repository, MutationReport("Killed").Replace("\"schemaVersion\":\"2\"", "\"schemaVersion\":\"2\",\"schemaVersion\":\"2\"", StringComparison.Ordinal), 90).Status);
    }

    [Fact]
    public void Mutation_RejectsReportsOutsideTheRepository()
    {
        using var repository = TestRepository.Create();
        using var outside = TestRepository.Create();
        Assess(outside, MutationReport("Killed"));
        Assert.Throws<InvalidDataException>(() => new StrykerJsonResultAdapter().Read(new(repository.Path,
            Suite("mutation", "mutation", "stryker-json", "-"), Path.Combine(outside.Path, ".cis/local/results/stryker.json"), null, null, 90)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(104857601)]
    public void Mutation_RejectsEmptyOrOversizedFilesBeforeParsing(long bytes)
    {
        using var repository = TestRepository.Create();
        var path = Path.Combine(repository.Path, "report.json");
        using (var file = File.Create(path)) file.SetLength(bytes);
        var error = Assert.Throws<InvalidDataException>(() => new StrykerJsonResultAdapter().Read(new(repository.Path,
            Suite("mutation", "mutation", "stryker-json", "report.json"), path, null, null, 90)));
        Assert.Contains("100 MiB", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutation_RejectsLinkedReportPaths()
    {
        using var repository = TestRepository.Create();
        using var outside = TestRepository.Create();
        var target = Path.Combine(outside.Path, "report.json");
        outside.Write("report.json", MutationReport("Killed"));
        var link = Path.Combine(repository.Path, "linked-report.json");
        File.CreateSymbolicLink(link, target);
        Assert.Throws<InvalidDataException>(() => new StrykerJsonResultAdapter().Read(new(repository.Path,
            Suite("mutation", "mutation", "stryker-json", "linked-report.json"), link, null, null, 90)));
    }

    private static void MutationWorkflow(TestRepository repository, string state = "succeeded", string failureKind = "none")
    {
        repository.Write("docs/cis/references/test-suite-profile.md", Profile("mutation", "stryker-json", ".cis/local/results/stryker.json"));
        repository.Write("docs/cis/workflows/verify.md", "| Step | Command | Depends on | Continue on failure | Timeout seconds | Test suites |\n| mutation | dotnet --version | - | no | 30 | api-unit |\n");
        var definition = new WorkflowService(new CisRepositoryContextResolver()).Describe(repository.Path, "verify").Workflow!;
        var began = DateTimeOffset.UtcNow.AddMinutes(-1);
        repository.Write(".cis/local/workflows/run/state.json", JsonSerializer.Serialize(new WorkflowRunState(2, "run", "verify", definition.Digest,
            state, began.ToString("O"), began.AddSeconds(30).ToString("O"),
            [new("mutation", state, state == "failed" ? 1 : 0, began.ToString("O"), began.AddSeconds(30).ToString("O"), 1, "mutation.log", state == "failed" ? "Native process failed." : null, FailureKind: failureKind)], "inputs"), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void Reconcile_MutationFindingsCannotBecomePassedWithFindings()
    {
        using var repository = TestRepository.Create();
        MutationWorkflow(repository);
        Assess(repository, MutationReport("Killed", "Survived"), 90);
        var result = Service().Reconcile(repository.Path, "run");
        Assert.Equal("failed", result.Manifest!.Status);
        Assert.Equal(4, result.ExitCode);
    }

    [Theory]
    [InlineData("product", TestFailureKind.Product)]
    [InlineData("infrastructure", TestFailureKind.Infrastructure)]
    [InlineData("timeout", TestFailureKind.Timeout)]
    public void Reconcile_FailedMutationKeepsFreshReportAndOriginalFailureKind(string kind, TestFailureKind expected)
    {
        using var repository = TestRepository.Create();
        MutationWorkflow(repository, "failed", kind);
        Assess(repository, MutationReport("Killed"));
        var result = Service().Reconcile(repository.Path, "run");
        var suite = Assert.Single(result.Manifest!.Suites);
        Assert.Equal("failed", suite.Status);
        Assert.Equal(expected, suite.FailureKind);
        Assert.Equal(1, suite.Mutation!.Killed);
        Assert.Equal(4, result.ExitCode);
    }

    [Fact]
    public void Reconcile_FailedMutationRejectsAStaleReport()
    {
        using var repository = TestRepository.Create();
        MutationWorkflow(repository, "failed", "product");
        Assess(repository, MutationReport("Killed"));
        File.SetLastWriteTimeUtc(Path.Combine(repository.Path, ".cis/local/results/stryker.json"), DateTime.UtcNow.AddHours(-1));
        var suite = Assert.Single(Service().Reconcile(repository.Path, "run").Manifest!.Suites);
        Assert.Equal("failed", suite.Status);
        Assert.Null(suite.Mutation);
        Assert.Contains(suite.Diagnostics, item => item.Contains("predates", StringComparison.Ordinal));
    }

    [Fact]
    public void Reconcile_RecomputesHashBoundSidecarAndRejectsChangedReportBytes()
    {
        using var repository = TestRepository.Create();
        MutationWorkflow(repository);
        var assessment = Assess(repository, MutationReport("Killed", "Killed", "Survived"), 60);
        var json = JsonSerializer.SerializeToNode(assessment, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        json["status"] = "failed"; json["mutation"]!["score"] = 0; json["mutation"]!["qualified"] = false;
        repository.Write(".cis/local/results/stryker.json.assessment.json", json.ToJsonString());
        var result = Service().Reconcile(repository.Path, "run");
        Assert.Equal(0, result.ExitCode);
        var suite = Assert.Single(result.Manifest!.Suites);
        Assert.True(suite.Mutation!.Qualified);
        Assert.Equal(200d / 3, suite.Mutation.Score);
        Assert.Contains(suite.Artifacts, artifact => artifact.Kind == "mutation-assessment");
        repository.Write(".cis/local/results/stryker.json", MutationReport("Killed"));
        Assert.Equal("invalid-evidence", Assert.Single(Service().Reconcile(repository.Path, "run").Manifest!.Suites).Status);
    }

    [Fact]
    public void Reconcile_RejectsOversizedMutationAssessment()
    {
        using var repository = TestRepository.Create();
        MutationWorkflow(repository);
        Assess(repository, MutationReport("Killed"));
        repository.Write(".cis/local/results/stryker.json.assessment.json", new string(' ', 1024 * 1024 + 1));
        Assert.Equal("invalid-evidence", Assert.Single(Service().Reconcile(repository.Path, "run").Manifest!.Suites).Status);
    }

    [Fact]
    public void MutationCommand_RegistersDispatchesAndLeavesRawReportUnchanged()
    {
        using var repository = TestRepository.Create();
        var native = MutationReport("Killed", "Survived");
        Assess(repository, native);
        var services = new ServiceCollection();
        var module = new TestingModule(); module.RegisterServices(services);
        Assert.Contains(services, registration => registration.ServiceType == typeof(ICisTestResultAdapter)
            && registration.ImplementationType == typeof(StrykerJsonResultAdapter));
        var registry = new MutationCommandRegistry(); module.RegisterCommands(registry, new MutationCommandServices(Service()));
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var original = Console.Out;
        int exit;
        try
        {
            Console.SetOut(output);
            exit = registry.Root.Parse(["test", "mutation", "assess", "--report", ".cis/local/results/stryker.json",
                "--repo", repository.Path, "--break", "90", "--format", "json"]).Invoke();
        }
        finally { Console.SetOut(original); }
        Assert.Equal(4, exit);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("mutation").GetProperty("qualified").GetBoolean());
        Assert.Equal(50, json.RootElement.GetProperty("mutation").GetProperty("score").GetDouble());
        Assert.Equal(64, json.RootElement.GetProperty("mutation").GetProperty("reportSha256").GetString()!.Length);
        Assert.Equal(native, File.ReadAllText(Path.Combine(repository.Path, ".cis/local/results/stryker.json")));
    }

    private sealed class MutationCommandRegistry : ICisCommandRegistry
    {
        internal RootCommand Root { get; } = new();
        public void Add(Command command) => Root.Subcommands.Add(command);
    }

    private sealed class MutationCommandServices(TestingService service) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(TestingService) ? service : null;
    }
}
