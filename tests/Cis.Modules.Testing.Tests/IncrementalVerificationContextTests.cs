using System.Security.Cryptography;
using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class IncrementalVerificationContextTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerificationLinksDoNotTurnTaskBookkeepingIntoNativeInputsButExplicitFixturesRemainMaterial(bool explicitlyConsumed)
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        fixture.Save(fixture.Profile with
        {
            MutationCommand = null,
            RecoveryCommand = null,
            MaxRunBytes = 4 * 1024 * 1024,
            MaxRetainedBytes = 32 * 1024 * 1024
        });
        fixture.Write("docs/task.md", "task_status: InProgress\nOriginal completion observations.");
        if (explicitlyConsumed)
            fixture.Save(fixture.Profile with { ConfigFiles = [.. fixture.Profile.ConfigFiles, "docs/task.md"] });
        var authority = new IncrementalNativeWorkTests.UnitInvocationAuthority(fixture.Path);
        var service = new IncrementalTestService(new VerificationReader(fixture), authority, authority);
        var options = new IncrementalTestOptions(fixture.Path, "profile.json", "unit", 4, 120);
        var first = service.Run(options, 0, TestContext.Current.CancellationToken);
        Assert.True(first.ExitCode == 0, string.Join("; ", first.Diagnostics));
        Assert.Equal(2, first.ExecutedUnitBatches);
        if (!explicitlyConsumed)
            Assert.All(first.Batches, batch => Assert.DoesNotContain("docs/task.md", batch.DependencyFiles));
        var calls = fixture.Calls();

        fixture.Write("docs/task.md", "task_status: Complete\nUpdated result reference and completion observations.");
        var after = service.Run(options, 0, TestContext.Current.CancellationToken);

        Assert.True(after.ExitCode == 0, string.Join("; ", after.Diagnostics));
        Assert.Equal(explicitlyConsumed ? 2 : 0, after.ExecutedUnitBatches);
        Assert.Equal(explicitlyConsumed ? 0 : 2, after.ReusedUnitBatches);
        Assert.Equal(calls.Length + (explicitlyConsumed ? 2 : 0), fixture.Calls().Length);
        Assert.Contains("src/Shared.cs", after.Batches.Single(batch => batch.SourceFile == "src/A.cs").DependencyFiles);
        Assert.Equal(0, service.Verify(new(fixture.Path, "profile.json") { Layer = "unit" }).ExitCode);
    }

    [Theory]
    [InlineData("references", true)]
    [InlineData("depends-on", false)]
    public void ContextualMetadataCannotEraseAnIndependentNativeDependency(string type, bool mixedObservations)
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        fixture.Save(fixture.Profile with
        {
            MutationCommand = null,
            RecoveryCommand = null,
            MaxRunBytes = 4 * 1024 * 1024,
            MaxRetainedBytes = 32 * 1024 * 1024
        });
        fixture.Write("docs/task.md", "An explicitly observed native dependency.");
        var reader = new VerificationReader(fixture, type, mixedObservations);
        var authority = new IncrementalNativeWorkTests.UnitInvocationAuthority(fixture.Path);
        var service = new IncrementalTestService(reader, authority, authority);
        var options = new IncrementalTestOptions(fixture.Path, "profile.json", "unit", 4, 120);
        var first = service.Run(options, 0, TestContext.Current.CancellationToken);
        Assert.True(first.ExitCode == 0, string.Join("; ", first.Diagnostics));
        Assert.Contains("docs/task.md", first.Batches.Single(batch => batch.SourceFile == "src/A.cs").DependencyFiles);

        fixture.Write("docs/task.md", "The actual native dependency changed.");
        var changed = service.Run(options, 0, TestContext.Current.CancellationToken);
        Assert.Equal(0, changed.ExitCode);
        Assert.Equal(1, changed.ExecutedUnitBatches);
        Assert.Equal(1, changed.ReusedUnitBatches);
    }

    private sealed class VerificationReader(IncrementalTestServiceTests.Fixture fixture,
        string? nativeEdgeType = null, bool mixedObservations = false) : ICisGraphSnapshotReader
    {
        public CisGraphSnapshotReadResult Read(string repositoryPath)
        {
            var snapshot = fixture.Read(repositoryPath);
            var location = new CisGraphLocation("docs/task.md", "line", "1");
            var document = new CisGraphNode("task", "sample", "document", "agent-task", "task", "Task",
                [], "derived", "active", [location], new Dictionary<string, string>(), []);
            var result = document with { Key = "recorded", Kind = "reference-item", Subtype = "verification-result", LocalId = "recorded" };
            var criterion = document with { Key = "criterion", Kind = "requirement", Subtype = "acceptance-criterion", LocalId = "criterion" };
            var observation = new CisGraphEvidence("canonical-markdown", "docs/task.md", "line", "1", null,
                "explicit-verification-declaration", "cis.markdown.verification-map/1", "high", null);
            CisGraphEdge Edge(string from, string to, string role) => new(from + "->" + to, "references", from, to,
                "declared", "high", [observation], new Dictionary<string, string> { ["role"] = role, ["mapping"] = "explicit" });
            var links = new List<CisGraphEdge>
            {
                Edge("at", "recorded", "explicit-result-reference"), Edge("criterion", "task", "implementing-task"),
                Edge("task", "a", "declared-implementation")
            };
            if (nativeEdgeType is not null)
            {
                var native = observation with { SourceKind = "repository-source", Path = "src/A.cs", Method = "compiler-binding" };
                links.Add(Edge("a", "task", "native-dependency") with
                {
                    Type = nativeEdgeType,
                    Observations = mixedObservations ? [observation, native] : [observation]
                });
            }
            return snapshot with
            {
                Graph = snapshot.Graph! with
                {
                    Nodes = [.. snapshot.Graph.Nodes, document, result, criterion],
                    Edges = [.. snapshot.Graph.Edges, .. links]
                },
                Manifest = snapshot.Manifest! with
                {
                    Inputs = [.. snapshot.Manifest.Inputs,
                        new("docs/task.md", Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(fixture.Full("docs/task.md")))))]
                }
            };
        }
    }
}
