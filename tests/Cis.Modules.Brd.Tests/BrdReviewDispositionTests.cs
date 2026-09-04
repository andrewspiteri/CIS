using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Brd.Tests;

public sealed class BrdReviewDispositionTests
{
    [Fact]
    public void FinalIndividualDecisionAutomaticallyLocksTheExactRecommendationSet()
    {
        using var fixture = ReviewFixture.Create();
        var service = fixture.Service;

        var initialized = service.Initialize(fixture.Root, fixture.RunId);
        var catalogPath = Path.Combine(fixture.Root, "docs", "cis", "catalog.yml");
        File.WriteAllText(catalogPath, File.ReadAllText(catalogPath)
            .Replace($"docs/cis/reviews/brd/{fixture.RunId}.md", $"reviews/brd/{fixture.RunId}.md", StringComparison.Ordinal)
            .Replace("authority: canonical", "authority: human-governed", StringComparison.Ordinal));
        service.Initialize(fixture.Root, fixture.RunId);
        var blocked = service.Approve(fixture.Root, fixture.RunId, "Andrew Spiteri", string.Empty);
        var decided = service.Decide(fixture.Root, fixture.RunId, "BRD-REV-001", "accepted",
            "Andrew Spiteri", string.Empty, "Add a measurable outcome with an owner and target date.");
        var approved = service.Approve(fixture.Root, fixture.RunId, "Andrew Spiteri", string.Empty);

        Assert.Equal("review-required", initialized.Status);
        Assert.Equal(1, initialized.PendingCount);
        Assert.Equal(4, blocked.ExitCode);
        Assert.Contains("Every finding", blocked.Errors.Single(), StringComparison.Ordinal);
        Assert.Equal("approved", decided.Status);
        Assert.Equal(2, decided.Disposition!.SchemaVersion);
        Assert.Equal("Add a measurable outcome with an owner and target date.",
            decided.Disposition.Findings.Single().ApprovedRecommendation);
        Assert.Null(decided.Disposition.Findings.Single().Rationale);
        Assert.Equal("approved", approved.Status);
        Assert.Equal("Andrew Spiteri", decided.Disposition.ApprovedBy);
        Assert.Null(decided.Disposition.ApprovalReason);
        Assert.True(CisBrdReviewDispositionCodec.IsApproved(decided.Disposition));
        Assert.True(CisBrdReviewDispositionCodec.IsApproved(approved.Disposition!));
        Assert.True(File.Exists(approved.CanonicalPath));
        Assert.Contains("BRD-REV-001 — major — accepted", File.ReadAllText(approved.CanonicalPath!), StringComparison.Ordinal);
        Assert.Contains($"docs/cis/reviews/brd/{fixture.RunId}.md", File.ReadAllText(catalogPath), StringComparison.Ordinal);
        Assert.Contains("authority: canonical", File.ReadAllText(catalogPath), StringComparison.Ordinal);
        Assert.Contains("Approved recommendation: Add a measurable outcome with an owner and target date.",
            File.ReadAllText(approved.CanonicalPath!), StringComparison.Ordinal);
    }

    [Fact]
    public void ApproveAsIs_MigratesAnUnapprovedLegacyRecordWithoutDemandingRationales()
    {
        using var fixture = ReviewFixture.Create();
        var initialized = fixture.Service.Initialize(fixture.Root, fixture.RunId);
        var legacy = initialized.Disposition! with { SchemaVersion = 1 };
        File.WriteAllText(initialized.CanonicalPath!, CisBrdReviewDispositionCodec.Render(legacy));

        var decided = fixture.Service.Decide(fixture.Root, fixture.RunId, "BRD-REV-001", "accepted",
            "Andrew Spiteri", string.Empty);

        Assert.Equal(2, decided.Disposition!.SchemaVersion);
        Assert.Equal("Add a measure.", decided.Disposition.Findings.Single().ApprovedRecommendation);
        Assert.Equal("approved", decided.Status);
        Assert.Null(decided.Disposition.ApprovalReason);
        Assert.True(CisBrdReviewDispositionCodec.IsApproved(decided.Disposition));
    }

