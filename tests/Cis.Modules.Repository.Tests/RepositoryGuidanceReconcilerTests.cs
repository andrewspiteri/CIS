using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Repository.Tests;

public sealed class RepositoryGuidanceReconcilerTests
{
    private const string Original = "# Team\r\n\r\nUse old-index first for repository navigation.\r\n\r\nKeep domain module boundaries.\r\n";
    private const string Generated = "# Repository\n\nRead CIS instructions before work.\n";
    private const string Instructions = "# Routing\n\nUse cis index first for repository navigation.\n";
    private static readonly RepositoryGuidanceModel Route = new("test-local", "strong-model");

    private static RepositoryFileMerge Proposal(string original = Original, string instructions = Instructions)
    {
        Assert.True(RepositoryAgentsMerge.TryCreate("/repo", original, Generated, out var proposal,
            [new(".github/instructions/cis-repository.instructions.md", instructions)]));
        return proposal!;
    }

    private static string Review(string[] kept, params (string Id, string? Text)[] edits) => JsonSerializer.Serialize(new
    {
        keptIds = kept.Append("old0").Distinct().ToArray(),
        changes = edits.Select(edit => new { oldId = edit.Id, newId = "new1-2", kind = "conflict",
            reason = "This repeats the superseded first navigation source.", text = edit.Text }),
    });

    [Fact]
    public void EveryOccurrenceIncludingNumberedSequencesIsReviewedInContext()
    {
        const string original = "# Routing\n\nUse old-index as the starting point.\n\nApplication document lookup sequence:\n\n1. Start with old-index.\n2. Preserve the invoice state machine.\n\n## Later workflow\n\nFor document lookup, use old-index first.\n\nUse PostgreSQL for persistence.\n";
        var model = new FakeModel(Review(["old4", "old7", "old9", "old13"], ("old2", null), ("old6", null), ("old11", null)));
        var source = Proposal(original);
        var progress = new List<string>();
        var result = new RepositoryGuidanceReconciler(model, Route, progress.Add).Reconcile(source);
        Assert.DoesNotContain("old-index", result.ProposedContent, StringComparison.Ordinal);
        Assert.Contains("Preserve the invoice state machine.", result.ProposedContent, StringComparison.Ordinal);
        Assert.Contains("Use PostgreSQL", result.ProposedContent, StringComparison.Ordinal);
        Assert.Equal(source.CurrentContent, result.CurrentContent);
        Assert.Equal(source.ReviewHash, result.ReviewHash);
        Assert.Equal("complete", result.GuidanceReview!.Status);
        Assert.Equal(8, result.GuidanceReview.TotalInstructions);
        Assert.Equal(8, result.GuidanceReview.ReviewedInstructions);
        Assert.Equal(2, result.GuidanceReview.CompletedPasses);
        Assert.Equal(3, result.GuidanceReview.Removals.Count);
        Assert.Equal(2, model.Requests.Count);
        Assert.Contains("Later workflow", model.Requests[0].Prompt, StringComparison.Ordinal);
        Assert.Contains("Application document lookup sequence", model.Requests[0].Prompt, StringComparison.Ordinal);
        Assert.Contains("SECOND PASS", model.Requests[1].Prompt, StringComparison.Ordinal);
        Assert.All(model.Requests, request => { Assert.Equal("strong-model", request.Model); Assert.False(request.AllowRemote); });
        Assert.Contains(progress, message => message.StartsWith("Pass 1 of 2: Comparing", StringComparison.Ordinal));
        Assert.Contains(progress, message => message.StartsWith("Pass 2 of 2: Checking", StringComparison.Ordinal));
        Assert.Contains(progress, message => message.Contains("8 instructions accounted for; 3 proposed changes", StringComparison.Ordinal));
        Assert.StartsWith("Both passes completed.", progress[^1], StringComparison.Ordinal);
        Assert.DoesNotContain(progress, message => message.Contains("invoice state machine", StringComparison.Ordinal));
    }

