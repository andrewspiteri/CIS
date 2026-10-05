using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    private const string ImportedTechnicalDirection = """
        ---
        status: Active
        ---
        # Technical intent
        ## 3. Implementation stack baseline
        Use C# and .NET 10, Windows Services and PostgreSQL 18.
        ## 15. Trusted-local API and configuration
        Bind APIs to loopback only. No application login or user-role system is implemented.
        ## 17. Observability and diagnosis
        Record capture gaps and spool capacity; retain independent service health and retry recovery.
        """;

    private static string SelectTechnicalDocument(Fixture fixture, string text)
    {
        const string relative = "docs/cis/imports/technical/selected.md";
        var path = Path.Combine(fixture.Authority, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        File.WriteAllText(Path.Combine(fixture.Authority, CisProductDocumentPaths.SelectionFile),
            JsonSerializer.Serialize(new Dictionary<string, string> { ["technical"] = relative }));
        return path;
    }

    [Fact]
    public void FeatureInheritsSelectedTechnicalIntentWithoutRecordingHumanAnswersAndRetainsOverridesOnDrift()
    {
        using var f = new Fixture(); CreateIntake(f);
        var selected = SelectTechnicalDocument(f, ImportedTechnicalDirection);
        File.WriteAllText(Path.Combine(f.Authority, "docs/cis/specs/technical-intent-spec.md"),
            "# Unused scaffold\n## Security and privacy\n- TODO: define access\n## Operations\n- TODO: define recovery\n");
        var before = f.Snapshot();
        var status = f.Service.Wizard(f.Authority, "referrals");
        var technical = status.Pages.Single(page => page.Id == "technical");
        Assert.Empty(status.Errors); Assert.True(technical.Complete); Assert.Equal("Inherited", technical.Status);
        Assert.Equal(3, technical.Fields.Count(field => field.Inherited));
        Assert.All(technical.Fields, field => { Assert.False(field.Required); Assert.Null(field.Answer); Assert.DoesNotContain("TODO", field.SuggestedAnswer); });
        Assert.Contains(".NET 10", technical.Fields.Single(field => field.Id == "technical-platform").SuggestedAnswer);
        Assert.Contains("No application login", technical.Fields.Single(field => field.Id == "technical-security").SuggestedAnswer);
        Assert.Contains("capture gaps", technical.Fields.Single(field => field.Id == "technical-operations").SuggestedAnswer);
        Assert.Equal("docs/cis/imports/technical/selected.md", Assert.Single(technical.Documents).Path);
        Assert.Equal(before, f.Snapshot()); Assert.False(status.Reviewed);
        f.Baseline.Active = false;
        technical = f.Service.Wizard(f.Authority, "referrals").Pages.Single(page => page.Id == "technical");
        Assert.False(technical.Complete); Assert.DoesNotContain(technical.Fields, field => field.Inherited);
        f.Baseline.Active = true;
        var saved = f.Service.SaveWizard(f.Authority, new("referrals", "technical",
            new() { ["technical-platform"] = "Use the approved stack with an additional bounded worker." }, "Reviewer", status.Revision!));
        Assert.Empty(saved.Errors);
        File.AppendAllText(selected, "\n## Deployment\nUse a new deployment package.\n");
        var stale = f.Service.Wizard(f.Authority, "referrals").Pages.Single(page => page.Id == "technical");
        Assert.False(stale.Complete);
        Assert.Contains("additional bounded worker", stale.Fields.Single(field => field.Id == "technical-platform").Answer!);
        Assert.Contains(stale.Attention, text => text.Contains("baseline"));
    }

    [Fact]
    public void PlaceholderAndFeatureSpecificTechnicalChoicesCannotBeMistakenForInheritedDecisions()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Request.SourcePath, "# Feature BRD\n## Runtime\nTODO: decide whether the feature needs a different runtime.\n");
        CreateIntake(f); SelectTechnicalDocument(f, ImportedTechnicalDirection);
        var page = f.Service.Wizard(f.Authority, "referrals").Pages.Single(page => page.Id == "technical");
        var platform = page.Fields.Single(field => field.Id == "technical-platform");
        Assert.True(platform.Required); Assert.False(platform.Inherited); Assert.False(page.Complete);
        var current = f.Service.Wizard(f.Authority, "referrals");
        var placeholder = f.Service.SaveWizard(f.Authority, new("referrals", "technical",
            new() { ["technical-platform"] = "Proposed direction:\n- TODO: choose the runtime." }, "Reviewer", current.Revision!));
        Assert.Empty(placeholder.Errors);
        Assert.False(placeholder.Pages.Single(item => item.Id == "technical").Complete);
        SelectTechnicalDocument(f, "# Selected draft\n## Security\nTODO: decide access\n## Runtime\nTBD\n");
        page = f.Service.Wizard(f.Authority, "referrals").Pages.Single(page => page.Id == "technical");
        Assert.DoesNotContain(page.Fields, field => field.Inherited);
        Assert.All(page.Fields, field => Assert.DoesNotContain("TODO", field.SuggestedAnswer));
    }
}
