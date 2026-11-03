using Cis.Abstractions;
using Cis.Host;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Fact]
    public void WizardResumesEightPagesAndKeepsQuestionsUnresolvedUntilExplicitlySaved()
    {
        using var f = new Fixture();
        CreateIntake(f);
        var before = f.Snapshot();
        var status = f.Service.Wizard(f.Authority, "referrals");
        Assert.Empty(status.Errors); Assert.Equal(8, status.Pages.Count);
        Assert.True(status.Pages[0].Complete); Assert.False(status.Reviewed);
        var business = status.Pages.Single(page => page.Id == "business");
        Assert.Equal(3, business.Fields.Count);
        Assert.All(business.Fields, field => Assert.Null(field.Answer));
        Assert.Single(f.Service.Requests(f.Authority));
        Assert.Equal(before, f.Snapshot());
        var saved = f.Service.SaveWizard(f.Authority, new("referrals", "business",
            new() { ["summary"] = "A reusable referral core with a BridgeLink adapter." }, "Reviewer", status.Revision!));
        Assert.Empty(saved.Errors); Assert.True(saved.Applied);
        Assert.False(saved.Pages.Single(page => page.Id == "business").Complete);
        Assert.Contains(saved.Pages.Single(page => page.Id == "business").Attention, item => item.Contains("2 answers", StringComparison.Ordinal));
        var reopened = f.Service.Wizard(f.Authority, "referrals");
        Assert.Equal("A reusable referral core with a BridgeLink adapter.", reopened.Pages.Single(page => page.Id == "business").Fields[0].Answer);
        Assert.DoesNotContain(File.ReadAllText(Path.Combine(f.Authority, status.Plan!.RequestPath)), "status: Active", StringComparison.Ordinal);
    }

    [Fact]
    public void SaveEvaluatesBaselineOnceAndPlaceholdersRemainUnresolved()
    {
        using var f = new Fixture(); CreateIntake(f);
        var status = f.Service.Wizard(f.Authority, "referrals");
        var before = f.Baseline.Calls;
        var saved = f.Service.SaveWizard(f.Authority, new("referrals", "technical", new() { ["summary"] = "TBD" }, "Reviewer", status.Revision!));
        Assert.Empty(saved.Errors); Assert.Equal(before + 1, f.Baseline.Calls);
        Assert.False(saved.Pages.Single(page => page.Id == "technical").Complete);
        var reopened = f.Service.Wizard(f.Authority, "referrals");
        Assert.Equal(saved.Revision, reopened.Revision);
    }

    [Fact]
    public void WizardReviewRequiresAllPagesAndDriftRetainsAnswersButRevokesCompletion()
    {
        using var f = new Fixture(); CreateIntake(f);
        var originalApprovals = File.ReadAllBytes(f.ApprovedBacklog);
        var status = f.Service.Wizard(f.Authority, "referrals");
        var blocked = f.Service.SaveWizard(f.Authority, new("referrals", "review", new() { ["summary"] = "Reviewed" }, "Reviewer", status.Revision!));
        Assert.NotEmpty(blocked.Errors);
        foreach (var page in status.Pages.Where(page => page.Id is not ("foundation" or "review")))
        {
            status = f.Service.SaveWizard(f.Authority, new("referrals", page.Id,
                page.Fields.ToDictionary(field => field.Id, field => "Explicit human answer for " + field.Label), "Reviewer", status.Revision!));
            Assert.Empty(status.Errors);
        }
        Assert.False(status.Reviewed);
        status = f.Service.SaveWizard(f.Authority, new("referrals", "review", new() { ["summary"] = "Feature definition reviewed; backlog approval remains required." }, "Reviewer", status.Revision!));
        Assert.Empty(status.Errors); Assert.True(status.Reviewed);
        Assert.Equal(originalApprovals, File.ReadAllBytes(f.ApprovedBacklog));
        File.AppendAllText(Path.Combine(f.Authority, "docs/cis/specs/technical-intent-spec.md"), "\nA changed product constraint.\n");
        var stale = f.Service.Wizard(f.Authority, "referrals");
        Assert.False(stale.Reviewed);
        var affected = stale.Pages.Single(page => page.Id == "technical");
        Assert.False(affected.Complete); Assert.NotNull(affected.Fields[0].Answer);
        Assert.Contains(affected.Attention, message => message.Contains("product baseline specs/technical-intent-spec.md", StringComparison.Ordinal));
        Assert.All(stale.Pages.Where(page => page.Id is not ("foundation" or "review" or "technical")), page =>
        {
            Assert.True(page.Complete); Assert.NotNull(page.Fields[0].Answer);
        });
    }

    [Fact]
    public void IdenticalWizardSavePreservesFinalReviewAndAuditAcrossProcesses()
    {
        using var f = new Fixture(); CreateIntake(f);
        var status = CompleteWizard(f);
        var before = f.Snapshot();
        var page = status.Pages.Single(page => page.Id == "business");
        var answer = new CisFeatureWizardAnswer("referrals", page.Id,
            page.Fields.ToDictionary(field => field.Id, field => field.Answer!), "Reviewer", status.Revision!);
        var unchanged = f.Service.SaveWizard(f.Authority, answer);
        Assert.Equal("unchanged", unchanged.Status); Assert.False(unchanged.Applied);
        Assert.True(unchanged.Reviewed); Assert.Equal(status.Revision, unchanged.Revision);
        var fresh = new FeatureIntakeService(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline]);
        Assert.True(fresh.Wizard(f.Authority, "referrals").Reviewed);
        Assert.Equal(before, f.Snapshot());

        var actorChange = fresh.SaveWizard(f.Authority, answer with { Actor = "Second reviewer" });
        Assert.True(actorChange.Applied); Assert.True(actorChange.Reviewed);
        var path = Path.Combine(f.Authority, status.Plan!.RequestPath);
        Assert.Contains("Reviewed by Second reviewer", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Contains("Feature definition reviewed; backlog approval remains required.", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal("Reviewer", ReviewPage(f, actorChange, "business").GetProperty("history")[0].GetProperty("actor").GetString());
    }

    [Fact]
    public void WizardDistinguishesUnrelatedPageEditsFromDependentContractChanges()
    {
        using var f = new Fixture(); CreateIntake(f);
        var status = CompleteWizard(f);
        var technical = status.Pages.Single(page => page.Id == "technical");
        var technicalAnswers = technical.Fields.ToDictionary(field => field.Id, field => field.Answer!);
        var experience = status.Pages.Single(page => page.Id == "experience");
        var answers = experience.Fields.ToDictionary(field => field.Id, field => field.Answer!);
        answers[experience.Fields[0].Id] = "The customer capture form adds an accessible confirmation control.";
        var edited = f.Service.SaveWizard(f.Authority, new("referrals", "experience", answers, "Reviewer", status.Revision!));
        Assert.True(edited.Applied); Assert.False(edited.Reviewed);
        Assert.True(edited.Pages.Single(page => page.Id == "technical").Complete);
        Assert.True(edited.Pages.Single(page => page.Id == "contracts").Complete);
        Assert.False(edited.Pages.Single(page => page.Id == "delivery").Complete);
        Assert.Contains(edited.Pages.Single(page => page.Id == "delivery").Attention,
            message => message.Contains("experience reviewed direction", StringComparison.Ordinal));
        var before = f.Snapshot();
        var staleWrite = f.Service.SaveWizard(f.Authority, new("referrals", "technical", technicalAnswers, "Reviewer", status.Revision!));
        Assert.NotEmpty(staleWrite.Errors); Assert.Equal(before, f.Snapshot());
        var retained = f.Service.SaveWizard(f.Authority, new("referrals", "technical", technicalAnswers, "Reviewer", edited.Revision!));
        Assert.Equal("unchanged", retained.Status); Assert.Equal(before, f.Snapshot());

        technicalAnswers[technical.Fields[0].Id] = "The platform must enforce a changed identity and permission boundary.";
        var contractChange = f.Service.SaveWizard(f.Authority, new("referrals", "technical", technicalAnswers, "Reviewer", edited.Revision!));
        Assert.True(contractChange.Pages.Single(page => page.Id == "technical").Complete);
        Assert.False(contractChange.Pages.Single(page => page.Id == "architecture").Complete);
        Assert.False(contractChange.Pages.Single(page => page.Id == "contracts").Complete);
        Assert.True(contractChange.Pages.Single(page => page.Id == "experience").Complete);
        Assert.Contains(contractChange.Pages.Single(page => page.Id == "contracts").Attention,
            message => message.Contains("technical reviewed direction", StringComparison.Ordinal));
    }

    [Fact]
    public void LegacyPageUsesExactCompatibilityThenRequiresOneLocalBasisRenewal()
    {
        using var f = new Fixture(); CreateIntake(f); var status = CompleteWizard(f);
        var path = Path.Combine(f.Authority, status.Plan!.RequestPath);
        var content = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        const string marker = "<!-- cis:feature-review-data\n";
        var start = content.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = content.IndexOf("\n-->", start, StringComparison.Ordinal);
        var metadata = JsonNode.Parse(content[start..end])!;
        metadata["pages"]!["technical"]!.AsObject().Remove("basis");
        File.WriteAllText(path, content[..start] + metadata.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + content[end..]);
        var fresh = new FeatureIntakeService(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline]);
        var compatible = fresh.Wizard(f.Authority, "referrals");
        Assert.Empty(compatible.Errors); Assert.True(compatible.Reviewed);
        var technical = compatible.Pages.Single(page => page.Id == "technical");
        var answers = technical.Fields.ToDictionary(field => field.Id, field => field.Answer!);
        var before = f.Snapshot();
        Assert.Equal("unchanged", fresh.SaveWizard(f.Authority, new("referrals", "technical", answers, "Reviewer", compatible.Revision!)).Status);
        Assert.Equal(before, f.Snapshot());
        File.AppendAllText(Path.Combine(f.Authority, "docs/cis/specs/technical-intent-spec.md"), "\nChanged permission contract.\n");
        var stale = fresh.Wizard(f.Authority, "referrals");
        Assert.Contains(stale.Pages.Single(page => page.Id == "technical").Attention,
            message => message.Contains("legacy whole-feature basis", StringComparison.Ordinal));
        Assert.True(stale.Pages.Single(page => page.Id == "experience").Complete);
        var renewed = fresh.SaveWizard(f.Authority, new("referrals", "technical", answers, "Reviewer", stale.Revision!));
        Assert.True(renewed.Pages.Single(page => page.Id == "technical").Complete);
        before = f.Snapshot();
        Assert.Equal("unchanged", fresh.SaveWizard(f.Authority, new("referrals", "technical", answers, "Reviewer", renewed.Revision!)).Status);
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public void BaselineApprovalAndGeneratedProvenanceDoNotInvalidateReviewedDirection()
    {
        using var f = new Fixture(); CreateIntake(f);
        var path = Path.Combine(f.Authority, "docs/cis/specs/technical-intent-spec.md");
        static string Baseline(string actor, string at, string build)
        {
            var row = $"| TI-DEC-001 | Architecture choice | Planning | Resolved | Modular monolith. Recorded by: {actor} at {at}. |";
            var hash = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(row))).ToLowerInvariant();
            var audit = JsonSerializer.Serialize(new { Id = "TI-DEC-001", Resolution = "Modular monolith.", Reason = "", Actor = actor, RecordedAtUtc = at, RowHash = hash });
            return $"---\nstatus: Active\nlast_reviewed: {at}\ncis:\n  approved_by: {actor}\n  approved_at: {at}\n  approval_reason: Reviewed\n  approved_content_hash: {hash}\n---\n\n# Technical Intent\n\n## Technical baseline\n<!-- cis:technical-intent-baseline:start -->\nGraph build: {build}\n<!-- cis:technical-intent-baseline:end -->\n\n## Architecture direction\n<!-- cis:technical-intent-questionnaire-evidence:start -->\n| Decision input | Area | Recorded direction | Authority and provenance |\n| --- | --- | --- | --- |\n| `TI-Q-004` | Architecture | Modular monolith | {actor} at {at} |\n<!-- cis:technical-intent-questionnaire-evidence:end -->\n\n## Open technical decisions\n{row}\n<!-- cis:technical-decision-review {audit} -->\n";
        }
        File.WriteAllText(path, Baseline("First reviewer", "2026-11-01T12:00:00Z", "graph-one"));
        var status = CompleteWizard(f);
        var original = ReviewPage(f, status, "technical").GetRawText();
        File.WriteAllText(path, Baseline("Second reviewer", "2026-11-02T12:00:00Z", "graph-two"));
        var fresh = new FeatureIntakeService(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline]);
        var retained = fresh.Wizard(f.Authority, "referrals");
        Assert.Empty(retained.Errors); Assert.True(retained.Reviewed);
        Assert.Equal(original, ReviewPage(f, retained, "technical").GetRawText());
        File.AppendAllText(path, "\n## Security boundary\n\nPrivileged support must use an isolated identity boundary.\n");
        var changed = fresh.Wizard(f.Authority, "referrals");
        Assert.False(changed.Reviewed); Assert.False(changed.Pages.Single(page => page.Id == "technical").Complete);
        Assert.True(changed.Pages.Single(page => page.Id == "business").Complete);
        Assert.Contains(changed.Pages.Single(page => page.Id == "technical").Attention,
            message => message.Contains("product baseline specs/technical-intent-spec.md", StringComparison.Ordinal));
    }

    [Fact]
    public void WizardPreservesReviewForDisplayAndComponentOrderButRequiresChangedAuthorityBoundaryReview()
    {
        using var f = new Fixture(); CreateIntake(f);
        void Register(string[] components, string label)
        {
            var workspace = f.Registry.Resolve(f.Authority).Workspace!;
            File.WriteAllText(workspace.ConfigurationPath, JsonSerializer.Serialize(new
            {
                schema_version = 2,
                ecosystem = new { id = workspace.Ecosystem!.Id, name = label },
                product = new { id = workspace.Product!.Id, name = label },
                repositories = workspace.Repositories.Select(repository => new
                {
                    id = repository.Id,
                    path = repository.RepositoryPath,
                    documentation_root = repository.DocumentationRoot,
                    role = repository.Role,
                    participation = repository.Participation,
                    relationship = repository.Relationship,
                    components = repository.Id == "referrals" ? components : repository.Components
                })
            }));
        }
        Register(["referral", "capture"], "Original label");
        var reviewed = CompleteWizard(f);
        var audit = ReviewPage(f, reviewed, "business").GetRawText();
        Register(["capture", "referral"], "Updated label");
        var fresh = new FeatureIntakeService(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline]);
        var unchanged = fresh.Wizard(f.Authority, "referrals");
        Assert.True(unchanged.Reviewed); Assert.NotEqual(reviewed.Revision, unchanged.Revision);
        Assert.Equal(audit, ReviewPage(f, unchanged, "business").GetRawText());
        Register(["referral"], "Updated label");
        var changed = fresh.Wizard(f.Authority, "referrals");
        Assert.False(changed.Reviewed);
        Assert.All(changed.Pages.Where(page => page.Id is not ("foundation" or "review")), page =>
        {
            Assert.False(page.Complete);
            Assert.Contains(page.Attention, reason => reason.Contains("feature authority and repository boundary", StringComparison.Ordinal));
        });
        Assert.Equal(audit, ReviewPage(f, changed, "business").GetRawText());
    }

    [Fact]
    public void WizardRejectsStaleSaveUnknownFieldsAndChangedSourceWithoutLosingWork()
    {
        using var f = new Fixture(); CreateIntake(f);
        var status = f.Service.Wizard(f.Authority, "referrals");
        var saved = f.Service.SaveWizard(f.Authority, new("referrals", "technical", new() { ["summary"] = "Reviewed technical changes." }, "Reviewer", status.Revision!));
        Assert.Empty(saved.Errors);
        var before = f.Snapshot();
        var old = f.Service.SaveWizard(f.Authority, new("referrals", "technical", new() { ["summary"] = "Outdated overwrite." }, "Reviewer", status.Revision!));
        Assert.NotEmpty(old.Errors); Assert.Equal(before, f.Snapshot());
        var invalid = f.Service.SaveWizard(f.Authority, new("referrals", "technical", new() { ["arbitrary-field"] = "No" }, "Reviewer", saved.Revision!));
        Assert.NotEmpty(invalid.Errors); Assert.Equal(before, f.Snapshot());
        File.AppendAllText(Path.Combine(f.Authority, status.Plan!.SourcePath), "Tampered");
        Assert.NotEmpty(f.Service.Wizard(f.Authority, "referrals").Errors);
        Assert.NotEmpty(f.Service.Wizard(f.Authority, "../escape").Errors);
    }

    [Fact]
    public void FeatureWizardCommandsAreRegisteredAndStatusDispatches()
    {
        using var f = new Fixture(); CreateIntake(f);
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new WorkspaceModule())
            .AddModule(new DocsModule()).AddModule(new GraphModule()).AddModule(new BrdModule()).Build();
        foreach (var command in new[] { "list", "status", "save", "navigation", "start-backlog" })
            Assert.Equal(0, application.Invoke(["brd", "feature", "wizard", command, "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "feature", "wizard", "status", "--slug", "referrals", "--workspace", f.Authority, "--format", "json"]));
    }

    private static void CreateIntake(Fixture f)
    {
        var preview = f.Service.Import(f.Authority, f.Request, true, false);
        var created = f.Service.Import(f.Authority, f.Request, false, true, preview.Plan!.PlanHash);
        Assert.Equal("created", created.Status);
    }

    private static CisFeatureWizardResult CompleteWizard(Fixture f)
    {
        var status = f.Service.Wizard(f.Authority, "referrals");
        foreach (var page in status.Pages.Where(page => page.Id is not ("foundation" or "review")))
        {
            status = f.Service.SaveWizard(f.Authority, new("referrals", page.Id,
                page.Fields.ToDictionary(field => field.Id, field => "Explicit human answer for " + field.Label), "Reviewer", status.Revision!));
            Assert.Empty(status.Errors);
        }
        status = f.Service.SaveWizard(f.Authority, new("referrals", "review",
            new() { ["summary"] = "Feature definition reviewed; backlog approval remains required." }, "Reviewer", status.Revision!));
        Assert.Empty(status.Errors); Assert.True(status.Reviewed);
        return status;
    }
}