    [Fact]
    public void AcceptAll_AtomicallyApprovesEveryPendingRecommendationAndPreservesPriorEdits()
    {
        using var fixture = ReviewFixture.Create(findingCount: 2);
        var service = fixture.Service;
        service.Initialize(fixture.Root, fixture.RunId);
        var individuallyApproved = service.Decide(fixture.Root, fixture.RunId, "BRD-REV-001", "accepted",
            "Andrew Spiteri", string.Empty, "Use the human-edited measurable outcome.");

        var approved = service.AcceptAll(fixture.Root, fixture.RunId, "Andrew Spiteri");
        var repeated = service.AcceptAll(fixture.Root, fixture.RunId, "Andrew Spiteri");

        Assert.Equal("review-required", individuallyApproved.Status);
        Assert.Equal("approved", approved.Status);
        Assert.Equal(0, approved.PendingCount);
        Assert.Equal(2, approved.AcceptedCount);
        Assert.All(approved.Disposition!.Findings, finding => Assert.Equal("Andrew Spiteri", finding.DecidedBy));
        Assert.Single(approved.Disposition.Findings.Select(finding => finding.DecidedAtUtc).Distinct());
        Assert.Equal("Use the human-edited measurable outcome.",
            approved.Disposition.Findings.Single(finding => finding.Id == "BRD-REV-001").ApprovedRecommendation);
        Assert.Equal("Resolve the second issue.",
            approved.Disposition.Findings.Single(finding => finding.Id == "BRD-REV-002").ApprovedRecommendation);
        Assert.True(CisBrdReviewDispositionCodec.IsApproved(approved.Disposition));
        Assert.Equal("approved", repeated.Status);
        Assert.Equal(approved.Disposition.ApprovedDecisionSha256, repeated.Disposition!.ApprovedDecisionSha256);
    }

    [Fact]
    public void ApprovedDecisionsAreImmutableAndBoundToTheReviewedBrd()
    {
        using var fixture = ReviewFixture.Create();
        var service = fixture.Service;
        service.Initialize(fixture.Root, fixture.RunId);
        service.Decide(fixture.Root, fixture.RunId, "BRD-REV-001", "rejected", "Andrew", "Not product scope.");
        service.Approve(fixture.Root, fixture.RunId, "Andrew", "Reject this recommendation.");

        var immutable = service.Decide(fixture.Root, fixture.RunId, "BRD-REV-001", "accepted", "Andrew", "Changed mind.");
        File.AppendAllText(fixture.BrdPath, "\nchanged\n");
        var stale = service.Status(fixture.Root, fixture.RunId);

        Assert.Equal(4, immutable.ExitCode);
        Assert.Contains("immutable", immutable.Errors.Single(), StringComparison.Ordinal);
        Assert.Equal(4, stale.ExitCode);
        Assert.Contains(stale.Errors, item => item.Contains("BRD changed", StringComparison.Ordinal));
    }

    [Fact]
    public void ManagedEvidenceOnlyChangeSupersedesReviewWithoutClaimingAuthoredBrdChanged()
    {
        using var fixture = ReviewFixture.Create();
        fixture.Service.Initialize(fixture.Root, fixture.RunId);
        var current = File.ReadAllText(fixture.BrdPath).Replace("| none |", "| BRD-SRC-abcdef123456 |", StringComparison.Ordinal);
        File.WriteAllText(fixture.BrdPath, current);

        var status = fixture.Service.Status(fixture.Root, fixture.RunId);

        Assert.Equal("superseded", status.Status);
        Assert.Equal(4, status.ExitCode);
        Assert.Contains(status.Errors, item => item.Contains("user-authored BRD content is unchanged", StringComparison.Ordinal)
            && item.Contains("fresh independent BRD review", StringComparison.Ordinal));
    }

