using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class IncrementalGraphWarningTests
{
    [Theory]
    [InlineData("CIS-GRAPH-DOC-002")]
    [InlineData("CIS-GRAPH-VERIFY-001")]
    [InlineData("CIS-GRAPH-VERIFY-002")]
    public void DocumentationAndVerificationMappingWarningsRemainVisibleAndPreserveUnchangedNativeEvidence(string code)
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var reader = new DiagnosticReader(fixture);
        var service = new IncrementalTestService(reader);
        var options = new IncrementalTestOptions(fixture.Path, "profile.json", "unit", 4, 120);
        var first = service.Run(options, 0, TestContext.Current.CancellationToken);
        Assert.Equal(0, first.ExitCode);
        Assert.Equal(2, first.ExecutedUnitBatches);
        var nativeCalls = fixture.Calls();

        fixture.Write("docs/unrelated.md", "Documentation outside native material inputs.");
        reader.DiagnosticCode = code;
        var unchanged = service.Run(options, 0, TestContext.Current.CancellationToken);

        Assert.Equal(0, unchanged.ExitCode);
        Assert.Equal(0, unchanged.ExecutedUnitBatches);
        Assert.Equal(2, unchanged.ReusedUnitBatches);
        Assert.Equal(nativeCalls, fixture.Calls());
        Assert.All(unchanged.Batches, batch => Assert.Equal("graph", batch.Selection));
        Assert.Contains(unchanged.Diagnostics, message => message.Contains(code, StringComparison.Ordinal));
        Assert.True(reader.CurrentReads > 0);
        reader.DiagnosticCode = null;
        var afterPublication = service.Run(options, 0, TestContext.Current.CancellationToken);
        Assert.Equal(0, afterPublication.ExecutedUnitBatches);
        Assert.Equal(2, afterPublication.ReusedUnitBatches);
        Assert.Equal(nativeCalls, fixture.Calls());
    }

    [Theory]
    [InlineData("CIS-GRAPH-CSHARP-001")]
    [InlineData("CIS-GRAPH-FUTURE-001")]
    [InlineData("missing-details")]
    [InlineData("partial-without-details")]
    public void UncertainDependenciesSelectTheConfiguredFullScopeInsteadOfEmptyConfidence(string fault)
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var reader = new DiagnosticReader(fixture) { DiagnosticCode = fault };
        var result = new IncrementalTestService(reader).Plan(new(fixture.Path, "profile.json", "unit"));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, result.Batches.Count);
        Assert.All(result.Batches, batch =>
        {
            Assert.Equal("all-tests", batch.Selection);
            Assert.Equal(fixture.Profile.FullTestFilter, batch.TestFilter);
            Assert.Equal(2, batch.TestFiles.Length);
            Assert.Contains("src/Shared.cs", batch.DependencyFiles);
        });
        Assert.Contains(result.Diagnostics, message => message.Contains("configured full test filter", StringComparison.Ordinal));
        Assert.Empty(fixture.Calls());
    }

    [Fact]
    public void WarningInADocumentThatIsAnExplicitNativeInputStillInvalidatesItsEvidence()
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        fixture.Write("docs/fixture.md", "original fixture");
        fixture.Save(fixture.Profile with { ConfigFiles = [.. fixture.Profile.ConfigFiles, "docs/fixture.md"] });
        var reader = new DiagnosticReader(fixture) { DiagnosticCode = "CIS-GRAPH-DOC-002" };
        var service = new IncrementalTestService(reader);
        var options = new IncrementalTestOptions(fixture.Path, "profile.json", "unit", 4, 120);
        Assert.Equal(0, service.Run(options, 0, TestContext.Current.CancellationToken).ExitCode);

        fixture.Write("docs/fixture.md", "changed native fixture");
        var changed = service.Plan(options);

        Assert.All(changed.Batches, batch => Assert.False(batch.UnitCurrent));
        Assert.All(changed.Batches, batch => Assert.Contains(batch.UnitReasons,
            reason => reason.Input == "docs/fixture.md" && reason.Change == "changed"));
    }

    private sealed class DiagnosticReader(IncrementalTestServiceTests.Fixture fixture) : ICisGraphSnapshotReader
    {
        public string? DiagnosticCode { get; set; }
        public int CurrentReads { get; private set; }

        public CisGraphSnapshotReadResult ReadCurrent(string repositoryPath)
        {
            CurrentReads++;
            return Read(repositoryPath);
        }

        public CisGraphSnapshotReadResult Read(string repositoryPath)
        {
            var original = fixture.Read(repositoryPath);
            if (DiagnosticCode is null) return original;
            var warnings = DiagnosticCode == "partial-without-details" ? 0 : 1;
            return original with
            {
                Graph = original.Graph! with
                {
                    Build = original.Graph.Build with { Status = "partial" },
                    DiagnosticSummary = new(0, warnings, 0)
                },
                Diagnostics = DiagnosticCode is "missing-details" or "partial-without-details" ? []
                    : [new(DiagnosticCode, "warning", "A diagnostic remains visible for docs/unrelated.md.", [])]
            };
        }
    }
}
