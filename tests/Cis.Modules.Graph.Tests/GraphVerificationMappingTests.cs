using System.Security.Cryptography;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Repository;

namespace Cis.Modules.Graph.Tests;

public sealed partial class GraphBuilderTests
{
    [Fact]
    public void FencedExamplesAreNotCriterionDeclarationsAndMalformedMappingHeadersAreDiagnosed()
    {
        var examples = VerificationMappingExtractor.Read("""
            ```markdown
            | ID | Criterion |
            | --- | --- |
            | AC-10 | A synthetic example. |
            ```

            ## Implementation and verification

            | Criterion | Check |
            | --- | --- |
            | AC-10 | example |
            """);
        Assert.Empty(examples.Criteria);
        Assert.Empty(examples.Mappings);
        Assert.NotEmpty(examples.Issues);
    }

    [Fact]
    public void ExplicitCriterionMappingReachesExactTaskArtifactCheckAndRecordedResult()
    {
        using var fixture = VerificationFixture.Create();
        Assert.Equal(0, CreateBuilder([fixture.Adapter]).Build(fixture.Repository.Path).ExitCode);
        var graph = fixture.Graph();
        var criterion = Assert.Single(graph.Nodes, node => node.Kind == "requirement" && node.LocalId == "fixture:acceptance#AC-10");
        Assert.Equal("source-declared-not-assessed", criterion.Properties["acceptance"]);
        var task = Assert.Single(graph.Nodes, node => node.Kind == "document" && node.LocalId == "fixture:task:one");
        var artifact = Assert.Single(graph.Nodes, node => node.Kind == "source-file" && node.LocalId == VerificationFixture.Artifact);
        var check = Assert.Single(graph.Nodes, node => node.Kind == "test" && node.Label == "ChecksFeature");
        Assert.Contains(graph.Edges, edge => edge.From == criterion.Key && edge.To == task.Key && edge.Type == "references");
        Assert.Contains(graph.Edges, edge => edge.From == task.Key && edge.To == artifact.Key && edge.Type == "references");
        var applicableCheck = Assert.Single(graph.Edges, edge => edge.From == artifact.Key && edge.To == check.Key && edge.Type == "verified-by");
        Assert.Equal("declared-applicable-check", applicableCheck.Properties["role"]);
        Assert.Equal("not-assessed", applicableCheck.Properties["coverageAssessment"]);
        Assert.Equal("not-assessed", applicableCheck.Properties["meaningAssessment"]);
        var result = fixture.ResultNode();
        Assert.Equal("repository-inputs-match-receipt", result.Properties["inputState"]);
        Assert.Equal("requires-current-native-verification", result.Properties["applicability"]);
        Assert.Equal("recorded-passed", fixture.ResultEdge().Properties["nativeOutcome"]);
        Assert.Contains(graph.Nodes, node => node.Subtype == "verification-input" && node.Properties["state"] == "unassessed-external"
            && node.Properties["observedSha256"] == "unassessed");
        Assert.Equal(0, new GraphValidator(new CisRepositoryContextResolver(), resultAdapters: [fixture.Adapter])
            .Validate(fixture.Repository.Path, strict: true).ExitCode);
    }

