using System.Security.Cryptography;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class IncrementalRefinementTests
{
    [Fact]
    public void CompletedExactNativeRerunAddsSeparateCreditWithoutExecutingOrRewritingReports()
    {
        using var fixture = new Fixture();
        var before = fixture.Reports().ToDictionary(path => path, File.ReadAllText);
        var result = fixture.Refine();
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        Assert.Equal("refined", result.Status);
        Assert.Equal(1, result.Raw!.Killed);
        Assert.Equal(1, result.Raw.TimedOut);
        Assert.Equal(50, result.Raw.Score);
        Assert.Equal(2, result.Effective!.Killed);
        Assert.Equal(0, result.Effective.TimedOut);
        Assert.Equal(100, result.Effective.Score);
        Assert.Equal(result.Raw.Denominator, result.Effective.Denominator);
        Assert.True(result.ThresholdMet);
        Assert.False(result.Qualified);
        Assert.Single(result.Credits);
        Assert.Equal("src/B.cs", result.Credits[0].SourceFile);
        Assert.Equal("native-B", Assert.Single(result.Credits[0].KillingTestIds));
        Assert.True(File.Exists(fixture.Inner.Full(result.SidecarPath!)));
        foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllText(pair.Key));
        Assert.Empty(fixture.Inner.Calls());
        Assert.Equal(50, fixture.Service.Plan(new(fixture.Inner.Path, "profile.json")).CompleteScopeScore);
    }

    [Fact]
    public void NarrowRerunRequiresTheOriginalUnitRefreshAndPublishesNoPrematureCredit()
    {
        using var fixture = new Fixture(refreshOriginal: false);
        var pending = fixture.Refine();
        Assert.Equal(3, pending.ExitCode);
        Assert.Equal("needs-baseline", pending.Status);
        Assert.True(pending.RequiresBaseline);
        Assert.Empty(pending.Credits);
        Assert.Null(pending.SidecarPath);
        Assert.Equal(pending.Raw, pending.Effective);
        fixture.RefreshOriginal();
        Assert.Equal(0, fixture.Refine().ExitCode);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Fact]
    public void IdenticalFullFilterUsesItsHealthyNativePostflightWithoutAnotherUnitRefresh()
    {
        using var fixture = new Fixture(refreshOriginal: false, narrow: false);
        var result = fixture.Refine();
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        Assert.False(result.RequiresBaseline);
        Assert.Single(result.Credits);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Fact]
    public void AVerifiedCompatibleProducerChainRetainsTheOriginalNativeHelperIdentity()
    {
        using var fixture = new Fixture();
        fixture.RevalidatePreviousProducer();
        var result = fixture.Refine();
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        Assert.Single(result.Credits);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Fact]
    public void AFailedOriginalUnitExecutionCannotBeUsedAsTheRefreshedBaseline()
    {
        using var fixture = new Fixture();
        fixture.FailOriginalUnitExecution();
        Assert.Equal(4, fixture.Refine().ExitCode);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Fact]
    public void ACompletedNativeRerunMayUseAShorterOuterInvocationGuard()
    {
        using var fixture = new Fixture(rerunGuard: 5);
        var before = fixture.Reports().ToDictionary(path => path, File.ReadAllText);
        var result = fixture.Refine();
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        Assert.Single(result.Credits);
        Assert.Equal(50, result.Raw!.Score);
        Assert.Equal(100, result.Effective!.Score);
        foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllText(pair.Key));
        Assert.Empty(fixture.Inner.Calls());
    }

    [Fact]
    public void ALongerOuterInvocationGuardCannotAuthorizeRefinementEvenAfterNativeCompletion()
    {
        using var fixture = new Fixture(rerunGuard: 11);
        var result = fixture.Refine();
        Assert.Equal(4, result.ExitCode);
        Assert.Contains(result.Diagnostics, item => item.Contains("outer invocation guard", StringComparison.Ordinal));
        Assert.Empty(result.Credits);
        Assert.Null(result.SidecarPath);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Fact]
    public void AShorterGuardThatActuallyTimedOutCannotTurnReportedKillsIntoRefinementCredit()
    {
        using var fixture = new Fixture(rerunGuard: 5);
        fixture.ChangeRerunStage(stage => stage with { Execution = stage.Execution! with { Status = "timeout", ExitCode = null } });
        var result = fixture.Refine();
        Assert.Equal(4, result.ExitCode);
        Assert.Empty(result.Credits);
        Assert.Null(result.SidecarPath);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Theory]
    [InlineData(15000)]
    [InlineData(30000)]
    [InlineData(60000)]
    public void ABoundedNativeRerunBudgetPreservesRawEvidenceAndRequiresACompletedKill(int budget)
    {
        using var fixture = new Fixture(rerunAdditionalTimeout: budget);
        var before = fixture.Reports().ToDictionary(path => path, File.ReadAllText);
        var template = File.ReadAllText(fixture.Inner.Full("stryker.json"));
        var result = fixture.Refine();
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        Assert.Single(result.Credits);
        Assert.Equal(50, result.Raw!.Score);
        Assert.Equal(100, result.Effective!.Score);
        Assert.Equal(result.Raw.Denominator, result.Effective.Denominator);
        Assert.False(result.Qualified);
        foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllText(pair.Key));
        Assert.Equal(template, File.ReadAllText(fixture.Inner.Full("stryker.json")));
        Assert.Empty(fixture.Inner.Calls());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(14999)]
    [InlineData(60001)]
    [InlineData(int.MaxValue)]
    public void InvalidNativeRerunBudgetsCannotAuthorizeCredit(int budget)
    {
        using var fixture = new Fixture(rerunAdditionalTimeout: 30000);
        fixture.ChangeRerun(fixture.Rerun with { RerunAdditionalTimeoutMilliseconds = budget });
        var result = fixture.Refine();
        Assert.Equal(4, result.ExitCode);
        Assert.Empty(result.Credits);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Theory]
    [InlineData(30)]
    [InlineData(15)]
    public void TheNativeRerunBudgetMustRemainBelowItsOuterGuard(int seconds)
    {
        using var fixture = new Fixture(rerunAdditionalTimeout: 30000);
        fixture.ChangeRerun(fixture.Rerun with { TimeoutSeconds = seconds });
        Assert.Equal(4, fixture.Refine().ExitCode);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("\"15000\"")]
    [InlineData("15000.5")]
    public void TheNativeTemplateBudgetMustBeAnExplicitPositiveInteger(string baseline)
    {
        using var fixture = new Fixture(rerunAdditionalTimeout: 30000);
        fixture.Inner.Write("stryker.json", "{\"stryker-config\":{\"additional-timeout\":" + baseline + "}}");
        Assert.Equal(4, fixture.Refine().ExitCode);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Fact]
    public void ANativeRerunBudgetRequiresTheDeclaredTemplate()
    {
        using var fixture = new Fixture(rerunAdditionalTimeout: 30000);
        fixture.ChangeRerun(fixture.Rerun with { StrykerConfigFile = null });
        Assert.Equal(4, fixture.Refine().ExitCode);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Fact]
    public void TheOriginalProfileCannotUseTheDiagnosticNativeRerunBudget()
    {
        using var fixture = new Fixture(rerunAdditionalTimeout: 30000, budgetOnOriginal: true);
        var result = fixture.Refine();
        Assert.Equal(4, result.ExitCode);
        Assert.Contains(result.Diagnostics, item => item.Contains("original profile", StringComparison.Ordinal));
        var verification = fixture.Service.Verify(new(fixture.Inner.Path, "profile.json"));
        Assert.Equal(4, verification.ExitCode);
        Assert.Contains(verification.Diagnostics, item => item.Contains("original full-file evidence", StringComparison.Ordinal));
        Assert.Empty(fixture.Inner.Calls());
    }

    [Theory]
    [InlineData("native-timeout")]
    [InlineData("reported-timeout")]
    [InlineData("changed-budget")]
    [InlineData("command")]
    public void ADeclaredBudgetCannotReplaceVerifiedCurrentNativeExecution(string fault)
    {
        using var fixture = new Fixture(rerunAdditionalTimeout: 30000);
        switch (fault)
        {
            case "native-timeout":
                fixture.ChangeRerunStage(stage => stage with { Execution = stage.Execution! with { Status = "timeout", ExitCode = null } });
                break;
            case "reported-timeout": fixture.ChangeRerunReport(report => report["files"]!["src/B.cs"]!["mutants"]![0]!["status"] = "Timeout"); break;
            case "changed-budget": fixture.ChangeRerun(fixture.Rerun with { RerunAdditionalTimeoutMilliseconds = 45000 }); break;
            case "command": fixture.ChangeRerunStage(stage => stage with { Execution = stage.Execution! with { Command = ["unrelated"] } }); break;
        }
        var result = fixture.Refine();
        Assert.Equal(4, result.ExitCode);
        Assert.Empty(result.Credits);
        Assert.Null(result.SidecarPath);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Theory]
    [InlineData("report")]
    [InlineData("fingerprint")]
    [InlineData("helper")]
    [InlineData("artifact")]
    [InlineData("reason")]
    [InlineData("length")]
    public void InventedOrMismatchedProducerHistoryCannotAuthorizeOldNativeReceipts(string fault)
    {
        using var fixture = new Fixture();
        fixture.RevalidatePreviousProducer();
        fixture.ChangeRerunStage(stage =>
        {
            var proof = stage.ProducerRevalidations[0];
            return stage with { ProducerRevalidations = fault switch
            {
                "report" => [proof with { ReportSha256 = new('a', 64) }],
                "fingerprint" => [proof with { PreviousFingerprint = new('a', 64) }],
                "helper" => [proof with { PreviousHelperSha256 = new('a', 64) }],
                "artifact" => [proof with { PreviousHelperAssemblyPath = fixture.Inner.Full("tool.txt") }],
                "reason" => [proof with { Reason = "" }],
                _ => Enumerable.Repeat(proof, 9).ToArray(),
            } };
        });
        Assert.Equal(4, fixture.Refine().ExitCode);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Theory]
    [InlineData("source")]
    [InlineData("test")]
    [InlineData("configuration")]
    [InlineData("tool")]
    [InlineData("environment")]
    [InlineData("command")]
    [InlineData("scope")]
    [InlineData("hash")]
    [InlineData("recovery")]
    [InlineData("postflight")]
    [InlineData("native")]
    [InlineData("active")]
    [InlineData("incomplete")]
    [InlineData("native-command")]
    [InlineData("helper")]
    [InlineData("postflight-cases")]
    [InlineData("range")]
    [InlineData("earlier-rerun")]
    public void ChangedOrUnhealthyInputsAndStagesCannotRefine(string fault)
    {
        using var fixture = new Fixture();
        switch (fault)
        {
            case "source": fixture.Inner.Write("src/B.cs", "class Changed { }"); break;
            case "test": fixture.Inner.Write("tests/BTests.cs", "class ChangedTest { }"); break;
            case "configuration": fixture.Inner.Write("config.json", "{\"changed\":true}"); break;
            case "tool": File.AppendAllText(fixture.Inner.Full("tool.txt"), "changed"); break;
            case "environment": fixture.ChangeRerun(fixture.Rerun with { Environment = new() { ["CIS_FIXTURE_INPUT"] = "changed" } }); break;
            case "command": fixture.ChangeRerun(fixture.Rerun with { MutationCommand = [.. fixture.Rerun.MutationCommand!, "--disable-bail"] }); break;
            case "scope": fixture.ChangeRerun(fixture.Rerun with { SourceFiles = ["src/Shared.cs"] }); break;
            case "hash": File.AppendAllText(fixture.ReportPath("rerun", "src/B.cs"), " "); break;
            case "recovery": fixture.ChangeRerunStage(stage => stage with { RecoveryExecution = stage.RecoveryExecution! with { ExitCode = 1 } }); break;
            case "postflight": fixture.ChangeRerunStage(stage => stage with { PostflightExecution = stage.PostflightExecution! with { Error = "cleanup failed" } }); break;
            case "native": fixture.ChangeRerunStage(stage => stage with { Execution = stage.Execution! with { Status = "timeout" } }); break;
            case "active": fixture.ChangeRerunState(state => state with { RecoveryRequired = true }); break;
            case "incomplete": fixture.ChangeRerunStage(stage => stage with { Completed = false }); break;
            case "native-command": fixture.ChangeRerunStage(stage => stage with { Execution = stage.Execution! with { Command = ["unrelated"] } }); break;
            case "helper": fixture.ChangeRerunStage(stage => stage with { RecoveryExecution = stage.RecoveryExecution! with { CisToolAssemblySha256 = "changed" } }); break;
            case "postflight-cases": fixture.ChangeRerunStage(stage => stage with { PostflightPassed = 0 }); break;
            case "range": fixture.ChangeRerun(fixture.Rerun with { MutationCommand = [.. fixture.Original.MutationCommand!, "--mutate", "{sourceFile}{0..99999}"] }); break;
            case "earlier-rerun": fixture.ChangeRerunStage(stage => stage with { FinishedUtc = DateTimeOffset.UtcNow.AddDays(-1) }); break;
        }
        Assert.Equal(4, fixture.Refine().ExitCode);
        Assert.False(Directory.Exists(fixture.Inner.Full(IncrementalTestService.LocalRoot + "/sample/refinements")));
        Assert.Empty(fixture.Inner.Calls());
    }

    [Theory]
    [InlineData("id")]
    [InlineData("operator")]
    [InlineData("replacement")]
    [InlineData("location")]
    [InlineData("report-source")]
    [InlineData("missing-killed-by")]
    [InlineData("unknown-test")]
    [InlineData("test-definition")]
    [InlineData("test-source")]
    [InlineData("test-location")]
    [InlineData("Survived")]
    [InlineData("Timeout")]
    [InlineData("NoCoverage")]
    [InlineData("RuntimeError")]
    [InlineData("Pending")]
    [InlineData("unknown-status")]
    public void AlteredDefinitionsAndNonKilledOutcomesReceiveNoCredit(string fault)
    {
        using var fixture = new Fixture();
        fixture.ChangeRerunReport(report =>
        {
            var file = report["files"]!["src/B.cs"]!;
            var mutant = file["mutants"]![0]!;
            var testFile = report["testFiles"]!["tests/BTests.cs"]!;
            switch (fault)
            {
                case "id": mutant["id"] = "different"; break;
                case "operator": mutant["mutatorName"] = "Different"; break;
                case "replacement": mutant["replacement"] = "different"; break;
                case "location": mutant["location"]!["start"]!["column"] = 2; break;
                case "report-source": file["source"] = "different"; break;
                case "missing-killed-by": mutant["killedBy"] = new JsonArray(); break;
                case "unknown-test": mutant["killedBy"] = new JsonArray("unknown"); break;
                case "test-definition": testFile["tests"]![0]!["name"] = "Fixture.BTests.Other"; break;
                case "test-source": testFile["source"] = "different"; break;
                case "test-location": testFile["tests"]![0]!["location"]!["end"]!["line"] = 999; break;
                default: mutant["status"] = fault; break;
            }
        });
        Assert.Equal(4, fixture.Refine().ExitCode);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Theory]
    [InlineData("Survived")]
    [InlineData("NoCoverage")]
    [InlineData("Killed")]
    public void OriginalOutcomesOtherThanTimeoutCannotGainCredit(string originalStatus)
    {
        using var fixture = new Fixture();
        fixture.ChangeOriginalReport(originalStatus);
        Assert.Equal(4, fixture.Refine().ExitCode);
        Assert.Empty(fixture.Inner.Calls());
    }

    internal sealed class Fixture : IDisposable
    {
        internal IncrementalTestServiceTests.Fixture Inner { get; } = new();
        internal IncrementalTestService Service { get; }
        internal IncrementalTestProfile Original { get; }
        internal IncrementalTestProfile Rerun { get; private set; }
        private readonly Dictionary<string, IncrementalTestState> _states = new(StringComparer.Ordinal);
        private string? _previousProducerDirectory;
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        internal Fixture(bool refreshOriginal = true, bool narrow = true, int? rerunGuard = null,
            int? rerunAdditionalTimeout = null, bool budgetOnOriginal = false)
        {
            Inner.Write("tool.txt", "immutable fixture tool input");
            Inner.Write("tests/ATests.cs", "class ATests { public void Value() {} }");
            Inner.Write("tests/BTests.cs", "class BTests { public void Value() {} }");
            Original = Inner.Profile with { ToolFiles = [.. Inner.Profile.ToolFiles, "tool.txt"] };
            if (rerunAdditionalTimeout is not null)
            {
                Inner.Write("stryker.json", "{\"stryker-config\":{\"additional-timeout\":15000,\"mutation-level\":\"Complete\"}}");
                Original = Original with
                {
                    ConfigFiles = [.. Original.ConfigFiles, "stryker.json"],
                    StrykerConfigFile = "stryker.json",
                    TimeoutSeconds = 120,
                    MutationCommand = [.. Original.MutationCommand!, "--config-file", "{mutationConfig}"],
                    RerunAdditionalTimeoutMilliseconds = budgetOnOriginal ? rerunAdditionalTimeout : null,
                };
            }
            Inner.Save(Original);
            Rerun = Original with
            {
                Id = "rerun", SourceFiles = ["src/B.cs"], CanarySourceFile = "src/B.cs",
                FullTestFilter = narrow ? "(" + Original.FullTestFilter + ")&(FullyQualifiedName=Fixture.BTests.Value)" : Original.FullTestFilter,
                TimeoutSeconds = rerunGuard ?? Original.TimeoutSeconds,
                RerunAdditionalTimeoutMilliseconds = rerunAdditionalTimeout,
                MutationCommand = [.. Original.MutationCommand!, "--mutate", "{sourceFile}{0..2}{4..6}"],
            };
            ChangeRerun(Rerun);
            Service = new(Inner);
            Seed(Original, "profile.json", refreshOriginal);
            Seed(Rerun, "rerun.json", true);
        }

        internal IncrementalRefinementResult Refine() => Service.Refine(new(Inner.Path, "profile.json", "rerun.json",
            "Reviewed narrower native test filter and exact source character range."));
        internal void ChangeRerun(IncrementalTestProfile profile)
        {
            Rerun = profile;
            Inner.Write("rerun.json", JsonSerializer.Serialize(profile, Json));
        }
        internal IEnumerable<string> Reports() => Directory.GetFiles(Inner.Full(IncrementalTestService.LocalRoot), "*-mutation.json", SearchOption.AllDirectories);
        internal string ReportPath(string id, string source) => Inner.Full(IncrementalTestService.LocalRoot + "/" + id + "/"
            + _states[id].Batches.Values.Single(batch => batch.SourceFile == source).Mutation!.ReportPath);
        internal void RefreshOriginal()
        {
            var state = _states["sample"];
            foreach (var key in state.Batches.Keys.ToArray())
                state.Batches[key] = state.Batches[key] with { Unit = state.Batches[key].Unit! with { FinishedUtc = DateTimeOffset.UtcNow } };
            Save("sample");
        }
        internal void FailOriginalUnitExecution()
        {
            var state = _states["sample"];
            var key = state.Batches.Keys.First();
            var unit = state.Batches[key].Unit!;
            state.Batches[key] = state.Batches[key] with { Unit = unit with { Execution = unit.Execution! with { ExitCode = 1 } } };
            Save("sample");
        }
        internal void ChangeRerunState(Func<IncrementalTestState, IncrementalTestState> change)
        {
            _states["rerun"] = change(_states["rerun"]);
            Save("rerun");
        }
        internal void ChangeRerunStage(Func<IncrementalStageResult, IncrementalStageResult> change)
            => ChangeStage("rerun", "src/B.cs", change);

        internal void RevalidatePreviousProducer()
        {
            _previousProducerDirectory = Directory.CreateTempSubdirectory("cis-refinement-producer-").FullName;
            var testing = System.IO.Path.Combine(_previousProducerDirectory, "Cis.Modules.Testing.dll");
            var helper = System.IO.Path.Combine(_previousProducerDirectory, "Cis.Abstractions.dll");
            File.Copy(typeof(IncrementalTestService).Assembly.Location, testing);
            File.Copy(typeof(CisProcessJob).Assembly.Location, helper);
            // Synthetic previous-producer bytes are never loaded or executed.
            File.AppendAllText(testing, "previous fixture testing producer");
            File.AppendAllText(helper, "previous fixture helper producer");
            var read = typeof(IncrementalTestService).GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Static)!;
            var select = typeof(IncrementalTestService).GetMethod("Select", BindingFlags.NonPublic | BindingFlags.Instance)!;
            foreach (var (id, path) in new[] { ("sample", "profile.json"), ("rerun", "rerun.json") })
            {
                var input = read.Invoke(null, [new IncrementalTestOptions(Inner.Path, path), Hash(testing), Hash(helper), false, null]);
                var selection = select.Invoke(Service, [input, new IncrementalTestState(), false])!;
                var batches = (IncrementalTestBatch[])selection.GetType().GetProperty("Batches")!.GetValue(selection)!;
                foreach (var batch in batches)
                {
                    var saved = _states[id].Batches[batch.Id];
                    _states[id].Batches[batch.Id] = saved with
                    {
                        Unit = saved.Unit! with { Fingerprint = batch.UnitFingerprint,
                            Execution = saved.Unit.Execution! with { CisToolAssemblySha256 = Hash(helper) } },
                        Mutation = saved.Mutation! with { Fingerprint = batch.MutationFingerprint,
                            Execution = saved.Mutation.Execution! with { CisToolAssemblySha256 = Hash(helper) },
                            RecoveryExecution = saved.Mutation.RecoveryExecution! with { CisToolAssemblySha256 = Hash(helper) },
                            PostflightExecution = saved.Mutation.PostflightExecution! with { CisToolAssemblySha256 = Hash(helper) } },
                    };
                }
                Save(id);
                var result = Service.Revalidate(new(Inner.Path, path), testing, "Reviewed compatible previous fixture producers.");
                Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
                _states[id] = JsonSerializer.Deserialize<IncrementalTestState>(File.ReadAllText(Inner.Full(IncrementalTestService.LocalRoot + "/" + id + "/state.json")), Json)!;
                Assert.All(_states[id].Batches.Values, saved => Assert.Single(saved.Mutation!.ProducerRevalidations));
            }
        }
        private void ChangeStage(string id, string source, Func<IncrementalStageResult, IncrementalStageResult> change)
        {
            var state = _states[id];
            var key = state.Batches.Single(pair => pair.Value.SourceFile == source).Key;
            state.Batches[key] = state.Batches[key] with { Mutation = change(state.Batches[key].Mutation!) };
            Save(id);
        }
        internal void ChangeRerunReport(Action<JsonObject> change) => ChangeReport(Rerun, "src/B.cs", change);
        internal void ChangeOriginalReport(string status) => ChangeReport(Original, "src/B.cs", report => report["files"]!["src/B.cs"]!["mutants"]![0]!["status"] = status);
        private void ChangeReport(IncrementalTestProfile profile, string source, Action<JsonObject> change)
        {
            var path = ReportPath(profile.Id, source);
            var report = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            change(report);
            File.WriteAllText(path, report.ToJsonString(Json));
            ChangeStage(profile.Id, source, stage => stage with { ReportSha256 = Hash(path), Mutation = Summary(profile, source, path) });
        }
        private void Seed(IncrementalTestProfile profile, string profilePath, bool refreshed)
        {
            var plan = Service.Plan(new(Inner.Path, profilePath));
            Assert.Equal(0, plan.ExitCode);
            var state = new IncrementalTestState();
            _states[profile.Id] = state;
            foreach (var batch in plan.Batches)
            {
                var prefix = IncrementalTestService.LocalRoot + "/" + profile.Id + "/reports/" + batch.Id;
                var unit = prefix + "-unit.xml";
                Inner.Write(unit, "<TestRun><Results><UnitTestResult testName=\"Fixture.Value\" outcome=\"Passed\" duration=\"00:00:00.001\" /></Results><ResultSummary outcome=\"Completed\"><Counters total=\"1\" executed=\"1\" passed=\"1\" failed=\"0\" /></ResultSummary></TestRun>");
                var mutation = prefix + "-mutation.json";
                var letter = System.IO.Path.GetFileNameWithoutExtension(batch.SourceFile);
                var status = profile.Id == "sample" && letter == "B" ? "Timeout" : "Killed";
                var testSource = File.ReadAllText(Inner.Full("tests/" + letter + "Tests.cs"));
                Inner.Write(mutation, JsonSerializer.Serialize(new
                {
                    schemaVersion = "2", thresholds = new { high = 100, low = 90 },
                    files = new Dictionary<string, object> { [batch.SourceFile] = new
                    {
                        language = "cs", source = File.ReadAllText(Inner.Full(batch.SourceFile)),
                        mutants = new[] { new { id = "0", mutatorName = "Boolean", replacement = "false", location = Location(1, 2), status,
                            killedBy = status == "Killed" ? new[] { "native-" + letter } : [], coveredBy = new[] { "native-" + letter } } },
                    } },
                    testFiles = new Dictionary<string, object> { ["tests/" + letter + "Tests.cs"] = new
                    {
                        language = "cs", source = testSource,
                        tests = new[] { new { id = "native-" + letter, name = "Fixture." + letter + "Tests.Value", location = Location(1, testSource.Length + 1) } },
                    } },
                }, Json));
                var relative = "reports/" + batch.Id;
                var unitStage = new IncrementalStageResult(batch.UnitFingerprint, "completed", true, relative + "-unit.xml", Hash(Inner.Full(unit)),
                    Execution(profile, batch, profile.UnitCommand, false), 1, 1, 0, 0, null,
                    DateTimeOffset.UtcNow.AddSeconds(refreshed ? -1 : -20), []);
                var mutationStage = new IncrementalStageResult(batch.MutationFingerprint, status == "Timeout" ? "completed-with-timeouts" : "completed", true,
                    relative + "-mutation.json", Hash(Inner.Full(mutation)), Execution(profile, batch, profile.MutationCommand!, false),
                    1, status == "Killed" ? 1 : 0, status == "Killed" ? 0 : 1, 0, Summary(profile, batch.SourceFile, Inner.Full(mutation)),
                    DateTimeOffset.UtcNow.AddSeconds(-3), [])
                {
                    RecoveryExecution = Execution(profile, batch, profile.RecoveryCommand!, true),
                    PostflightExecution = Execution(profile, batch, profile.UnitCommand, true), PostflightPassed = 1,
                };
                state.Batches.Add(batch.Id, new(batch.SourceFile, unitStage, mutationStage));
            }
            Save(profile.Id);
        }
        private MutationNativeExecution Execution(IncrementalTestProfile profile, IncrementalTestBatch batch, string[] command, bool full)
        {
            var output = Inner.Full(IncrementalTestService.LocalRoot + "/" + profile.Id + "/work/012345abcdef");
            return new("exited", 0, 1, 1, 1, false, null)
            {
                CisToolAssemblySha256 = Hash(typeof(CisProcessJob).Assembly.Location),
                ExecutablePath = command[0],
                Command = command.Select(value => value.Replace("{sourceFile}", batch.SourceFile, StringComparison.Ordinal)
                    .Replace("{testFilter}", full ? profile.FullTestFilter : batch.TestFilter, StringComparison.Ordinal)
                    .Replace("{mutationConfig}", System.IO.Path.Combine(output, "stryker-config.json"), StringComparison.Ordinal)
                    .Replace("{output}", output, StringComparison.Ordinal)).ToArray(),
            };
        }
        private TestMutationSummary Summary(IncrementalTestProfile profile, string source, string path)
        {
            var suite = new TestSuiteProfile(profile.Id, profile.Id, "mutation", "native", "run", ".", "stryker-json", path,
                "-", "-", "-", "always", "local", "current-result");
            return new StrykerJsonResultAdapter().Read(new(Inner.Path, suite, path, null, null, profile.BreakThreshold)).Mutation! with
                { Qualified = false, Scope = "incremental-batch", SourcePath = "" };
        }
        private static object Location(int start, int end) => new { start = new { line = 1, column = start }, end = new { line = 1, column = end } };
        private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        private void Save(string id) => Inner.Write(IncrementalTestService.LocalRoot + "/" + id + "/state.json", JsonSerializer.Serialize(_states[id], Json));
        public void Dispose()
        {
            Inner.Dispose();
            if (_previousProducerDirectory is not null)
            {
                Assert.StartsWith("cis-refinement-producer-", System.IO.Path.GetFileName(_previousProducerDirectory), StringComparison.Ordinal);
                Assert.Equal(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetTempPath()),
                    System.IO.Path.GetDirectoryName(_previousProducerDirectory), OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                Directory.Delete(_previousProducerDirectory, true);
            }
        }
    }
}
