using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed partial class TestingServiceTests
{
    private static JsonObject RunAssessment(TestRepository repository, string kind = "qualification", params string[] outcomes)
    {
        repository.Write("src/example.cs", "return value > 0;");
        repository.Write("mutation-profile.json", "{\"schemaVersion\":1}");
        MutationWorkflow(repository);
        var result = Assess(repository, MutationReport(outcomes.Length == 0 ? ["Killed"] : outcomes), 90);
        var json = JsonSerializer.SerializeToNode(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        json["cisRun"] = JsonSerializer.SerializeToNode(new
        {
            schemaVersion = 1,
            runId = "test-run",
            profileId = "sample",
            kind,
            partial = kind != "qualification",
            qualified = kind == "qualification",
            sourceUnchanged = true,
            scopeMatched = true,
            executionStatus = "exited",
            nativeExitCode = 0,
            healthCheckConfigured = false,
            sourceIdentity = new
            {
                unchanged = true,
                inputs = new[] { "src/example.cs", "mutation-profile.json" }.Select(path => new
                {
                    path,
                    sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(repository.Path, path)))))
                }).ToArray(),
            },
        });
        repository.Write(".cis/local/testing/mutation/sample/latest-run.json", "{\"schemaVersion\":1,\"profileId\":\"sample\",\"runId\":\"test-run\",\"status\":\"qualified\",\"exitCode\":0,\"qualified\":true}");
        return json;
    }

    private static TestSuiteExecution ReconcileRunAssessment(TestRepository repository, JsonObject assessment)
    {
        repository.Write(".cis/local/results/stryker.json.assessment.json", assessment.ToJsonString());
        return Assert.Single(Service().Reconcile(repository.Path, "run").Manifest!.Suites);
    }

    [Theory]
    [InlineData("canary")]
    [InlineData("diagnostic")]
    public void Reconcile_PartialMutationCountsRemainVisibleWithoutQualifying(string kind)
    {
        using var repository = TestRepository.Create();
        var suite = ReconcileRunAssessment(repository, RunAssessment(repository, kind));
        Assert.Equal("partial", suite.Status);
        Assert.Equal(100, suite.Mutation!.Score);
        Assert.Equal(1, suite.Mutation.Killed);
        Assert.False(suite.Mutation.Qualified);
    }

    [Fact]
    public void Reconcile_MissingRepresentativeProofPreservesNativeValidityAndThresholdWithoutQualification()
    {
        using var repository = TestRepository.Create();
        var suite = ReconcileRunAssessment(repository, RunAssessment(repository));
        Assert.Equal("pilot-required", suite.Status);
        Assert.True(suite.Mutation!.ValidEvidence);
        Assert.True(suite.Mutation.ThresholdMet);
        Assert.False(suite.Mutation.Qualified);
        Assert.Equal(TestFailureKind.MissingPrerequisite, suite.FailureKind);
        var belowGate = ReconcileRunAssessment(repository, RunAssessment(repository, "qualification", "Killed", "Survived"));
        Assert.Equal(50, belowGate.Mutation!.Score);
        Assert.False(belowGate.Mutation.Qualified);
    }

    [Theory]
    [InlineData("src/example.cs")]
    [InlineData("mutation-profile.json")]
    public void Reconcile_ChangedMutationInputsKeepCountsButInvalidateQualification(string changedPath)
    {
        using var repository = TestRepository.Create();
        var assessment = RunAssessment(repository);
        repository.Write(changedPath, "changed input");
        var suite = ReconcileRunAssessment(repository, assessment);
        Assert.Equal("stale", suite.Status);
        Assert.Equal(TestFailureKind.InvalidEvidence, suite.FailureKind);
        Assert.Equal(1, suite.Mutation!.Killed);
        Assert.False(suite.Mutation.Qualified);
    }

    [Theory]
    [InlineData("timeout", 0)]
    [InlineData("exited", 7)]
    public void Reconcile_NativeExecutionFailureCannotBeClearedByACompleteKillScore(string status, int exit)
    {
        using var repository = TestRepository.Create();
        var assessment = RunAssessment(repository);
        assessment["cisRun"]!["executionStatus"] = status;
        assessment["cisRun"]!["nativeExitCode"] = exit;
        var suite = ReconcileRunAssessment(repository, assessment);
        Assert.Equal("failed", suite.Status);
        Assert.Equal(100, suite.Mutation!.Score);
        Assert.False(suite.Mutation.Qualified);
    }

    [Fact]
    public void Reconcile_FailedConfiguredHealthCheckKeepsNativeCountsAndTheHold()
    {
        using var repository = TestRepository.Create();
        var assessment = RunAssessment(repository);
        assessment["cisRun"]!["healthCheckConfigured"] = true;
        assessment["cisRun"]!["healthCheckExecution"] = new JsonObject { ["status"] = "exited", ["exitCode"] = 7 };
        var suite = ReconcileRunAssessment(repository, assessment);
        Assert.Equal("failed", suite.Status);
        Assert.Equal(1, suite.Mutation!.Killed);
        Assert.False(suite.Mutation.Qualified);
    }

    [Theory]
    [InlineData("running")]
    [InlineData("canary-failed")]
    [InlineData("diagnostic-completed")]
    public void Reconcile_LaterAttemptPreventsAnEarlierPassFromLookingCurrent(string status)
    {
        using var repository = TestRepository.Create();
        var assessment = RunAssessment(repository);
        repository.Write(".cis/local/testing/mutation/sample/latest-run.json", JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            profileId = "sample",
            runId = "later-run",
            status,
            exitCode = status == "diagnostic-completed" ? 0 : 4,
            qualified = false,
        }));
        var suite = ReconcileRunAssessment(repository, assessment);
        Assert.Equal("stale", suite.Status);
        Assert.Equal(100, suite.Mutation!.Score);
        Assert.False(suite.Mutation.Qualified);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("path")]
    [InlineData("duplicate")]
    [InlineData("digest")]
    [InlineData("partial")]
    public void Reconcile_MalformedRunContextFailsClosed(string defect)
    {
        using var repository = TestRepository.Create();
        var assessment = RunAssessment(repository);
        var run = assessment["cisRun"]!;
        var pins = run["sourceIdentity"]!["inputs"]!.AsArray();
        switch (defect)
        {
            case "schema": run["schemaVersion"] = "1"; break;
            case "path": pins[0]!["path"] = "../outside.cs"; break;
            case "duplicate": pins.Add(pins[0]!.DeepClone()); break;
            case "digest": pins[0]!["sha256"] = "invalid"; break;
            case "partial": run["partial"] = true; break;
        }
        var suite = ReconcileRunAssessment(repository, assessment);
        Assert.Equal("invalid-evidence", suite.Status);
        Assert.False(suite.Mutation?.Qualified == true);
    }

    [Theory]
    [InlineData("agent")]
    [InlineData("human")]
    public void NativeRunOutputSeparatesValidThresholdResultsFromReadinessAndQualification(string format)
    {
        // Existing synthetic protocol execution checks CLI projection only; it
        // supplies no real mutant activation or governed representative proof.
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var native = fixture.Profile.MutationCommand!.Select(value => value.Replace("{sourceFile}", "src/A.cs", StringComparison.Ordinal)
            .Replace("{testFilter}", fixture.Profile.FullTestFilter, StringComparison.Ordinal)).ToArray();
        var profile = new MutationRunProfile
        {
            SchemaVersion = 1,
            Id = "output",
            Command = native,
            CanaryCommand = native,
            SourceFiles = ["src/A.cs"],
            ConfigFiles = ["config.json"],
            ReportPath = fixture.Profile.MutationReportPath,
            BreakThreshold = 90,
            TimeoutSeconds = 15,
            MinFreeBytes = 0,
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        fixture.Write("mutation-output.json", JsonSerializer.Serialize(profile, options));
        var registry = new MutationCommandRegistry();
        new TestingModule().RegisterCommands(registry, new MutationCommandServices(Service()));
        using var output = new StringWriter();
        var original = Console.Out;
        try
        {
            Console.SetOut(output);
            var exit = registry.Root.Parse(["test", "mutation", "run", "--repo", fixture.Path,
                "--profile", "mutation-output.json", "--format", format]).Invoke();
            Assert.True(exit == 0, output.ToString());
            var rendered = output.ToString();
            if (format == "agent")
            {
                Assert.Contains("validEvidence=true;thresholdMet=true;qualified=false", rendered, StringComparison.Ordinal);
                Assert.Contains("pilotStatus=legacy-unaccounted;pilotReady=false;controllerAssessed=false", rendered, StringComparison.Ordinal);
                Assert.Contains("pilotNextAction=", rendered, StringComparison.Ordinal);
                Assert.Contains("remainingMilliseconds=unknown;restorationMilliseconds=unknown;usedAttempts=unknown;remainingAttempts=unknown", rendered, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("Native evidence valid: true; threshold met: true; qualified: false.", rendered, StringComparison.Ordinal);
                Assert.Contains("Representative pilot: legacy-unaccounted; controls ready: false; controller assessed: false.", rendered, StringComparison.Ordinal);
                Assert.Contains("Pilot next action:", rendered, StringComparison.Ordinal);
                Assert.Contains("Work capacity: unknown; no governed allowance observation.", rendered, StringComparison.Ordinal);
            }
            output.GetStringBuilder().Clear();
            fixture.Write("mutation-output.json", JsonSerializer.Serialize(profile with { CorrectnessPilotProfile = "missing;pilot.json" }, options));
            var held = registry.Root.Parse(["test", "mutation", "run", "--repo", fixture.Path,
                "--profile", "mutation-output.json", "--format", format]).Invoke();
            Assert.Equal(4, held);
            if (format == "agent")
            {
                var action = Assert.Single(output.ToString().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
                    line => line.StartsWith("pilotNextAction=", StringComparison.Ordinal));
                Assert.DoesNotContain(';', action);
                Assert.Contains("missing,pilot.json", action, StringComparison.Ordinal);
            }
            else Assert.Contains("missing;pilot.json", output.ToString(), StringComparison.Ordinal);
            Assert.Single(fixture.Calls());
        }
        finally { Console.SetOut(original); }
    }

    [Theory]
    [InlineData("json", "invalid-profile")]
    [InlineData("bad-format", null)]
    public void MutationRunCommandValidatesInputsWithoutLaunchingATarget(string format, string? status)
    {
        using var repository = TestRepository.Create();
        var registry = new MutationCommandRegistry();
        new TestingModule().RegisterCommands(registry, new MutationCommandServices(Service()));
        using var output = new StringWriter();
        var original = Console.Out;
        int exit;
        try
        {
            Console.SetOut(output);
            exit = registry.Root.Parse(["test", "mutation", "run", "--repo", repository.Path,
                "--profile", "missing.json", "--format", format]).Invoke();
        }
        finally { Console.SetOut(original); }
        Assert.Equal(2, exit);
        if (status is not null)
            Assert.Equal(status, JsonNode.Parse(output.ToString())!["status"]!.GetValue<string>());
        Assert.False(Directory.Exists(Path.Combine(repository.Path, ".cis/local/testing/mutation")));
    }
}
