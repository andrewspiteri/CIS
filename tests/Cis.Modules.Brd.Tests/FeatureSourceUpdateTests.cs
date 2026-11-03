using Cis.Abstractions;
using Cis.Host;
using System.Text.Json;

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
        Assert.Contains(after.Pages.Single(page => page.Id == "business").Attention, message => message.Contains("ambiguous", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceReorderingPreservesReviewsAndUnrelatedTechnicalAdditionRetainsDecisionsAndProvenance()
    {
        using var f = new Fixture();
        var metadata = "---\nstatus: Active\nlast_reviewed: 2026-11-01\ncis:\n  approved_by: First source reviewer\n  approved_at: 2026-11-01\n  approval_reason: Source reviewed\n  approved_content_hash: old-digest\n---\n\n";
        File.WriteAllText(f.Request.SourcePath, metadata + "# Feature\n\n## Requirements\n\nFR-001 Preserve the exact source.\n\n## Open decisions\n\n1. DEC-01: Who owns support?\n2. DEC-02: Is direct handoff recorded?\n\n## References\n\nSource reference.\n");
        CreateIntake(f); var status = CompleteWizard(f);
        var originalBusiness = ReviewPage(f, status, "business");
        var originalTechnical = ReviewPage(f, status, "technical").GetRawText();
        File.WriteAllText(f.Request.SourcePath, metadata.Replace("First source reviewer", "Second source reviewer", StringComparison.Ordinal)
            .Replace("2026-11-01", "2026-11-02", StringComparison.Ordinal).Replace("old-digest", "new-digest", StringComparison.Ordinal)
            + "# Feature\n\n## Requirements\n\nFR-001 Preserve the exact source.\n\n## Open decisions\n\n- **DEC-02**: Is direct handoff recorded?\n- **DEC-01**: Who owns support?\n\n## References\n\nSource reference.\n");
        var preview = Reimport(f);
        Assert.Empty(preview.Errors); Assert.Equal(2, preview.Plan!.RetainedDecisionAnswers);
        Assert.Empty(preview.Plan.PagesRequiringReview);
        Assert.True(Reimport(f, false, preview.Plan.PlanHash).Applied);
        var fresh = new FeatureIntakeService(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline]);
        var reopened = fresh.Wizard(f.Authority, "referrals");
        Assert.Empty(reopened.Errors); Assert.True(reopened.Reviewed);
        var business = reopened.Pages.Single(page => page.Id == "business");
        Assert.Contains("direct handoff", business.Fields.Single(field => field.Id == "decision-001").Answer, StringComparison.Ordinal);
        Assert.Contains("support", business.Fields.Single(field => field.Id == "decision-002").Answer, StringComparison.Ordinal);
        var retained = ReviewPage(f, reopened, "business");
        Assert.Equal(originalBusiness.GetProperty("actor").GetString(), retained.GetProperty("actor").GetString());
        Assert.Equal(originalBusiness.GetProperty("savedAt").GetString(), retained.GetProperty("savedAt").GetString());
        Assert.Equal(originalTechnical, ReviewPage(f, reopened, "technical").GetRawText());
        Assert.Equal(2, reopened.SourceHistory.Count);
        File.AppendAllText(f.Request.SourcePath, "\n## Runtime deployment\n\nThe runtime uses the retained deployment platform.\n");
        preview = Reimport(f); Assert.True(Reimport(f, false, preview.Plan!.PlanHash).Applied);
        var changed = fresh.Wizard(f.Authority, "referrals");
        Assert.False(changed.Reviewed);
        Assert.True(changed.Pages.Single(page => page.Id == "business").Complete);
        Assert.False(changed.Pages.Single(page => page.Id == "technical").Complete);
        Assert.Equal(originalBusiness.GetProperty("savedAt").GetString(), ReviewPage(f, changed, "business").GetProperty("savedAt").GetString());
    }

    [Fact]
    public void ChangedDecisionMeaningRetainsStableIdentityAnswerAndOnlyAffectedReviewNeedsRenewal()
    {
        using var f = new Fixture();
        var source = "# Feature\n\n## Requirements\n\nFR-001 Preserve the exact source.\n\n## Open decisions\n\n1. DEC-01: Who owns support?\n2. DEC-02: Is direct handoff recorded?\n";
        File.WriteAllText(f.Request.SourcePath, source); CreateIntake(f);
        var status = CompleteWizard(f);
        var before = ReviewPage(f, status, "business");
        File.WriteAllText(f.Request.SourcePath, source.Replace("DEC-01: Who owns support?", "DEC-01: Who owns privileged support access?", StringComparison.Ordinal));
        var preview = Reimport(f);
        Assert.Equal(2, preview.Plan!.RetainedDecisionAnswers);
        Assert.Contains("Business definition", preview.Plan.PagesRequiringReview);
        Assert.Contains("Review and next steps", preview.Plan.PagesRequiringReview);
        Assert.DoesNotContain("Technical direction", preview.Plan.PagesRequiringReview);
        Assert.True(Reimport(f, false, preview.Plan.PlanHash).Applied);
        var after = f.Service.Wizard(f.Authority, "referrals");
        Assert.False(after.Reviewed);
        Assert.True(after.Pages.Single(page => page.Id == "technical").Complete);
        var business = after.Pages.Single(page => page.Id == "business");
        Assert.False(business.Complete);
        Assert.Contains(business.Attention, message => message.Contains("DEC-01", StringComparison.Ordinal) && message.Contains("changed its question", StringComparison.Ordinal));
        Assert.Contains("Who owns support?", business.Fields.Single(field => field.Id == "decision-001").Answer, StringComparison.Ordinal);
        Assert.Equal(before.GetProperty("savedAt").GetString(), ReviewPage(f, after, "business").GetProperty("savedAt").GetString());
        var renewed = f.Service.SaveWizard(f.Authority, new("referrals", "business",
            business.Fields.ToDictionary(field => field.Id, field => field.Answer!), "Reviewer", after.Revision!));
        Assert.True(renewed.Pages.Single(page => page.Id == "business").Complete);
        Assert.False(renewed.Reviewed);
    }

    [Fact]
    public void StableDecisionMovedToDifferentSupportedScopeRetainsAnswerButNeedsLocalReview()
    {
        using var f = new Fixture();
        const string intro = "# Feature\n\n## Requirements\n\nFR-001 Preserve the exact source.\n\n## Open decisions\n\n";
        File.WriteAllText(f.Request.SourcePath, intro + "#### MVP scope\n1. DEC-01: Who owns support?\n2. DEC-02: Is direct handoff recorded?\n\n#### Post-MVP scope\n");
        CreateIntake(f); var reviewed = CompleteWizard(f);
        var before = ReviewPage(f, reviewed, "business");
        File.WriteAllText(f.Request.SourcePath, intro + "#### MVP scope\n- DEC-02: Is direct handoff recorded?\n- DEC-01: Who owns support?\n\n#### Post-MVP scope\n");
        var preview = Reimport(f); Assert.True(Reimport(f, false, preview.Plan!.PlanHash).Applied);
        Assert.True(f.Service.Wizard(f.Authority, "referrals").Reviewed);
        File.WriteAllText(f.Request.SourcePath, intro + "#### MVP scope\n- DEC-02: Is direct handoff recorded?\n\n#### Post-MVP scope\n- DEC-01: Who owns support?\n");
        preview = Reimport(f); Assert.Equal(2, preview.Plan!.RetainedDecisionAnswers);
        Assert.True(Reimport(f, false, preview.Plan.PlanHash).Applied);
        var changed = f.Service.Wizard(f.Authority, "referrals");
        var business = changed.Pages.Single(page => page.Id == "business");
        Assert.False(changed.Reviewed); Assert.False(business.Complete);
        Assert.True(changed.Pages.Single(page => page.Id == "technical").Complete);
        Assert.False(changed.Pages.Single(page => page.Id == "delivery").Complete);
        Assert.Contains(business.Attention, message => message.Contains("business decision context DEC-01", StringComparison.Ordinal));
        Assert.Contains("Who owns support?", business.Fields.Single(field => field.Id == "decision-002").Answer, StringComparison.Ordinal);
        Assert.Equal(before.GetProperty("savedAt").GetString(), ReviewPage(f, changed, "business").GetProperty("savedAt").GetString());
    }

    [Fact]
    public void UnassignedDecisionContextUsesConservativeLocalBasisWithoutInventingQuestions()
    {
        using var f = new Fixture();
        var source = "# Feature\n\n## Requirements\n\nFR-001 Preserve the exact source.\n\n## Open decisions\n\n```csharp\nconst string policy = \"read  only\";\n```\n";
        File.WriteAllText(f.Request.SourcePath, source); CreateIntake(f);
        var reviewed = CompleteWizard(f);
        Assert.Single(reviewed.Pages.Single(page => page.Id == "business").Fields);
        File.WriteAllText(f.Request.SourcePath, source.Replace("\"read  only\"", "\"read only\"", StringComparison.Ordinal));
        var preview = Reimport(f); Assert.True(Reimport(f, false, preview.Plan!.PlanHash).Applied);
        var changed = f.Service.Wizard(f.Authority, "referrals");
        var business = changed.Pages.Single(page => page.Id == "business");
        Assert.Single(business.Fields); Assert.NotNull(business.Fields[0].Answer);
        Assert.False(business.Complete); Assert.False(changed.Reviewed);
        Assert.True(changed.Pages.Single(page => page.Id == "technical").Complete);
        Assert.Contains(business.Attention, reason => reason.Contains("unassigned source context (impact is conservative)", StringComparison.Ordinal));
    }

    private static JsonElement ReviewPage(Fixture f, CisFeatureWizardResult status, string page)
    {
        var content = File.ReadAllText(Path.Combine(f.Authority, status.Plan!.RequestPath)).Replace("\r\n", "\n", StringComparison.Ordinal);
        const string marker = "<!-- cis:feature-review-data\n";
        var start = content.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = content.IndexOf("\n-->", start, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(content[start..end]);
        return document.RootElement.GetProperty("pages").GetProperty(page).Clone();
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
        var reviewed = CompleteWizard(f);
        var originalReviewer = ReviewPage(f, reviewed, "business").GetProperty("savedAt").GetString();
        var original = File.ReadAllBytes(f.Request.SourcePath);
        File.AppendAllText(f.Request.SourcePath, "\nRevision two.\n");
        var first = Reimport(f); Assert.True(Reimport(f, false, first.Plan!.PlanHash).Applied);
        File.WriteAllBytes(f.Request.SourcePath, original);
        var second = Reimport(f); Assert.True(Reimport(f, false, second.Plan!.PlanHash).Applied);
        var status = f.Service.Wizard(f.Authority, "referrals");
        Assert.Empty(status.Errors); Assert.Equal(4, status.SourceHistory.Count); Assert.False(status.Reviewed);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(f.Authority, status.Plan!.SourcePath)));
        Assert.Equal(originalReviewer, ReviewPage(f, status, "business").GetProperty("savedAt").GetString());
        Assert.NotNull(status.Pages.Single(page => page.Id == "business").Fields[0].Answer);
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
