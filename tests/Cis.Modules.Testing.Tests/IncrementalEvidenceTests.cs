using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class IncrementalEvidenceTests
{
    [Fact]
    public void CurrentNativeMissesProduceARawReceiptWithoutExecutionOrStateWrites()
    {
        using var fixture = new Fixture();
        var before = fixture.Snapshot();
        var result = fixture.Verify();
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        Assert.Equal("verified", result.Status);
        Assert.False(result.Qualified);
        Assert.True(result.ScopeComplete);
        Assert.False(result.ThresholdMet);
        Assert.Equal(50, result.Raw!.Score);
        Assert.Equal(result.Raw, result.Effective);
        Assert.Equal(2, result.Batches.Count);
        Assert.NotEmpty(result.RuntimeIdentifier!);
        Assert.NotEmpty(result.OsDescription!);
        Assert.All(result.Batches, batch =>
        {
            Assert.True(batch.FullFile);
            Assert.True(batch.FullPostflightReceiptVerified);
            Assert.False(batch.RawPostflightRetained);
            Assert.Equal(4, batch.Executions.Length);
            Assert.Equal(64, batch.Executions[0].CommandSha256.Length);
            Assert.Equal(batch.UnitReportSha256, Hash(fixture.Inner.Full(batch.UnitReportPath)));
            Assert.Contains(result.Closures, closure => closure.Id == batch.ClosureId);
        });
        Assert.Equal(before, fixture.Snapshot());
        Assert.Empty(fixture.Inner.Calls());
    }

    [Fact]
    public void ExplicitNativeRerunRecomputesCreditAndIgnoresInventedSavedSidecars()
    {
        using var fixture = new Fixture();
        fixture.Inner.Write(IncrementalTestService.LocalRoot + "/sample/refinements/rerun.json", "{\"effective\":{\"killed\":9999},\"qualified\":true}");
        var before = fixture.Snapshot();
        var result = fixture.Verify("rerun.json");
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        Assert.Equal(1, result.Raw!.Killed);
        Assert.Equal(2, result.Effective!.Killed);
        Assert.Equal(100, result.Effective.Score);
        Assert.Single(result.Credits);
        Assert.Single(result.Refinements);
        Assert.Equal(2, result.Refinements[0].RerunReports.Count);
        Assert.All(result.Refinements[0].RerunReports, pin => Assert.Equal(pin.Sha256, Hash(fixture.Inner.Full(pin.Path))));
        Assert.True(result.ThresholdMet);
        Assert.Equal(before, fixture.Snapshot());
        Assert.Empty(fixture.Inner.Calls());
    }

    [Fact]
    public void MultipleCompletedRerunsCannotCountTheSameTimeoutTwice()
    {
        using var fixture = new Fixture();
        fixture.CloneRerun();
        var result = fixture.Verify("rerun.json", "another.json");
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        Assert.Equal(2, result.Refinements.Count);
        Assert.Single(result.Credits);
        Assert.Equal(2, result.Effective!.Killed);
        Assert.Equal(0, result.Effective.TimedOut);
        Assert.Equal(2, result.Effective.Denominator);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Theory]
    [InlineData("source")]
    [InlineData("test")]
    [InlineData("dependency")]
    [InlineData("configuration")]
    [InlineData("tool")]
    [InlineData("environment")]
    [InlineData("hash")]
    [InlineData("native")]
    [InlineData("unit")]
    [InlineData("recovery")]
    [InlineData("postflight")]
    [InlineData("postflight-count")]
    [InlineData("helper")]
    [InlineData("history")]
    [InlineData("command")]
    [InlineData("active")]
    [InlineData("completed")]
    public void SavedCompletionFlagsCannotOverrideDamagedOrStaleEvidence(string fault)
    {
        using var fixture = new Fixture();
        switch (fault)
        {
            case "source": fixture.Inner.Write("src/A.cs", "class Changed {}"); break;
            case "test": fixture.Inner.Write("tests/ATests.cs", "class ChangedTest {}"); break;
            case "dependency": fixture.Inner.Write("src/Shared.cs", "class ChangedDependency {}"); break;
            case "configuration": fixture.Inner.Write("config.json", "{\"changed\":true}"); break;
            case "tool": File.AppendAllText(fixture.Inner.Full("tool.txt"), "changed"); break;
            case "environment": fixture.WriteProfile("profile.json", fixture.Profile("profile.json") with { Environment = new() { ["EXPLICIT_INPUT"] = "changed" } }); break;
            case "hash": File.AppendAllText(fixture.Report("sample", "src/A.cs"), " "); break;
            case "native": fixture.ChangeStage("sample", "src/A.cs", stage => stage with { Execution = stage.Execution! with { Status = "timeout" } }); break;
            case "unit": fixture.ChangeUnit(stage => stage with { Execution = stage.Execution! with { ExitCode = 1 } }); break;
            case "recovery": fixture.ChangeStage("sample", "src/A.cs", stage => stage with { RecoveryExecution = stage.RecoveryExecution! with { ExitCode = 1 } }); break;
            case "postflight": fixture.ChangeStage("sample", "src/A.cs", stage => stage with { PostflightExecution = stage.PostflightExecution! with { Error = "failed" } }); break;
            case "postflight-count": fixture.ChangeStage("sample", "src/A.cs", stage => stage with { PostflightPassed = 0 }); break;
            case "helper": fixture.ChangeStage("sample", "src/A.cs", stage => stage with { Execution = stage.Execution! with { CisToolAssemblySha256 = new('a', 64) } }); break;
            case "history": fixture.ChangeStage("sample", "src/A.cs", stage => stage with { ProducerRevalidations = null! }); break;
            case "command": fixture.ChangeStage("sample", "src/A.cs", stage => stage with { Execution = stage.Execution! with { Command = ["unrelated"] } }); break;
            case "active": fixture.ChangeState("sample", state => state with { RecoveryRequired = true }); break;
            case "completed": fixture.ChangeStage("sample", "src/A.cs", stage => stage with { Completed = false }); break;
        }
        var result = fixture.Verify();
        Assert.Equal(4, result.ExitCode);
        Assert.False(result.ScopeComplete);
        Assert.Empty(result.Batches);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("test-source")]
    [InlineData("test-location")]
    [InlineData("replacement-location")]
    public void EveryOriginalKillRequiresCurrentNativeTestAndMutantDefinitions(string fault)
    {
        using var fixture = new Fixture();
        fixture.ChangeReport("sample", "src/A.cs", report =>
        {
            var mutant = report["files"]!["src/A.cs"]!["mutants"]![0]!;
            switch (fault)
            {
                case "missing": mutant["killedBy"] = new JsonArray(); break;
                case "unknown": mutant["killedBy"] = new JsonArray("unknown-test"); break;
                case "test-source": report["testFiles"]!["tests/ATests.cs"]!["source"] = "changed"; break;
                case "test-location": report["testFiles"]!["tests/ATests.cs"]!["tests"]![0]!["location"]!["end"]!["line"] = 999; break;
                case "replacement-location": mutant["location"]!["end"]!["line"] = 999; break;
            }
        });
        Assert.Equal(4, fixture.Verify().ExitCode);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Theory]
    [InlineData("range")]
    [InlineData("level")]
    [InlineData("level-alias")]
    [InlineData("level-joined")]
    [InlineData("since")]
    [InlineData("since-colon")]
    [InlineData("baseline-colon")]
    [InlineData("alternate-config")]
    public void CompletedNarrowedOriginalsCannotClaimFullFileEvidence(string fault)
    {
        using var fixture = new Fixture();
        var profile = fixture.Profile("profile.json");
        var command = fault switch
        {
            "range" => new[] { "--mutate", "{sourceFile}{0..2}" },
            "level" => ["--mutation-level", "Standard"],
            "level-alias" => ["-l", "Standard"],
            "level-joined" => ["-l:Standard"],
            "since-colon" => ["--since:main"],
            "baseline-colon" => ["--with-baseline:main"],
            "alternate-config" => ["--config-file", "untracked-config.json"],
            _ => ["--since", "main"],
        };
        fixture.WriteProfile("profile.json", profile with { MutationCommand = [.. profile.MutationCommand!, .. command] });
        fixture.Rebind("profile.json", command);
        Assert.Equal(4, fixture.Verify().ExitCode);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Fact]
    public void AnUndeclaredImplicitNativeConfigCannotOverrideTheFullFilePolicy()
    {
        using var fixture = new Fixture();
        fixture.Inner.Write("stryker-config.json", "{\"stryker-config\":{\"since\":{}}}");
        var result = fixture.Verify();
        Assert.Equal(4, result.ExitCode);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Implicit native configuration", StringComparison.Ordinal));
        Assert.Empty(fixture.Inner.Calls());
    }

    [Theory]
    [InlineData("since", false)]
    [InlineData("baseline", false)]
    [InlineData("since", true)]
    [InlineData("baseline", true)]
    public void NativeIncrementalObjectsRequireAnExplicitDisabledValue(string name, bool explicitlyDisabled)
    {
        using var fixture = new Fixture();
        var settings = new JsonObject { ["mutation-level"] = "Complete", [name] = new JsonObject() };
        if (explicitlyDisabled) settings[name]!["enabled"] = false;
        fixture.Inner.Write("config.json", new JsonObject { ["stryker-config"] = settings }.ToJsonString(Fixture.Json));
        var profile = fixture.Profile("profile.json");
        fixture.WriteProfile("profile.json", profile with { StrykerConfigFile = "config.json",
            MutationCommand = [.. profile.MutationCommand!, "--config-file", "{mutationConfig}"] });
        fixture.Rebind("profile.json", ["--config-file", "{mutationConfig}"]);
        var result = fixture.Verify();
        Assert.True(result.ExitCode == (explicitlyDisabled ? 0 : 4), string.Join("; ", result.Diagnostics));
        Assert.Empty(fixture.Inner.Calls());
    }

    [Fact]
    public void AChangedRerunOrAnOldFullUnitBaselineCannotPublishCredit()
    {
        using var fixture = new Fixture();
        fixture.ChangeUnit(stage => stage with { FinishedUtc = DateTimeOffset.UtcNow.AddDays(-1) });
        Assert.Equal(4, fixture.Verify("rerun.json").ExitCode);
        fixture.ChangeUnit(stage => stage with { FinishedUtc = DateTimeOffset.UtcNow });
        fixture.ChangeReport("rerun", "src/B.cs", report => report["files"]!["src/B.cs"]!["mutants"]![0]!["replacement"] = "true");
        Assert.Equal(4, fixture.Verify("rerun.json").ExitCode);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Fact]
    public void DeclarationOnlyFilesHaveZeroDenominatorsWithoutLoweringTheAggregateGate()
    {
        using var fixture = new Fixture();
        fixture.ChangeReport("sample", "src/B.cs", report => report["files"]!["src/B.cs"]!["mutants"] = new JsonArray());
        var result = fixture.Verify();
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        Assert.Equal(1, result.Raw!.Denominator);
        Assert.Equal(100, result.Raw.Score);
        Assert.Equal(0, result.Batches.Single(batch => batch.SourceFile == "src/B.cs").Raw!.Denominator);
        Assert.Null(result.Batches.Single(batch => batch.SourceFile == "src/B.cs").Raw!.Score);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Fact]
    public void AUnitOnlyProfileVerifiesItsCurrentNativeUnitEvidenceWithoutMutationClaims()
    {
        using var fixture = new Fixture();
        fixture.WriteProfile("profile.json", fixture.Profile("profile.json") with { MutationCommand = null, RecoveryCommand = null });
        fixture.Rebind("profile.json");
        var result = fixture.Verify();
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        Assert.Null(result.Raw);
        Assert.Null(result.Effective);
        Assert.False(result.ThresholdMet);
        Assert.All(result.Batches, batch =>
        {
            Assert.False(batch.FullFile);
            Assert.False(batch.FullPostflightReceiptVerified);
            Assert.Null(batch.MutationReportPath);
            Assert.Single(batch.Executions);
        });
        Assert.Empty(fixture.Inner.Calls());
    }

    [Fact]
    public void ExportedEvidenceContainsDigestsRatherThanCommandOrEnvironmentValues()
    {
        using var fixture = new Fixture();
        var profile = fixture.Profile("profile.json");
        fixture.WriteProfile("profile.json", profile with { Environment = new() { ["PRIVATE_SETTING"] = "secret-fixture-value" } });
        fixture.Rebind("profile.json");
        var result = fixture.Verify();
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        var json = JsonSerializer.Serialize(result, Fixture.Json);
        Assert.DoesNotContain("secret-fixture-value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_SETTING", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"command\":", json, StringComparison.Ordinal);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(json) <= 1024 * 1024);
        Assert.Empty(fixture.Inner.Calls());
    }

    [Fact]
    public void ElevenBatchesShareOneLargeClosureWithoutRepeatingItsPathsInTheReceipt()
    {
        using var fixture = new Fixture();
        fixture.ExpandSources(11, 1000, 140);
        var result = fixture.Verify();
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        Assert.Equal(11, result.Batches.Count);
        Assert.Equal(1, result.Raw!.Killed);
        Assert.Equal(2, result.Raw.Denominator);
        Assert.True(result.Closures.Count < result.Batches.Count);
        Assert.Equal(1000, result.Inputs.Count(item => item.Path.StartsWith("support/", StringComparison.Ordinal)));
        var json = JsonSerializer.Serialize(result, Fixture.Json);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(json) < 1024 * 1024);
        Assert.All(result.Closures, closure => Assert.All(closure.DependencyInputIndices,
            index => Assert.InRange(index, 0, result.Inputs.Count - 1)));
        Assert.Empty(fixture.Inner.Calls());
    }

    [Fact]
    public void AnOversizedReceiptFailsClosedWithoutWritingOrExecutingAnything()
    {
        using var fixture = new Fixture();
        fixture.ExpandSources(11, 1100, 1000);
        var before = fixture.Snapshot();
        var result = fixture.Verify();
        Assert.Equal(4, result.ExitCode);
        Assert.Contains(result.Diagnostics, item => item.Contains("1 MiB", StringComparison.Ordinal));
        Assert.Empty(result.Inputs);
        Assert.Equal(before, fixture.Snapshot());
        Assert.Empty(fixture.Inner.Calls());
    }

    internal sealed class Fixture : IDisposable
    {
        internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        private readonly IncrementalRefinementTests.Fixture _base = new();
        private IncrementalTestService? _expanded;
        internal IncrementalTestServiceTests.Fixture Inner => _base.Inner;
        internal IncrementalTestService Service => _expanded ?? _base.Service;
        internal Fixture()
        {
            foreach (var path in new[] { "profile.json", "rerun.json" })
            {
                WriteProfile(path, Profile(path) with { MutationCommand = [.. Profile(path).MutationCommand!, "--mutation-level", "Complete"] });
                Rebind(path, ["--mutation-level", "Complete"]);
            }
        }
        internal IncrementalEvidenceResult Verify(params string[] reruns) => Service.Verify(new(Inner.Path, "profile.json", reruns));
        internal string[] Snapshot() => Directory.GetFiles(Inner.Path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path => System.IO.Path.GetRelativePath(Inner.Path, path) + ":" + Hash(path)).ToArray();
        internal IncrementalTestProfile Profile(string path) => JsonSerializer.Deserialize<IncrementalTestProfile>(File.ReadAllText(Inner.Full(path)), Json)!;
        internal void WriteProfile(string path, IncrementalTestProfile profile) => Inner.Write(path, JsonSerializer.Serialize(profile, Json));
        private IncrementalTestState State(string id) => JsonSerializer.Deserialize<IncrementalTestState>(File.ReadAllText(Inner.Full(IncrementalTestService.LocalRoot + "/" + id + "/state.json")), Json)!;
        internal string Report(string id, string source) => Inner.Full(IncrementalTestService.LocalRoot + "/" + id + "/" + State(id).Batches.Values.Single(batch => batch.SourceFile == source).Mutation!.ReportPath);
        internal void ChangeState(string id, Func<IncrementalTestState, IncrementalTestState> change)
            => Inner.Write(IncrementalTestService.LocalRoot + "/" + id + "/state.json", JsonSerializer.Serialize(change(State(id)), Json));
        internal void ChangeStage(string id, string source, Func<IncrementalStageResult, IncrementalStageResult> change)
            => ChangeState(id, state =>
            {
                var key = state.Batches.Single(pair => pair.Value.SourceFile == source).Key;
                state.Batches[key] = state.Batches[key] with { Mutation = change(state.Batches[key].Mutation!) };
                return state;
            });
        internal void ChangeUnit(Func<IncrementalStageResult, IncrementalStageResult> change)
            => ChangeState("sample", state =>
            {
                foreach (var key in state.Batches.Keys.ToArray()) state.Batches[key] = state.Batches[key] with { Unit = change(state.Batches[key].Unit!) };
                return state;
            });
        internal void ChangeReport(string id, string source, Action<JsonObject> change)
        {
            var path = Report(id, source);
            var report = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            change(report);
            File.WriteAllText(path, report.ToJsonString(Json));
            var summary = new StrykerJsonResultAdapter().Read(new(Inner.Path,
                new(id, id, "mutation", "native", "run", ".", "stryker-json", path, "-", "-", "-", "always", "local", "current-result"),
                path, null, null, 90)).Mutation! with { Qualified = false, Scope = "incremental-batch", SourcePath = "" };
            if (report["files"]![source]!["mutants"]!.AsArray().Count == 0) summary = summary with { ValidEvidence = true };
            ChangeStage(id, source, stage => stage with { ReportSha256 = Hash(path), Mutation = summary });
        }
        internal void Rebind(string profilePath, string[]? addedNativeArguments = null)
        {
            var profile = Profile(profilePath);
            var plan = Service.Plan(new(Inner.Path, profilePath));
            Assert.Equal(0, plan.ExitCode);
            ChangeState(profile.Id, state =>
            {
                foreach (var batch in plan.Batches)
                {
                    var saved = state.Batches[batch.Id];
                    state.Batches[batch.Id] = saved with
                    {
                        Unit = saved.Unit! with { Fingerprint = batch.UnitFingerprint },
                        Mutation = saved.Mutation! with { Fingerprint = batch.MutationFingerprint,
                            Execution = saved.Mutation.Execution! with { Command = [.. saved.Mutation.Execution!.Command!, .. (addedNativeArguments ?? [])
                                .Select(value => value.Replace("{mutationConfig}", System.IO.Path.Combine(Inner.Full(IncrementalTestService.LocalRoot + "/" + profile.Id + "/work/012345abcdef"), "stryker-config.json"), StringComparison.Ordinal))] } },
                    };
                }
                return state;
            });
        }
        internal void CloneRerun()
        {
            var profile = Profile("rerun.json") with { Id = "another" };
            WriteProfile("another.json", profile);
            var from = Inner.Full(IncrementalTestService.LocalRoot + "/rerun");
            var to = Inner.Full(IncrementalTestService.LocalRoot + "/another");
            foreach (var path in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                Inner.Write(System.IO.Path.GetRelativePath(Inner.Path, System.IO.Path.Combine(to, System.IO.Path.GetRelativePath(from, path))),
                    File.ReadAllText(path));
            ChangeState("another", state =>
            {
                foreach (var key in state.Batches.Keys.ToArray())
                {
                    var saved = state.Batches[key];
                    state.Batches[key] = saved with { Unit = Rewrite(saved.Unit!), Mutation = Rewrite(saved.Mutation!) };
                }
                return state;
                static IncrementalStageResult Rewrite(IncrementalStageResult stage) => stage with
                {
                    Execution = RewriteExecution(stage.Execution!),
                    RecoveryExecution = stage.RecoveryExecution is null ? null : RewriteExecution(stage.RecoveryExecution),
                    PostflightExecution = stage.PostflightExecution is null ? null : RewriteExecution(stage.PostflightExecution),
                };
                static MutationNativeExecution RewriteExecution(MutationNativeExecution execution) => execution with
                {
                    Command = execution.Command!.Select(value => value.Replace('\\', '/').Contains("/rerun/work/", StringComparison.Ordinal)
                        ? value.Replace("rerun", "another", StringComparison.Ordinal) : value).ToArray(),
                };
            });
            Rebind("another.json");
        }

        internal void ExpandSources(int count, int extraInputs, int pathLength)
        {
            var profile = Profile("profile.json");
            var added = Enumerable.Range(0, count - profile.SourceFiles.Length).Select(index => "src/Declaration" + index + ".cs").ToArray();
            foreach (var source in added) Inner.Write(source, "public interface IDeclaration" + System.IO.Path.GetFileNameWithoutExtension(source) + " {}");
            var directory = "support/" + string.Join('/', Enumerable.Range(0, pathLength / 100).Select(index => new string('x', 90) + index));
            var extras = Enumerable.Range(0, extraInputs).Select(index => directory + "/Input" + index + ".cs").ToArray();
            foreach (var file in extras) Inner.Write(file, "// shared input");
            _expanded = new(new ExpandedReader(Inner, extras));
            profile = profile with { SourceFiles = [.. profile.SourceFiles, .. added] };
            WriteProfile("profile.json", profile);
            var plan = Service.Plan(new(Inner.Path, "profile.json"));
            Assert.Equal(0, plan.ExitCode);
            ChangeState(profile.Id, state =>
            {
                var template = state.Batches.Values.First();
                foreach (var batch in plan.Batches.Where(batch => added.Contains(batch.SourceFile, StringComparer.Ordinal)))
                {
                    var prefix = "reports/" + batch.Id;
                    Inner.Write(IncrementalTestService.LocalRoot + "/sample/" + prefix + "-unit.xml",
                        File.ReadAllText(Inner.Full(IncrementalTestService.LocalRoot + "/sample/" + template.Unit!.ReportPath)));
                    Inner.Write(IncrementalTestService.LocalRoot + "/sample/" + prefix + "-mutation.json", JsonSerializer.Serialize(new
                    {
                        schemaVersion = "2", thresholds = new { high = 100, low = 90 },
                        files = new Dictionary<string, object> { [batch.SourceFile] = new
                        {
                            language = "cs", source = File.ReadAllText(Inner.Full(batch.SourceFile)), mutants = Array.Empty<object>(),
                        } },
                    }, Json));
                    var unit = template.Unit! with { ReportPath = prefix + "-unit.xml",
                        ReportSha256 = Hash(Inner.Full(IncrementalTestService.LocalRoot + "/sample/" + prefix + "-unit.xml")),
                        Execution = Execution(profile.UnitCommand, false) };
                    var mutation = template.Mutation! with { ReportPath = prefix + "-mutation.json",
                        Execution = Execution(profile.MutationCommand!, false), RecoveryExecution = Execution(profile.RecoveryCommand!, true),
                        PostflightExecution = Execution(profile.UnitCommand, true) };
                    state.Batches.Add(batch.Id, new(batch.SourceFile, unit, mutation));
                    MutationNativeExecution Execution(string[] command, bool full) => template.Unit.Execution! with
                    {
                        ExecutablePath = command[0], Command = command.Select(value => value.Replace("{sourceFile}", batch.SourceFile, StringComparison.Ordinal)
                            .Replace("{testFilter}", full ? profile.FullTestFilter : batch.TestFilter, StringComparison.Ordinal)
                            .Replace("{output}", Inner.Full(IncrementalTestService.LocalRoot + "/sample/work/012345abcdef"), StringComparison.Ordinal)).ToArray(),
                    };
                }
                return state;
            });
            foreach (var source in added) ChangeReport(profile.Id, source, _ => { });
            Rebind("profile.json");
        }

        private sealed class ExpandedReader(IncrementalTestServiceTests.Fixture inner, string[] extraInputs) : ICisGraphSnapshotReader
        {
            public CisGraphSnapshotReadResult Read(string repositoryPath)
            {
                var result = inner.Read(repositoryPath);
                return result with { Manifest = result.Manifest! with { Inputs = [.. result.Manifest!.Inputs,
                    .. extraInputs.Select(file => new CisGraphInput(file, Hash(inner.Full(file))))] } };
            }
        }
        public void Dispose() => _base.Dispose();
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
