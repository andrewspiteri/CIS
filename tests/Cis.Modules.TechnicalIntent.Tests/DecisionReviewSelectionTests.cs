using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Graph;
using Xunit;

namespace Cis.Modules.TechnicalIntent.Tests;

public sealed partial class TechnicalIntentWorkflowTests
{
    [Fact]
    public void DecisionSaveRejectsAnEquivalentSelectedDocumentSwitchAfterItsLatestStateFence()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        Assert.Equal(0, environment.Intent.Initialize(environment.Authority.Path).ExitCode);
        var originalRelative = Path.GetRelativePath(environment.Authority.Path, environment.TechnicalIntentPath).Replace('\\', '/');
        const string equivalentRelative = "docs/specs/equivalent-technical-intent.md";
        var equivalentPath = Path.Combine(environment.Authority.Path, equivalentRelative);
        var selectionPath = Path.Combine(environment.Authority.Path, CisProductDocumentPaths.SelectionFile);
        File.WriteAllText(selectionPath, JsonSerializer.Serialize(new Dictionary<string, string> { ["technical"] = originalRelative }));
        var before = File.ReadAllBytes(environment.TechnicalIntentPath);
        File.WriteAllBytes(equivalentPath, before);
        var decision = environment.Intent.Status(environment.Authority.Path).Validation!.Decisions.First();
        var service = new TechnicalIntentService(environment.Registry, environment.Resolver,
            new GraphSnapshotReader(environment.Resolver), environment.Brd, environment.Merger, () =>
            {
                File.WriteAllText(selectionPath, JsonSerializer.Serialize(new Dictionary<string, string> { ["technical"] = equivalentRelative }));
                return DateTimeOffset.UtcNow;
            });
        var result = service.ResolveDecision(environment.Authority.Path,
            new(decision.Id, "Retain the reviewed current product boundaries.", null, decision.ReviewToken!, "Fixture owner"));
        Assert.Equal("conflict", result.Status);
        Assert.False(result.Applied);
        Assert.Equal(before, File.ReadAllBytes(environment.TechnicalIntentPath));
        Assert.Equal(before, File.ReadAllBytes(equivalentPath));
    }
}