    [Fact]
    public void FreshnessTreatsOnlyGovernedQuestionAnswersAsReviewCompatible()
    {
        using var fixture = ReviewFixture.Create();

        var current = fixture.Service.Freshness(fixture.Root, fixture.RunId);
        File.WriteAllText(fixture.BrdPath, File.ReadAllText(fixture.BrdPath).Replace(
            "1. Who owns the product outcome?\n2. What launch phase is accepted?",
            "| ID | Question | Answer | Answered by | Answered at UTC |\n"
            + "| --- | --- | --- | --- | --- |\n"
            + "| BRD-Q-001 | Who owns the product outcome? | Andrew owns it. | Andrew | 2026-09-01T07:45:00Z |\n"
            + "| BRD-Q-002 | What launch phase is accepted? | Unanswered | - | - |",
            StringComparison.Ordinal));

        var answerOnly = fixture.Service.Freshness(fixture.Root, fixture.RunId);
        File.WriteAllText(fixture.BrdPath, File.ReadAllText(fixture.BrdPath).Replace(
            "What launch phase is accepted?", "Which launch phase is accepted?", StringComparison.Ordinal));
        var changedQuestion = fixture.Service.Freshness(fixture.Root, fixture.RunId);
        File.AppendAllText(fixture.BrdPath, "\nSubstantive requirement change.\n");
        var changedBrd = fixture.Service.Freshness(fixture.Root, fixture.RunId);

        Assert.Equal("current", current.Status);
        Assert.True(current.Compatible);
        Assert.Equal("question-answers-only", answerOnly.Status);
        Assert.True(answerOnly.Compatible);
        Assert.Equal("stale", changedQuestion.Status);
        Assert.False(changedQuestion.Compatible);
        Assert.Equal("stale", changedBrd.Status);
        Assert.False(changedBrd.Compatible);
    }

    private sealed class ReviewFixture : IDisposable
    {
        public string Root { get; }
        public string RunId { get; } = "RUN-20260830100000-REVIEW";
        public string BrdPath => Path.Combine(Root, "docs", "cis", "specs", "business-requirements.md");
        public BrdReviewDispositionService Service { get; }

        private ReviewFixture(string root)
        {
            Root = root;
            var workspace = new CisWorkspace(root, Path.Combine(root, ".cis", "workspace.yml"),
                [new CisWorkspaceRepository("fixture", root, "docs/cis", "authority")]);
            Service = new BrdReviewDispositionService(new Registry(workspace),
                () => DateTimeOffset.Parse("2026-08-30T10:00:00Z"));
        }

        public static ReviewFixture Create(int findingCount = 1)
        {
            var root = Path.Combine(Path.GetTempPath(), "cis-brd-review-tests", Guid.NewGuid().ToString("N"));
            var fixture = new ReviewFixture(root);
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.BrdPath)!);
            File.WriteAllText(fixture.BrdPath, "---\nstatus: Review Required\n---\n# BRD\n\n"
                + "<!-- cis:baseline:start -->\n| none |\n<!-- cis:baseline:end -->\n\n"
                + "<!-- cis:sources:start -->\n| none |\n<!-- cis:sources:end -->\n\n"
                + "## Open questions\n\n"
                + "1. Who owns the product outcome?\n"
                + "2. What launch phase is accepted?\n");
            var run = Path.Combine(root, ".cis", "local", "agents", "runs", fixture.RunId);
            Directory.CreateDirectory(run);
            var result = JsonSerializer.Serialize(new
            {
                status = "Succeeded",
                review = new
                {
                    recommendation = "revise",
                    strengths = new[] { "Clear actor." },
                    findings = Enumerable.Range(1, findingCount).Select(index => new
                    {
                        id = $"BRD-REV-{index:000}", severity = "major", category = "testability",
                        location = "Outcomes", observation = index == 1 ? "No measure." : "A second issue exists.",
                        recommendation = index == 1 ? "Add a measure." : "Resolve the second issue."
                    }).ToArray()
                }
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var resultPath = Path.Combine(run, "result.json"); File.WriteAllText(resultPath, result);
            var manifest = JsonSerializer.Serialize(new
            {
                taskId = "BRD-REVIEW", status = "Succeeded", provider = "claude",
                taskDigest = Sha(File.ReadAllText(fixture.BrdPath)), resultDigest = ShaFile(resultPath),
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            File.WriteAllText(Path.Combine(run, "manifest.json"), manifest);
            var reviewed = Path.Combine(root, ".cis", "local", "agents", "workspaces", fixture.RunId,
                "fixture", "docs", "cis", "specs", "business-requirements.md");
            Directory.CreateDirectory(Path.GetDirectoryName(reviewed)!);
            File.Copy(fixture.BrdPath, reviewed);
            return fixture;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch (IOException) { }
        }

        private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        private static string ShaFile(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        private sealed class Registry(CisWorkspace workspace) : ICisWorkspaceRegistry
        {
            public CisWorkspaceResolution Resolve(string workspacePath) => new(workspace, []);
        }
    }
}
