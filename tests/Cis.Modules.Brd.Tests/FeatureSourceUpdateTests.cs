using Cis.Abstractions;
using Cis.Host;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    private static CisFeatureSourceUpdateResult Reimport(Fixture f, bool preview = true, string? hash = null, bool confirmed = true)
        => f.Service.UpdateSource(f.Authority, "referrals", f.Request.SourcePath, "Source reviewer", preview, confirmed, hash);

    [Fact]
    public void ReimportPreviewAndIdenticalSourceAreReadOnlyAndRequireExplicitPlan()
    {
        using var f = new Fixture(); CreateIntake(f);
        var before = f.Snapshot();
        Assert.Equal("unchanged", Reimport(f).Status);
        Assert.Equal(before, f.Snapshot());
        File.AppendAllText(f.Request.SourcePath, "\nNew requirement.\n");
        var preview = Reimport(f);
        Assert.Empty(preview.Errors); Assert.Equal("preview", preview.Status);
        Assert.Equal(before, f.Snapshot());
        Assert.Equal(3, Reimport(f, false, confirmed: false).ExitCode);
        Assert.Equal("stale-preview", Reimport(f, false).Status);
        Assert.Equal(before, f.Snapshot());
        Assert.True(Reimport(f, false, preview.Plan!.PlanHash).Applied);
        before = f.Snapshot();
        Assert.Equal("unchanged", Reimport(f, false, preview.Plan.PlanHash).Status);
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public void ReimportRemapsAnswersByQuestionRetainsWorkAndHistoryAndRequiresRenewedReview()
    {
        using var f = new Fixture(); CreateIntake(f);
        var status = f.Service.Wizard(f.Authority, "referrals");
        foreach (var page in status.Pages.Where(page => page.Id != "foundation"))
        {
            var answer = new CisFeatureWizardAnswer("referrals", page.Id,
                page.Fields.ToDictionary(field => field.Id, field => "Human answer: " + field.Label), "Owner", status.Revision!);
            if (page.Id == "delivery") answer = answer with { RepositoryWork = [new("API", "referrals", "Referral API", "Issue referral codes.", [], [])] };
            status = f.Service.SaveWizard(f.Authority, answer);
            Assert.Empty(status.Errors);
        }
        Assert.True(status.Reviewed);
        var request = Path.Combine(f.Authority, status.Plan!.RequestPath);
        File.AppendAllText(request, "\n## Owner notes\n\nPreserve this manually written scope note.\n");
        var oldRequest = File.ReadAllBytes(request);
        var oldSource = File.ReadAllBytes(Path.Combine(f.Authority, status.Plan.SourcePath));
        var registry = File.ReadAllBytes(f.Registry.Resolve(f.Authority).Workspace!.ConfigurationPath);
        var approved = File.ReadAllBytes(f.ApprovedBacklog);
        File.WriteAllText(f.Request.SourcePath, "# Updated BRD\n\n## Scope\nNew requirements.\n\n## Open decisions\n1. Is direct handoff recorded?\n2. New question?\n3. Who owns support?\n");
        var preview = Reimport(f);
        Assert.Empty(preview.Errors); Assert.Equal(2, preview.Plan!.RetainedDecisionAnswers);
        Assert.Equal(["New question?"], preview.Plan.AddedDecisions);
        Assert.Empty(preview.Plan.RemovedDecisions);
        Assert.True(Reimport(f, false, preview.Plan.PlanHash).Applied);
        var reopened = f.Service.Wizard(f.Authority, "referrals");
        Assert.Empty(reopened.Errors); Assert.False(reopened.Reviewed); Assert.True(reopened.Pages[0].Complete);
        Assert.All(reopened.Pages.Skip(1), page => Assert.False(page.Complete));
        var fields = reopened.Pages.Single(page => page.Id == "business").Fields;
        Assert.Equal("Human answer: Is direct handoff recorded?", fields.Single(field => field.Id == "decision-001").Answer);
        Assert.Null(fields.Single(field => field.Id == "decision-002").Answer);
        Assert.Equal("Human answer: Who owns support?", fields.Single(field => field.Id == "decision-003").Answer);
        Assert.Single(reopened.RepositoryWork); Assert.Equal("API", reopened.RepositoryWork[0].Id);
        Assert.Equal(2, reopened.SourceHistory.Count);
        Assert.Equal(oldRequest, File.ReadAllBytes(Path.Combine(f.Authority, preview.Plan.PreviousRequestPath)));
        Assert.Equal(oldSource, File.ReadAllBytes(Path.Combine(f.Authority, preview.Plan.PreviousSourcePath)));
        Assert.Equal(File.ReadAllBytes(f.Request.SourcePath), File.ReadAllBytes(Path.Combine(f.Authority, reopened.Plan!.SourcePath)));
        Assert.Equal(registry, File.ReadAllBytes(f.Registry.Resolve(f.Authority).Workspace!.ConfigurationPath));
        Assert.Equal(approved, File.ReadAllBytes(f.ApprovedBacklog));
        Assert.Contains("Preserve this manually written scope note.", File.ReadAllText(request));
        Assert.Single(f.Service.Navigation(f.Authority).Features);
        // A subsequent reviewed save works against the new source and keeps the work breakdown.
        var saved = f.Service.SaveWizard(f.Authority, new("referrals", "business",
            fields.ToDictionary(field => field.Id, field => field.Answer ?? "Explicit new answer."), "Owner", reopened.Revision!));
        Assert.Empty(saved.Errors); Assert.True(saved.Pages.Single(page => page.Id == "business").Complete);
        Assert.Single(saved.RepositoryWork);
    }

    [Fact]
    public void RemovedAndAmbiguousQuestionsCannotAcquireAnotherQuestionsAnswer()
    {
        using var f = new Fixture(); CreateIntake(f);
        var status = f.Service.Wizard(f.Authority, "referrals");
        status = f.Service.SaveWizard(f.Authority, new("referrals", "business", new()
        { ["summary"] = "Feature scope.", ["decision-001"] = "Support team.", ["decision-002"] = "Yes, before redirect." }, "Owner", status.Revision!));
        File.WriteAllText(f.Request.SourcePath, "# Updated\n## Open decisions\n1. Is direct handoff recorded?\n2. Is direct handoff recorded?\n3. Replacement question?\n");
        var preview = Reimport(f);
        Assert.Empty(preview.Errors); Assert.Equal(0, preview.Plan!.RetainedDecisionAnswers);
        Assert.Equal(["Who owns support?"], preview.Plan.RemovedDecisions);
        Assert.True(Reimport(f, false, preview.Plan.PlanHash).Applied);
        var after = f.Service.Wizard(f.Authority, "referrals");
        Assert.Empty(after.Errors);
        Assert.All(after.Pages.Single(page => page.Id == "business").Fields.Skip(1), field => Assert.Null(field.Answer));
        var history = File.ReadAllText(Path.Combine(f.Authority, preview.Plan.PreviousRequestPath));
        Assert.Contains("Support team.", history); Assert.Contains("Yes, before redirect.", history);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("request")]
    [InlineData("baseline")]
    public void ReimportRejectsStalePreviewWithoutChangingCurrentSource(string changed)
    {
        using var f = new Fixture(); CreateIntake(f);
        File.AppendAllText(f.Request.SourcePath, "\nNew requirements.\n");
        var preview = Reimport(f);
        var status = f.Service.Wizard(f.Authority, "referrals");
        var file = changed == "source" ? f.Request.SourcePath : changed == "request"
            ? Path.Combine(f.Authority, status.Plan!.RequestPath)
            : Path.Combine(f.Authority, "docs/cis/specs/technical-intent-spec.md");
        File.AppendAllText(file, "\nConcurrent edit.\n");
        var before = f.Snapshot();
        Assert.Equal("stale-preview", Reimport(f, false, preview.Plan!.PlanHash).Status);
        Assert.Equal(before, f.Snapshot());
        Assert.Empty(f.Service.Wizard(f.Authority, "referrals").Errors);
    }

    [Fact]
    public void ReimportCanReturnToAnEarlierSourceWithoutRevivingOldReview()
    {
        using var f = new Fixture(); CreateIntake(f);
        var original = File.ReadAllBytes(f.Request.SourcePath);
        File.AppendAllText(f.Request.SourcePath, "\nRevision two.\n");
        var first = Reimport(f); Assert.True(Reimport(f, false, first.Plan!.PlanHash).Applied);
        File.WriteAllBytes(f.Request.SourcePath, original);
        var second = Reimport(f); Assert.True(Reimport(f, false, second.Plan!.PlanHash).Applied);
        var status = f.Service.Wizard(f.Authority, "referrals");
        Assert.Empty(status.Errors); Assert.Equal(4, status.SourceHistory.Count); Assert.False(status.Reviewed);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(f.Authority, status.Plan!.SourcePath)));
    }

    [Fact]
    public void IncompleteRevisionWriteLeavesPreviousRequestUsableAndProtectsExternalSourceFields()
    {
        using var f = new Fixture(); CreateIntake(f);
        File.AppendAllText(f.Request.SourcePath, "\nNew requirements.\n");
        var preview = Reimport(f);
        var destination = Path.Combine(f.Authority, preview.Plan!.SourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, "Interrupted or tampered revision");
        var before = f.Snapshot();
        Assert.NotEmpty(Reimport(f, false, preview.Plan.PlanHash).Errors);
        Assert.Equal(before, f.Snapshot()); Assert.Empty(f.Service.Wizard(f.Authority, "referrals").Errors);
        var request = Path.Combine(f.Authority, f.Service.Wizard(f.Authority, "referrals").Plan!.RequestPath);
        File.WriteAllText(request, File.ReadAllText(request).Replace("## Open decisions from the BRD", "## Human edited questions", StringComparison.Ordinal));
        before = f.Snapshot(); Assert.NotEmpty(Reimport(f).Errors); Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public void ReimportCommandRegistersAndDispatchesReadOnlyPreview()
    {
        using var f = new Fixture(); CreateIntake(f);
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new WorkspaceModule())
            .AddModule(new DocsModule()).AddModule(new GraphModule()).AddModule(new BrdModule()).Build();
        Assert.Equal(0, application.Invoke(["brd", "feature", "wizard", "reimport", "--help"]));
        File.AppendAllText(f.Request.SourcePath, "\nUpdated requirement.\n");
        var request = Path.Combine(f.Authority, f.Service.Wizard(f.Authority, "referrals").Plan!.RequestPath);
        var original = File.ReadAllBytes(request);
        Assert.Equal(0, application.Invoke(["brd", "feature", "wizard", "reimport", "--slug", "referrals",
            "--source", f.Request.SourcePath, "--actor", "Reviewer", "--workspace", f.Authority, "--dry-run", "--format", "json"]));
        Assert.Equal(original, File.ReadAllBytes(request));
    }
}