    [Fact]
    public void AuditCanCorrectMissedOccurrencesAndRestoreUsefulRules()
    {
        var model = new FakeModel(Review(["old2"], ("old4", null)), Review(["old4"], ("old2", null)));
        var result = new RepositoryGuidanceReconciler(model, Route).Reconcile(Proposal());
        Assert.DoesNotContain("Use old-index", result.ProposedContent, StringComparison.Ordinal);
        Assert.Contains("Keep domain module boundaries.\r\n", result.ProposedContent, StringComparison.Ordinal);
        Assert.Equal("whole-document-model", Assert.Single(result.GuidanceReview!.Removals).Method);
        Assert.Equal(3, result.GuidanceReview.Removals[0].StartLine);
    }

    [Fact]
    public void CisFeedbackCannotEraseAnotherToolsUsageHistoryAndExportCommands()
    {
        var source = Proposal("# Tools\n\nUse `runner usage summary` and `runner usage export` for runner token history.\n",
            "# Usage\n\nUse cis feedback for CIS invocation history.\n");
        var result = new RepositoryGuidanceReconciler(new FakeModel(Review([], ("old2", null))), Route).Reconcile(source);
        Assert.Contains("runner usage summary", result.ProposedContent, StringComparison.Ordinal);
        Assert.Contains("runner usage export", result.ProposedContent, StringComparison.Ordinal);
        Assert.Contains(result.GuidanceReview!.Findings, finding => finding.Kind == "coverage");
        Assert.Empty(result.GuidanceReview.Removals);
    }

    [Fact]
    public void UnresolvedEnforcementUsesSuppliedEvidenceAndSurvivesBothPasses()
    {
        const string path = ".github/scripts/validate.py";
        var source = Proposal() with { ContextSources = [new(path, "requires_old_routing()", "hash", false)] };
        var response = JsonSerializer.Serialize(new { keptIds = new[] { "old0", "old4" }, changes = new[] {
            new { oldId = "old2", newId = "new1-2", kind = "conflict", reason = "CIS owns routing", text = (string?)null } },
            findings = new[] { new { kind = "enforcement", path, detail = "Migrate the existing routing gate before claiming replacement." } } });
        var result = new RepositoryGuidanceReconciler(new FakeModel(response), Route).Reconcile(source);
        Assert.Equal(path, Assert.Single(result.GuidanceReview!.Findings).Path);
        Assert.Equal(2, result.GuidanceReview.CompletedPasses);
    }

