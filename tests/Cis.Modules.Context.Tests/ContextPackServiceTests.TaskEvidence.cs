using System.Security.Cryptography;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Context;
using Cis.Modules.Graph;
using Cis.Modules.Repository;

namespace Cis.Modules.Context.Tests;

public sealed partial class ContextPackServiceTests
{
    [Theory]
    [InlineData(999, 64_000)]
    [InlineData(1_000_001, 64_000)]
    [InlineData(16_000, 999)]
    [InlineData(16_000, 4_000_001)]
    public void Prepare_RejectsUnsupportedDeliveryBudgetsBeforeReadingGraph(int characters, int bytes)
    {
        var request = new CisTaskContextRequest("unused", "CIS-0001", "WORK-001", "task.md",
            MaxCharacters: characters, MaxUtf8Bytes: bytes);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => CreateService().Prepare(request));

        Assert.Equal("request", exception.ParamName);
    }

    [Fact]
    public void Prepare_RejectsMalformedAdvisoryRootsWithoutRequiringAGraph()
    {
        IReadOnlyList<string>?[] invalidRoots =
        [
            null, [null!], [""], [new string('a', 257)], ["../authority"], ["another/repository"],
            ["one", "two", "three", "four", "five"]
        ];
        foreach (var roots in invalidRoots)
        {
            var result = CreateService().Prepare(new("unused", "CIS-0001", "WORK-001", "task.md")
            {
                ContextRoots = roots!
            });

            Assert.Equal("unavailable", result.Status);
            Assert.Equal("unknown", result.Freshness);
            Assert.Null(result.SnapshotId);
            Assert.Null(result.DetailPath);
            Assert.Equal("CIS-TASK-CONTEXT-ROOTS-INVALID", Assert.Single(result.DiagnosticCodes));
            Assert.Contains("grants no authority", result.Content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Prepare_WithValidAdvisoryIdentifiersAndNoCurrentGraphReturnsBoundedUnavailability()
    {
        var result = CreateService().Prepare(new("unused", "CIS-0001", "WORK-001", "task.md")
        {
            ContextRoots = ["repository:task#WORK-001", "component.api-v1"]
        });

        Assert.Equal("unavailable", result.Status);
        Assert.Equal("unknown", result.Freshness);
        Assert.Null(result.SnapshotId);
        Assert.Null(result.DetailPath);
        Assert.Equal("CIS-TASK-CONTEXT-CURRENT-UNAVAILABLE", Assert.Single(result.DiagnosticCodes));
        Assert.Contains("Canonical task authority remains separate", result.Content, StringComparison.Ordinal);
        Assert.InRange(result.DeliveredCharacters, 1, result.MaxCharacters);
        Assert.InRange(result.DeliveredUtf8Bytes, result.DeliveredCharacters, result.MaxUtf8Bytes);
    }

    [Fact]
    public void Prepare_TraversesTaskCriterionCheckAndRecordedDispositionWithoutComponentCatalogFanout()
    {
        using var repository = TemporaryRepository.CreateInitialized();
        const string artifact = "src/Orders.Api/Feature.cs";
        const string test = "tests/Orders.Api.Tests/FeatureTests.cs";
        const string originalTask = "docs/cis/changes/CIS-0001/agent-tasks/WORK-001.md";
        const string activeTask = "docs/cis/changes/CIS-0001/agent-tasks/WORK-002.md";
        const string acceptance = "docs/cis/changes/CIS-0001/acceptance.md";
        const string catalog = "docs/cis/references/noise-components.md";
        const string profile = ".cis/testing/context-fixture.json";
        const string report = ".cis/local/testing/context-fixture.trx";
        const string receipt = ".cis/local/testing/context-fixture-evidence.json";
        repository.Write(artifact, "namespace Example; public class Feature { public int Read() => 1; }");
        repository.Write(test, "namespace Example; public class FeatureTests { [Fact] public void ChecksFeature() { new Feature().Read(); } }");
        repository.Write(profile, "{\"unitResultFormat\":\"trx\"}");
        repository.Write(report, "<TestRun><Results><UnitTestResult testName=\"Example.FeatureTests.ChecksFeature\" outcome=\"Passed\" /></Results></TestRun>");
        repository.Write(originalTask, "---\ntitle: Original implementation\ntype: agent-task\nstatus: Draft\ncis:\n  stable_id: fixture:task:original\n---\n# Original implementation\n");
        repository.Write(activeTask, "---\ntitle: Inspect current evidence\ntype: agent-task\nstatus: Draft\ncis:\n  stable_id: fixture:task:active\n---\n# Inspect evidence\n[Original](WORK-001.md) and [catalogue](../../../references/noise-components.md).\n");
        repository.Write(catalog, "---\ntitle: Unrelated component catalogue\ntype: component-sheet\nstatus: Draft\n---\n# Noise\n## Component catalogue\n| Component ID | Name |\n| --- | --- |\n| noise-component | Unrelated declaration |\n");
        repository.Write(acceptance, "---\ntitle: Acceptance\ntype: verification-record\nstatus: Draft\ncis:\n  stable_id: fixture:acceptance\n---\n# Acceptance\n| ID | Criterion |\n| --- | --- |\n| AC-10 | Preserve declared behavior. |\n\n## Implementation and verification\n| Criterion | Task | Component | Artifact | Check | Result reference |\n| --- | --- | --- | --- | --- | --- |\n"
            + $"| fixture:acceptance#AC-10 | fixture:task:original | orders-api | {artifact} | {test}#ChecksFeature | {receipt} |\n");
        foreach (var item in new[] { ("fixture:task:original", originalTask, "agent-task"), ("fixture:task:active", activeTask, "agent-task"),
            ("fixture:acceptance", acceptance, "verification-record"), ("fixture:noise", catalog, "component-sheet") })
        {
            File.AppendAllText(ToAbsolute(repository.Path, "docs/cis/catalog.yml"),
                $"\n  - id: {item.Item1}\n    path: {item.Item2}\n    type: {item.Item3}\n    status: draft\n    authority: canonical\n");
        }
        string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(ToAbsolute(repository.Path, path))));
        var stamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var hash = new string('a', 64);
        var evidence = new IncrementalEvidenceResult("verified", 0)
        {
            ProfileId = "fixture", ProfilePath = profile, ProfileSha256 = Hash(profile), Layer = "unit", ScopeComplete = true, VerifiedUtc = stamp,
            Inputs = [new(artifact, Hash(artifact), ["source"]), new(test, Hash(test), ["test"]), new(profile, Hash(profile), ["profile"])],
            Closures = [new("closure", [1], [0])],
            Batches = [new(artifact, "closure", hash, hash, hash, false, report, Hash(report), new FileInfo(ToAbsolute(repository.Path, report)).Length,
                hash, stamp, 1, null, null, null, null, null, [new("unit", "exited", 0, 1, hash, hash, false, false)])]
        };
        repository.Write(receipt, JsonSerializer.Serialize(evidence, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var adapter = new ContextFixtureResultAdapter();
        var resolver = new CisRepositoryContextResolver();
        var builder = CreateBuilder([adapter]);
        var snapshots = new GraphSnapshotReader(resolver, builder: builder, resultAdapters: [adapter]);
        var service = new ContextPackService(new GraphQueryService(resolver, resultAdapters: [adapter]), snapshots);

        var prepared = service.Prepare(new(repository.Path, "CIS-0001", "WORK-002", activeTask)
        {
            ContextRoots = ["fixture:task:original"]
        });

        Assert.NotEqual("unavailable", prepared.Status);
        Assert.Contains("recorded-passed", prepared.Content, StringComparison.Ordinal);
        Assert.Contains("requires-current-native-verification", prepared.Content, StringComparison.Ordinal);
        Assert.Contains(receipt, prepared.Content, StringComparison.Ordinal);
        Assert.True(prepared.DeliveredCharacters <= 16_000);
        Assert.True(prepared.DeliveredUtf8Bytes <= 64_000);
        using var details = JsonDocument.Parse(File.ReadAllText(ToAbsolute(repository.Path, prepared.DetailPath!)));
        Assert.Single(details.RootElement.GetProperty("verificationFacts").EnumerateArray());
        Assert.DoesNotContain(details.RootElement.GetProperty("sources").EnumerateArray()
            .SelectMany(source => source.GetProperty("nodeKeys").EnumerateArray().Select(node => node.GetString())),
            key => key?.EndsWith("::component::noise-component", StringComparison.Ordinal) == true);
        var graph = new SqliteGraphStore().Read(repository.Path).Graph!;
        var active = Assert.Single(graph.Nodes, node => node.LocalId == "fixture:task:active");
        var criterion = Assert.Single(graph.Nodes, node => node.LocalId == "fixture:acceptance#AC-10");
        Assert.DoesNotContain(graph.Edges, edge => edge.From == criterion.Key && edge.To == active.Key);

        var unresolved = service.Prepare(new(repository.Path, "CIS-0001", "WORK-002", activeTask)
        {
            ContextRoots = ["fixture:task:missing"]
        });
        Assert.Equal("unavailable", unresolved.Status);
        Assert.Contains("CIS-TASK-CONTEXT-ROOT-UNRESOLVED", unresolved.DiagnosticCodes);

        var legacy = service.Create(new(repository.Path, "fixture:task:active", "document", "Legacy pack", "Checks", 4, 80, 15_000, false, null, true));
        Assert.Null(legacy.VerificationFacts);
    }

    private sealed class ContextFixtureResultAdapter : ICisTestResultAdapter
    {
        public string Format => "trx";
        public TestSuiteExecution Read(TestResultAdapterContext context)
            => new("fixture", "unit", "native", "passed", TestFailureKind.None, 1, 1, 0, 0, 1,
                [new("", "Example.FeatureTests.ChecksFeature", "passed", 1, null, null)], null, null, [], []);
    }
}
