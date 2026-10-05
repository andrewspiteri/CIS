using Cis.Modules.Repository;
using Xunit;

namespace Cis.Modules.Brd.Tests;

public sealed partial class BrdWorkflowTests
{
    [Fact]
    public void ApprovedBacklogAppearsAsFeaturesAndOpensOneProductScopedWizardIdempotently()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        environment.Service.Initialize(environment.Authority.Path, "Product BRD");
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        File.WriteAllText(environment.CanonicalPath, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(environment.CanonicalPath),
            "(?ms)^## Functional requirements\\s*$.*?(?=^## )",
            "## Functional requirements\n\n| ID | Requirement | Priority | Acceptance intent |\n| --- | --- | --- | --- |\n| BRD-FR-001 | Record market events. | Must | Retain original event identifiers. |\n| BRD-FR-002 | Produce unrelated finance reports. | Must | Export the report. |\n\n"));
        File.WriteAllText(environment.CanonicalPath, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(environment.CanonicalPath),
            "(?ms)^## Constraints and assumptions\\s*$.*?(?=^## )",
            "## Constraints and assumptions\n\nMarket event timestamps must remain in UTC.\n\n"));
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Owner", "Reviewed requirements.").ExitCode);
        var backlog = new BrdBacklogService(environment.Service, environment.Registry,
            new CisRepositoryContextResolver(), new DocumentationCatalogMerger(), [new ReadyCheck()]);
        Assert.Equal(0, backlog.Build(environment.Authority.Path).ExitCode);
        var baseline = new MutableProductDefinitionAuthority { Active = true, BaselineHash = "sha256:product" };
        var intake = new FeatureIntakeService(environment.Registry, new RepositoryImporter(new RepositoryInitializer(), environment.Registry),
            new DocumentationCatalogMerger(), [baseline], backlog: backlog);
        var pending = intake.Navigation(environment.Authority.Path);
        Assert.NotEmpty(pending.BacklogFeatures);
        Assert.All(pending.BacklogFeatures, feature => Assert.False(feature.CanStart));
        Assert.Empty(intake.Requests(environment.Authority.Path));
        Assert.Equal(0, backlog.Approve(environment.Authority.Path, "Owner", "Reviewed delivery scope.").ExitCode);
        var navigation = intake.Navigation(environment.Authority.Path);
        var item = navigation.BacklogFeatures.First(feature => feature.CanStart);
        var originalBrd = File.ReadAllBytes(environment.CanonicalPath);
        var backlogPath = Path.Combine(environment.Authority.Path, "docs/plans/high-level-backlog.md");
        var originalBacklog = File.ReadAllBytes(backlogPath);
        Assert.NotEqual(0, intake.StartBacklogFeature(environment.Authority.Path, item.Id, "Owner", "stale").ExitCode);
        Assert.Empty(intake.Requests(environment.Authority.Path));
        baseline.Active = false;
        Assert.NotEqual(0, intake.StartBacklogFeature(environment.Authority.Path, item.Id, "Owner", item.InputHash).ExitCode);
        baseline.Active = true;
        var started = intake.StartBacklogFeature(environment.Authority.Path, item.Id, "Owner", item.InputHash);
        Assert.True(started.ExitCode == 0, string.Join("; ", started.Errors));
        Assert.Equal(item.Id, started.Plan!.BacklogItemId);
        Assert.Equal("product", started.Plan.RepositoryMode);
        Assert.Empty(started.Plan.IntegrationRepositories);
        var wizard = intake.Wizard(environment.Authority.Path, started.Plan.Slug);
        Assert.Empty(wizard.Errors);
        Assert.True(wizard.Pages.Single(page => page.Id == "foundation").Complete);
        Assert.False(wizard.Reviewed);
        Assert.Empty(wizard.RepositoryWork);
        var sourceBrd = Assert.Single(wizard.SourceBrds);
        Assert.Equal(new[] { item.RequirementId }, sourceBrd.RequirementIds);
        Assert.Equal(originalBrd, File.ReadAllBytes(Path.Combine(environment.Authority.Path, sourceBrd.Path)));
        Assert.Contains("Record market events", wizard.Description);
        Assert.Contains("Retain original event identifiers", wizard.Description);
        Assert.DoesNotContain("unrelated finance reports", wizard.Description);
        Assert.Contains("timestamps must remain in UTC", wizard.PlanningContext);
        var stories = wizard.Pages.Single(page => page.Id == "delivery").Fields.Single(field => field.Id == "delivery-stories-mvp").SuggestedAnswer;
        Assert.Contains("Record market events", stories);
        Assert.Contains("Retain original event identifiers", stories);
        Assert.DoesNotContain("unrelated finance reports", stories);
        Assert.Equal(originalBrd, File.ReadAllBytes(environment.CanonicalPath));
        Assert.Equal(originalBacklog, File.ReadAllBytes(backlogPath));
        var after = intake.Navigation(environment.Authority.Path);
        Assert.DoesNotContain(after.BacklogFeatures, feature => feature.Id == item.Id);
        Assert.Single(after.Features);
        Assert.False(intake.StartBacklogFeature(environment.Authority.Path, item.Id, "Owner", item.InputHash).Applied);
        Assert.Single(intake.Requests(environment.Authority.Path));
        File.WriteAllText(environment.CanonicalPath, File.ReadAllText(environment.CanonicalPath).Replace("timestamps must remain in UTC", "timestamps use local time"));
        Assert.Contains("timestamps must remain in UTC", intake.Wizard(environment.Authority.Path, started.Plan.Slug).PlanningContext);
        var retainedPath = Path.Combine(environment.Authority.Path, sourceBrd.Path);
        File.AppendAllText(retainedPath, "\nChanged retained evidence\n");
        Assert.Contains(intake.Wizard(environment.Authority.Path, started.Plan.Slug).Errors, error => error.Contains("linked source BRD has changed"));
    }
}