    [Fact]
    public void MixedParagraphCanRetainProjectDetailsWhileReplacingSupersededPolicy()
    {
        var source = Proposal("# Team\r\n\r\nUse old-index first. Preserve the invoice state machine.\r\n");
        var model = new FakeModel(Review([], ("old2", "Preserve the invoice state machine.")));
        var result = new RepositoryGuidanceReconciler(model, Route).Reconcile(source);
        Assert.DoesNotContain("old-index", result.ProposedContent, StringComparison.Ordinal);
        Assert.Contains("Preserve the invoice state machine.\r\n", result.ProposedContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditCanRemoveDemotedRoutesAndTheirSupportingCommandsWithoutLosingDistinctCapabilities()
    {
        const string original = "# Team\n\n## Old navigation\n\nUse old-index first for repository navigation.\n\n```sh\nold-index build\n```\n\n## Project capabilities\n\n| Command | Purpose |\n| --- | --- |\n| `old-index route` | Repository navigation |\n| `parrgen scaffold Invoice` | Generate the project invoice shape |\n\n```sql\nSELECT invoice_id FROM invoices;\n```\n\nPreserve the invoice state machine.\n";
        const string retainedTable = "| Command | Purpose |\n| --- | --- |\n| `parrgen scaffold Invoice` | Generate the project invoice shape |";
        var model = new FakeModel(
            Review(["old2", "old6", "old10", "old12", "old17", "old21"], ("old4", "Use old-index for supplementary navigation after CIS.")),
            Review(["old10", "old17", "old21"], ("old2", null), ("old4", null), ("old6", null), ("old12", retainedTable)));

        var result = new RepositoryGuidanceReconciler(model, Route).Reconcile(Proposal(original));

        Assert.Equal("complete", result.GuidanceReview!.Status);
        Assert.Equal(8, result.GuidanceReview.TotalInstructions);
        Assert.DoesNotContain("old-index", result.ProposedContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Old navigation", result.ProposedContent, StringComparison.Ordinal);
        Assert.Contains(retainedTable, result.ProposedContent, StringComparison.Ordinal);
        Assert.Contains("```sql\nSELECT invoice_id FROM invoices;\n```", result.ProposedContent, StringComparison.Ordinal);
        Assert.Contains("Preserve the invoice state machine.", result.ProposedContent, StringComparison.Ordinal);
        Assert.Contains("supplementary navigation after CIS", model.Requests[1].Prompt, StringComparison.Ordinal);
        Assert.Contains("Audit the proposal's replacement text too", model.Requests[1].Prompt, StringComparison.Ordinal);
        Assert.All(model.Requests, request => Assert.Contains("Prefer deletion (text: null)", request.Prompt, StringComparison.Ordinal));
        Assert.Contains(result.GuidanceReview.Removals, removal => removal.StartLine == 7 && removal.EndLine == 9);
        Assert.Contains(result.GuidanceReview.Removals, removal => removal.StartLine == 13 && removal.EndLine == 16);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{\"keptIds\":[\"old2\"],\"changes\":[]}")]
    [InlineData("{\"keptIds\":[\"old2\",\"old4\",\"old999\"],\"changes\":[]}")]
    [InlineData("{\"keptIds\":[\"old2\",\"old4\",\"old2\"],\"changes\":[]}")]
    [InlineData("{\"keptIds\":[\"old4\"],\"changes\":[{\"oldId\":\"old2\",\"newId\":\"new999\",\"kind\":\"conflict\",\"reason\":\"invented source\",\"text\":null}]}")]
    public void InvalidOrIncompletePassIsDiscardedAtomically(string json)
    {
        var source = Proposal();
        var result = new RepositoryGuidanceReconciler(new FakeModel(json), Route).Reconcile(source);
        Assert.Equal(source.ProposedContent, result.ProposedContent);
        Assert.Empty(result.GuidanceReview!.Removals);
        Assert.Equal("partial", result.GuidanceReview.Status);
        Assert.Equal(0, result.GuidanceReview.CompletedPasses);
        Assert.NotEmpty(result.GuidanceReview.Warnings);
    }

    [Fact]
    public void FailedAuditKeepsFirstProposalButNeverClaimsCompleteReview()
    {
        var model = new FakeModel(Review(["old4"], ("old2", null)), "truncated");
        var progress = new List<string>();
        var result = new RepositoryGuidanceReconciler(model, Route, progress.Add).Reconcile(Proposal());
        Assert.DoesNotContain("old-index", result.ProposedContent, StringComparison.Ordinal);
        Assert.Equal("partial", result.GuidanceReview!.Status);
        Assert.Equal(1, result.GuidanceReview.CompletedPasses);
        Assert.Contains(progress, message => message.StartsWith("Warning:", StringComparison.Ordinal));
        Assert.StartsWith("Preparing an incomplete proposal", progress[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void NoImplicitSmallModelFallbackAndOnlyLiteralDuplicatesWithoutSelection()
    {
        var model = new FakeModel(Review([]));
        var source = Proposal("# Team\n\n- Read CIS instructions before work.\n\nKeep domain rules.\n");
        var result = new RepositoryGuidanceReconciler(model).Reconcile(source);
        Assert.Empty(model.Requests);
        Assert.Equal("exact-text", Assert.Single(result.GuidanceReview!.Removals).Method);
        Assert.Equal("unavailable", result.GuidanceReview.Status);
        Assert.Equal(0, result.GuidanceReview.ReviewedInstructions);
    }

    [Fact]
    public void ExamplesAreReviewedInWholeBlocksWhileFrontMatterAndManagedSectionsStayExcluded()
    {
        var model = new FakeModel(JsonSerializer.Serialize(new { keptIds = new[] { "old4", "old6", "old16" }, changes = Array.Empty<object>() }));
        var source = Proposal("---\nmetadata: must stay excluded\n---\n\n# Team\n\n~~~~text\nexample must remain\n```\n~~~~\n\n<!-- cis:repository-guidance:start -->\nold generated text\n<!-- cis:repository-guidance:end -->\n\n\nKeep domain rules.\n");
        var result = new RepositoryGuidanceReconciler(model, Route).Reconcile(source);
        Assert.Contains("example must remain", model.Requests[0].Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("metadata: must stay excluded", model.Requests[0].Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("old generated text", model.Requests[0].Prompt, StringComparison.Ordinal);
        Assert.Equal("complete", result.GuidanceReview!.Status);
        Assert.Equal(3, result.GuidanceReview.TotalInstructions);
        Assert.Contains("~~~~text\nexample must remain\n```\n~~~~", result.ProposedContent, StringComparison.Ordinal);
        Assert.Contains("metadata: must stay excluded", result.ProposedContent, StringComparison.Ordinal);
        Assert.Empty(result.GuidanceReview.Removals);
    }

    [Fact]
    public void CredentialsAreNeverSentAndRemoteUseRequiresExplicitAuthorization()
    {
        var model = new FakeModel(Review(["old2", "old4"])) { IsLocal = false };
        var source = Proposal();
        var denied = new RepositoryGuidanceReconciler(model, Route).Reconcile(source);
        Assert.Empty(model.Requests);
        Assert.Equal(source.ProposedContent, denied.ProposedContent);
        var sensitive = new RepositoryGuidanceReconciler(model, Route with { AllowRemote = true })
            .Reconcile(Proposal(Original + "\napi_key = abcdefghijklmnopqrstuvwxyz012345\n"));
        Assert.Empty(model.Requests);
        Assert.Equal("skipped", sensitive.GuidanceReview!.Status);
        var allowed = new RepositoryGuidanceReconciler(model, Route with { AllowRemote = true }).Reconcile(source);
        Assert.Equal("complete", allowed.GuidanceReview!.Status);
        Assert.All(model.Requests, request => Assert.True(request.AllowRemote));
    }

    [Fact]
    public void ChangedReplacementInstructionsInvalidateReviewHash() =>
        Assert.NotEqual(Proposal().ReviewHash, Proposal(instructions: Instructions + "\nNew guidance.\n").ReviewHash);

    [Fact]
    public void LinkedScriptEvidenceCanRemoveIndirectRoutingWhilePreservingTheNamedContractObligation()
    {
        var source = Proposal("# Team\n\nRun `.github/scripts/preflight.py --contracts`. Verify command, event and module-ownership contracts.\n") with
        {
            ContextSources = [new(".github/scripts/preflight.py", "# Also requires old graph routing.\nrun_old_graph()", "hash", true)],
            ContextWarnings = ["Reference excerpt is incomplete."],
        };
        var model = new FakeModel(Review([], ("old2", "Verify command, event and module-ownership contracts.")));
        var result = new RepositoryGuidanceReconciler(model, Route).Reconcile(source);
        Assert.Contains("READ-ONLY REFERENCES", model.Requests[0].Prompt, StringComparison.Ordinal);
        Assert.Contains("run_old_graph()", model.Requests[0].Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("preflight.py", result.ProposedContent, StringComparison.Ordinal);
        Assert.Contains("Verify command, event and module-ownership contracts.", result.ProposedContent, StringComparison.Ordinal);
        Assert.Contains("Reference excerpt is incomplete.", result.GuidanceReview!.Warnings);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RetainedRuleDuplicatesMustCiteAnUnchangedSurvivingOccurrence(bool keepTarget)
    {
        var source = Proposal("# Team\n\nUse the invoice scaffold.\n\nUse the invoice scaffold for generated invoices.\n");
        var response = JsonSerializer.Serialize(new
        {
            keptIds = keepTarget ? new[] { "old0", "old2" } : new[] { "old0" },
            changes = new[] { new { oldId = "old4", newId = "old2", kind = "duplicate", reason = "Same scaffold obligation and scope.", text = (string?)null } },
        });
        var result = new RepositoryGuidanceReconciler(new FakeModel(response), Route).Reconcile(source);
        Assert.Equal(keepTarget ? "complete" : "partial", result.GuidanceReview!.Status);
        Assert.Contains("Use the invoice scaffold.\n", result.ProposedContent, StringComparison.Ordinal);
        Assert.Equal(!keepTarget, result.ProposedContent.Contains("for generated invoices", StringComparison.Ordinal));
    }

    [Fact]
    public void FormattingRepairsSurvivingListsWithoutTouchingCodeOrFrontMatter()
    {
        const string input = "---\r\nversion: 2\r\n---\r\n\r\n# Guide\r\n\r\n\r\n6. Scaffold invoices.\r\n\r\n## Checks\r\n\r\n3. Build.\r\n\r\n5. Test.\r\n   Keep this condition.\r\n\r\n```text\r\n7. Literal numbered example.\r\n\r\n\r\n```\r\n";
        var result = RepositoryGuidanceFormatting.Clean(input);
        Assert.StartsWith("---\r\nversion: 2\r\n---", result, StringComparison.Ordinal);
        Assert.Contains("# Guide\r\n\r\n1. Scaffold invoices.", result, StringComparison.Ordinal);
        Assert.Contains("1. Build.\r\n\r\n2. Test.\r\n   Keep this condition.", result, StringComparison.Ordinal);
        Assert.Contains("```text\r\n7. Literal numbered example.\r\n\r\n\r\n```", result, StringComparison.Ordinal);
        Assert.Equal(result, RepositoryGuidanceFormatting.Clean(result));
    }

    [Fact]
    public void LinkedInstructionProposalPreservesScopingWithoutAddingRepeatedEntryPointGuidance()
    {
        const string original = "---\napplyTo: 'src/**'\n---\n\n# Routing\n\nUse old graph first.\n\nKeep module boundaries.\n";
        Assert.True(RepositoryAgentsMerge.TryCreate("/repo", original, Generated, out var source,
            [new(".github/instructions/cis-repository.instructions.md", Instructions)], ".github/instructions/routing.instructions.md"));
        var response = JsonSerializer.Serialize(new
        {
            keptIds = new[] { "old4", "old8" },
            changes = new[] { new { oldId = "old6", newId = "new1-2", kind = "conflict", reason = "CIS owns this routing responsibility.", text = (string?)null } },
        });
        var result = new RepositoryGuidanceReconciler(new FakeModel(response), Route).Reconcile(source!);
        Assert.Equal("complete", result.GuidanceReview!.Status);
        Assert.StartsWith("---\napplyTo: 'src/**'\n---", result.ProposedContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Use old graph", result.ProposedContent, StringComparison.Ordinal);
        Assert.Contains("Keep module boundaries.", result.ProposedContent, StringComparison.Ordinal);
        Assert.DoesNotContain(RepositoryAgentsMerge.Start, result.ProposedContent, StringComparison.Ordinal);
    }

    private sealed class FakeModel(params string[] responses) : ICisTextGenerationService
    {
        public bool IsLocal { get; init; } = true;
        public List<CisTextGenerationRequest> Requests { get; } = [];
        public CisAiStatus GetStatus() => new([new("test-local", "available", "test", IsLocal, [new("strong-model")], null)]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request)
        {
            Requests.Add(request);
            return new("generated", "test-local", "strong-model", responses[Math.Min(Requests.Count - 1, responses.Length - 1)], null, IsLocal);
        }
    }
}