    [Theory]
    [InlineData("fixture:acceptance#AC-404", "fixture:task:one", VerificationFixture.TestPath + "#ChecksFeature")]
    [InlineData("fixture:acceptance#AC-10", "WORK-001", VerificationFixture.TestPath + "#ChecksFeature")]
    [InlineData("fixture:acceptance#AC-10", "fixture:task:one", VerificationFixture.TestPath)]
    public void ExplicitMappingRefusesUnresolvedCriterionTaskOrFileWideCheck(string criterion, string task, string check)
    {
        using var fixture = VerificationFixture.Create();
        fixture.WriteAcceptance(criterion, task, check);
        var build = CreateBuilder([fixture.Adapter]).Build(fixture.Repository.Path);
        Assert.Equal(0, build.ExitCode);
        Assert.Contains(build.Diagnostics, diagnostic => diagnostic.Code == "CIS-GRAPH-VERIFY-001");
        Assert.DoesNotContain(fixture.Graph().Nodes, node => node.Subtype == "verification-result");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void MissingNativeAdapterOrForeignBatchCannotInventExactExecution(bool adapterAvailable, bool foreignBatch)
    {
        using var fixture = VerificationFixture.Create();
        if (foreignBatch) fixture.Save(fixture.Receipt with { Batches = [fixture.Receipt.Batches[0] with { SourceFile = "src/Other.cs" }] });
        Assert.Equal(0, CreateBuilder(adapterAvailable ? [fixture.Adapter] : []).Build(fixture.Repository.Path).ExitCode);
        Assert.Equal("unknown", fixture.ResultEdge().Properties["nativeOutcome"]);
    }

    [Fact]
    public void ReceiptWithChangedInputsKeepsHistoricalCaseOutcomeButRequiresCurrentVerification()
    {
        using var fixture = VerificationFixture.Create();
        fixture.Repository.Write(VerificationFixture.Artifact, "namespace Example; public class Feature { public int Read() => 2; }");
        Assert.Equal(0, CreateBuilder([fixture.Adapter]).Build(fixture.Repository.Path).ExitCode);
        Assert.Equal("recorded-passed", fixture.ResultEdge().Properties["nativeOutcome"]);
        Assert.Equal("inputs-changed", fixture.ResultNode().Properties["inputState"]);
        Assert.Equal("requires-current-native-verification", fixture.ResultNode().Properties["applicability"]);
    }

    [Fact]
    public void SemanticCriterionEditRefreshesPayloadWithoutChangingManagedNativeHashOrBuildBaseline()
    {
        using var fixture = VerificationFixture.Create();
        var reader = fixture.Reader();
        using var scope = CisReadScope.Enter();
        var first = reader.ReadCurrent(fixture.Repository.Path);
        var before = Assert.Single(first.Manifest!.Inputs, input => input.Path == VerificationFixture.AcceptancePath);
        var absolute = Path.Combine(fixture.Repository.Path, VerificationFixture.AcceptancePath);
        File.WriteAllText(absolute, File.ReadAllText(absolute).Replace("The declared behavior is checked.", "The revised behavior is checked.", StringComparison.Ordinal));
        var second = reader.ReadCurrent(fixture.Repository.Path);
        var after = Assert.Single(second.Manifest!.Inputs, input => input.Path == VerificationFixture.AcceptancePath);
        Assert.Equal(before.Hash, after.Hash);
        Assert.NotEqual(before.ProjectionHash, after.ProjectionHash);
        Assert.Equal(first.Graph!.Build.Id, second.Graph!.Build.Id);
        Assert.NotSame(first, second);
        Assert.Contains(second.Graph.Nodes, node => node.LocalId == "fixture:acceptance#AC-10" && node.Label == "The revised behavior is checked.");
    }

    [Fact]
    public void ReceiptAssessmentTimestampAndTaskBookkeepingReuseTheSameProjection()
    {
        using var fixture = VerificationFixture.Create();
        var reader = fixture.Reader();
        using var scope = CisReadScope.Enter();
        var first = reader.ReadCurrent(fixture.Repository.Path);
        fixture.Save(fixture.Receipt with { VerifiedUtc = fixture.Receipt.VerifiedUtc.AddHours(1), GraphBuildId = "different-assessment-context" });
        var task = Path.Combine(fixture.Repository.Path, VerificationFixture.TaskPath);
        File.WriteAllText(task, File.ReadAllText(task).Replace("task_status: NotStarted", "task_status: Completed", StringComparison.Ordinal));
        Assert.Same(first, reader.ReadCurrent(fixture.Repository.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovingDeclaredCriterionOrMappingRefreshesOnlyContextLocators(bool mappingOnly)
    {
        using var fixture = VerificationFixture.Create();
        var reader = fixture.Reader();
        using var scope = CisReadScope.Enter();
        var first = reader.ReadCurrent(fixture.Repository.Path);
        var before = Assert.Single(first.Manifest!.Inputs, input => input.Path == VerificationFixture.AcceptancePath);
        var absolute = Path.Combine(fixture.Repository.Path, VerificationFixture.AcceptancePath);
        var content = File.ReadAllText(absolute);
        File.WriteAllText(absolute, mappingOnly
            ? content.Replace("## Implementation and verification", "<!-- Context note. -->\n\n## Implementation and verification", StringComparison.Ordinal)
            : content.Replace("# Acceptance evidence", "# Acceptance evidence\n\n<!-- Context note. -->", StringComparison.Ordinal));

        var second = reader.ReadCurrent(fixture.Repository.Path);
        var after = Assert.Single(second.Manifest!.Inputs, input => input.Path == VerificationFixture.AcceptancePath);
        Assert.Equal(before.Hash, after.Hash);
        Assert.Equal(first.Graph!.Build.Id, second.Graph!.Build.Id);
        Assert.NotEqual(before.ProjectionHash, after.ProjectionHash);
        var lines = File.ReadAllLines(absolute);
        var criterionLine = Array.FindIndex(lines, line => line.StartsWith("| AC-10 |", StringComparison.Ordinal)) + 1;
        var mappingLine = Array.FindIndex(lines, line => line.StartsWith("| fixture:acceptance#AC-10 |", StringComparison.Ordinal)) + 1;
        var criterion = Assert.Single(second.Graph.Nodes, node => node.LocalId == "fixture:acceptance#AC-10");
        Assert.Equal(criterionLine.ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.Single(criterion.Locations).LocatorValue);
        var declaredCheck = Assert.Single(second.Graph.Edges, edge => edge.From == criterion.Key && edge.Type == "verified-by");
        Assert.Equal(mappingLine.ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.Single(declaredCheck.Observations).LocatorValue);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("objective")]
    [InlineData("related-link")]
    public void SemanticTaskDocumentChangesRefreshContextWithoutChangingNativeMaterial(string field)
    {
        using var fixture = VerificationFixture.Create();
        fixture.Repository.Write("src/Orders.Api/Other.cs", "public class OtherCode {}");
        var path = Path.Combine(fixture.Repository.Path, VerificationFixture.TaskPath);
        File.AppendAllText(path, "\n## Objective\n\nOriginal objective.\n\n## Related context\n\n[Implementation](../../../../../src/Orders.Api/Feature.cs)\n");
        var reader = fixture.Reader();
        using var scope = CisReadScope.Enter();
        var first = reader.ReadCurrent(fixture.Repository.Path);
        var before = Assert.Single(first.Manifest!.Inputs, input => input.Path == VerificationFixture.TaskPath);
        var original = File.ReadAllText(path);
        var changed = field switch
        {
            "title" => original.Replace("title: Check declared behavior", "title: Revised declared behavior", StringComparison.Ordinal),
            "objective" => original.Replace("Original objective.", "Revised objective.", StringComparison.Ordinal),
            _ => original.Replace("src/Orders.Api/Feature.cs", "src/Orders.Api/Other.cs", StringComparison.Ordinal),
        };
        File.WriteAllText(path, changed);
        var second = reader.ReadCurrent(fixture.Repository.Path);
        var after = Assert.Single(second.Manifest!.Inputs, input => input.Path == VerificationFixture.TaskPath);
        Assert.Equal(before.Hash, after.Hash);
        Assert.Equal(first.Graph!.Build.Id, second.Graph!.Build.Id);
        Assert.NotEqual(before.ProjectionHash, after.ProjectionHash);
        var task = Assert.Single(second.Graph.Nodes, node => node.Kind == "document" && node.LocalId == "fixture:task:one");
        Assert.Equal(field == "title" ? "Revised declared behavior" : "Check declared behavior", task.Label);
    }

    [Fact]
    public void TaskStatusCheckboxAndCompletionBookkeepingReuseContextProjection()
    {
        using var fixture = VerificationFixture.Create();
        var path = Path.Combine(fixture.Repository.Path, VerificationFixture.TaskPath);
        File.AppendAllText(path, "\n## Completion evidence\n\n| Check | Result | Evidence |\n| --- | --- | --- |\n");
        var reader = fixture.Reader();
        using var scope = CisReadScope.Enter();
        var first = reader.ReadCurrent(fixture.Repository.Path);
        File.WriteAllText(path, File.ReadAllText(path).Replace("status: Draft", "status: Active", StringComparison.Ordinal)
            .Replace("task_status: NotStarted", "task_status: Completed", StringComparison.Ordinal)
            .Replace("- [ ] Preserve", "- [x] Preserve", StringComparison.Ordinal)
            + "\n| Native check | Passed | recorded-result.json |\n");
        Assert.Same(first, reader.ReadCurrent(fixture.Repository.Path));
    }

    [Fact]
    public void ChangedReportRefreshesResultFacts()
    {
        using var fixture = VerificationFixture.Create();
        var reader = fixture.Reader();
        using var scope = CisReadScope.Enter();
        var first = reader.ReadCurrent(fixture.Repository.Path);
        Assert.Equal("recorded-passed", fixture.ResultEdge().Properties["nativeOutcome"]);
        fixture.Repository.Write(VerificationFixture.ReportPath, "changed native report");
        var second = reader.ReadCurrent(fixture.Repository.Path);
        Assert.NotSame(first, second);
        Assert.Equal(first.Graph!.Build.Id, second.Graph!.Build.Id);
        Assert.Equal("unknown", fixture.ResultEdge().Properties["nativeOutcome"]);
    }

    [Fact]
    public void NativeReportChangedDuringAdapterReadCannotClaimPassed()
    {
        using var fixture = VerificationFixture.Create();
        fixture.Adapter.DuringRead = context => File.AppendAllText(context.ResultPath, "changed after initial pin assessment");
        Assert.Equal(0, CreateBuilder([fixture.Adapter]).Build(fixture.Repository.Path).ExitCode);
        Assert.Equal("unknown", fixture.ResultEdge().Properties["nativeOutcome"]);
    }

    [Fact]
    public void ReceiptAssessmentContractIsReadFromSavedJsonRatherThanComputedGetter()
    {
        using var fixture = VerificationFixture.Create();
        var absolute = Path.Combine(fixture.Repository.Path, VerificationFixture.ReceiptPath);
        File.WriteAllText(absolute, File.ReadAllText(absolute).Replace("\"assessmentContractVersion\":" + CisIncrementalResultContract.Version,
            "\"assessmentContractVersion\":9999", StringComparison.Ordinal));
        Assert.Equal(0, CreateBuilder([fixture.Adapter]).Build(fixture.Repository.Path).ExitCode);
        Assert.Equal("unsupported-receipt", fixture.ResultNode().Properties["receiptState"]);
        Assert.Equal("unknown", fixture.ResultEdge().Properties["nativeOutcome"]);
    }

    private sealed class VerificationFixture : IDisposable
    {
        internal const string Artifact = "src/Orders.Api/Feature.cs";
        internal const string TestPath = "src/Orders.Api/FeatureTests.cs";
        internal const string AcceptancePath = "docs/cis/changes/CIS-0001/acceptance.md";
        internal const string TaskPath = "docs/cis/changes/CIS-0001/agent-tasks/WORK-001.md";
        internal const string ReceiptPath = ".cis/local/testing/fixture-evidence.json";
        internal const string ReportPath = ".cis/local/testing/fixture.trx";
        private const string ProfilePath = ".cis/testing/fixture.json";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        internal TemporaryRepository Repository { get; } = TemporaryRepository.CreateInitializedApi();
        internal FixtureResultAdapter Adapter { get; } = new();
        internal IncrementalEvidenceResult Receipt { get; private set; } = null!;

        internal static VerificationFixture Create()
        {
            var fixture = new VerificationFixture();
            fixture.Repository.Write(Artifact, "namespace Example; public class Feature { public int Read() => 1; }");
            fixture.Repository.Write(TestPath, "namespace Example; public class FeatureTests { [Fact] public void ChecksFeature() { new Feature().Read(); } }");
            fixture.Repository.Write(ProfilePath, "{\"unitResultFormat\":\"trx\"}");
            fixture.Repository.Write(ReportPath, "<TestRun><Results><UnitTestResult testName=\"Example.FeatureTests.ChecksFeature\" outcome=\"Passed\" /></Results></TestRun>");
            fixture.Repository.Write(TaskPath, """
                ---
                title: Check declared behavior
                type: agent-task
                status: Draft
                task_id: WORK-001
                task_status: NotStarted
                cis:
                  stable_id: fixture:task:one
                ---

                # Check declared behavior

                ## Acceptance criteria

                - [ ] Preserve the declared behavior.
                """);
            File.AppendAllText(Path.Combine(fixture.Repository.Path, "docs/cis/catalog.yml"), """

                  - id: fixture:task:one
                    path: docs/cis/changes/CIS-0001/agent-tasks/WORK-001.md
                    type: agent-task
                    status: draft
                    authority: canonical
                  - id: fixture:acceptance
                    path: docs/cis/changes/CIS-0001/acceptance.md
                    type: verification-record
                    status: draft
                    authority: canonical
                """);
            fixture.WriteAcceptance();
            string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.Repository.Path, path)))).ToLowerInvariant();
            var stamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            var hash = new string('a', 64);
            fixture.Save(new("verified", 0)
            {
                ProfileId = "fixture", ProfilePath = ProfilePath, ProfileSha256 = Hash(ProfilePath), Layer = "unit", ScopeComplete = true, VerifiedUtc = stamp,
                Inputs = [new(Artifact, Hash(Artifact), ["source"]), new(TestPath, Hash(TestPath), ["test"]),
                    new(ProfilePath, Hash(ProfilePath), ["profile"]), new("C:/external/native-tool.dll", hash, ["producer"])],
                Closures = [new("closure", [1], [0])],
                Batches = [new(Artifact, "closure", hash, hash, hash, false, ReportPath, Hash(ReportPath),
                    new FileInfo(Path.Combine(fixture.Repository.Path, ReportPath)).Length, hash, stamp, 1,
                    null, null, null, null, null, [new("unit", "exited", 0, 1, hash, hash, false, false)])],
            });
            return fixture;
        }

        internal void WriteAcceptance(string criterion = "fixture:acceptance#AC-10", string task = "fixture:task:one", string check = TestPath + "#ChecksFeature")
            => Repository.Write(AcceptancePath, """
                ---
                title: Acceptance evidence
                type: verification-record
                status: Draft
                cis:
                  stable_id: fixture:acceptance
                ---

                # Acceptance evidence

                | ID | Criterion |
                | --- | --- |
                | AC-10 | The declared behavior is checked. |

                ## Implementation and verification

                | Criterion | Task | Component | Artifact | Check | Result reference |
                | --- | --- | --- | --- | --- | --- |
                """ + $"\n| {criterion} | {task} | orders-api | {Artifact} | {check} | {ReceiptPath} |\n");

        internal void Save(IncrementalEvidenceResult receipt)
        {
            Receipt = receipt;
            Repository.Write(ReceiptPath, JsonSerializer.Serialize(receipt, Json));
        }

        internal GraphSnapshotReader Reader() => new(new CisRepositoryContextResolver(), builder: CreateBuilder([Adapter]), resultAdapters: [Adapter]);
        internal CisGraphDocument Graph() => new SqliteGraphStore().Read(Repository.Path).Graph!;
        internal CisGraphNode ResultNode() => Assert.Single(Graph().Nodes, node => node.Subtype == "verification-result");
        internal CisGraphEdge ResultEdge() => Assert.Single(Graph().Edges, edge => edge.Properties.GetValueOrDefault("role") == "explicit-result-reference");
        public void Dispose() => Repository.Dispose();
    }

    private sealed class FixtureResultAdapter : ICisTestResultAdapter
    {
        public string Format => "trx";
        internal Action<TestResultAdapterContext>? DuringRead { get; set; }
        public TestSuiteExecution Read(TestResultAdapterContext context)
        {
            DuringRead?.Invoke(context);
            return new("fixture", "unit", "native", "passed", TestFailureKind.None, 1, 1, 0, 0, 1,
                [new("", "Example.FeatureTests.ChecksFeature", "passed", 1, null, null)], null, null, [], []);
        }
    }
}
