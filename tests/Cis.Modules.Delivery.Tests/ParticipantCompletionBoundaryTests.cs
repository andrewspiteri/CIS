using Cis.Abstractions;
using Cis.Modules.Plan;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void ParticipantPlanTransitionRequiresBothReceiptsThenRejectsLaterDossierChanges()
    {
        using var fixture = new ParticipantClosureFixture(seedPlan: true);
        var plans = fixture.Plans!;
        var transition = new PlanTaskTransitionRequest(fixture.Authority.Path, fixture.Change.Id, fixture.Task.Id,
            "Complete", "Fixture", "Exercise participant transition through public planning service.");
        Assert.Contains(plans.TransitionTask(transition).Errors, error => error.Contains("closing evidence is missing", StringComparison.Ordinal));
        fixture.WriteReceipt("first");
        Assert.Contains(plans.TransitionTask(transition).Errors, error => error.StartsWith("second:", StringComparison.Ordinal));
        fixture.WriteReceipt("second");
        var completed = plans.TransitionTask(transition);
        Assert.True(completed.Applied, string.Join("; ", completed.Errors));
        Assert.Empty(plans.TransitionTask(transition).Errors);
        fixture.Authority.Write(fixture.Change.RelativePath + "/impact.md", "Changed operational consequences.");
        Assert.Contains(plans.TransitionTask(transition).Errors, error => error.Contains("authority dossier context is missing or stale", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletingOneTaskPreservesAnotherTasksReviewAcrossNativeUsageBookkeeping(bool malformedSavings)
    {
        using var fixture = new ParticipantClosureFixture(seedPlan: true);
        if (malformedSavings)
        {
            var ledger = Path.Combine(fixture.Authority.Path, CisToolUsageSnapshot.LedgerPath);
            var entry = System.Text.Json.Nodes.JsonNode.Parse(File.ReadLines(ledger).First())!;
            var lines = new List<string>();
            foreach (var savings in new[] { int.MaxValue, int.MaxValue, -200 })
            {
                entry["invocationId"] = Guid.NewGuid().ToString("N");
                entry["possibleTokenSavings"] = savings;
                lines.Add(entry.ToJsonString());
            }
            File.WriteAllLines(ledger, lines);
        }
        var firstTask = fixture.Task;
        var firstDocument = fixture.Document;
        fixture.WriteReceipt("first"); fixture.WriteReceipt("second");
        fixture.Task = fixture.OtherTask!;
        fixture.Document = File.ReadAllText(Path.Combine(fixture.Authority.Path, fixture.Change.RelativePath, fixture.Task.TaskPath!));
        fixture.WriteReceipt("first"); fixture.WriteReceipt("second");
        Assert.Empty(fixture.Review());
        var before = CisTaskAuthorityContext.Capture(fixture.Context("authority"), fixture.Change.Id);
        var result = fixture.Plans!.TransitionTask(new(fixture.Authority.Path, fixture.Change.Id, firstTask.Id,
            "Complete", "Fixture", "Verify another task review survives real usage bookkeeping."));
        Assert.True(result.Applied, string.Join("; ", result.Errors));
        var verification = File.ReadAllText(Path.Combine(fixture.Authority.Path, fixture.Change.RelativePath, "verification.md"));
        Assert.Contains("CIS tool-usage snapshot", verification, StringComparison.Ordinal);
        if (malformedSavings) Assert.Contains("possibleTokenSavings=4294967294", verification, StringComparison.Ordinal);
        Assert.Equal(before, CisTaskAuthorityContext.Capture(fixture.Context("authority"), fixture.Change.Id));
        Assert.Equal(fixture.Plans.ReadTaskDigest(fixture.Authority.Path, fixture.Change.Id, fixture.Task.Id),
            EngineeringCompletionReview.TaskDigest(fixture.Task, fixture.Document));
        Assert.Empty(fixture.Review());
        Assert.NotEqual(firstDocument, File.ReadAllText(Path.Combine(fixture.Authority.Path, fixture.Change.RelativePath, firstTask.TaskPath!)));
    }

    [Theory]
    [InlineData("invalid-workspace")]
    [InlineData("missing-target-config")]
    [InlineData("missing-registry")]
    public void UncertainAdoptionCannotBypassParticipantCompletion(string scenario)
    {
        using var fixture = new ParticipantClosureFixture();
        fixture.OptOut("authority");
        if (scenario == "invalid-workspace") fixture.Authority.Write(".cis/workspace.yml", "schema_version: 2\nrepositories: [broken");
        if (scenario == "missing-target-config") File.Delete(Path.Combine(fixture.First.Path, ".cis/repository.yml"));
        if (scenario == "missing-registry") fixture.RegistryAvailable = false;
        Assert.Contains(fixture.Review(), error => error.Contains("adoption cannot be determined", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("failed")]
    [InlineData("no-checker")]
    [InlineData("alignment-error")]
    [InlineData("changed-contract")]
    public void ParticipantCompletionRequiresCurrentAuthorityChecks(string scenario)
    {
        using var fixture = new ParticipantClosureFixture();
        fixture.WriteReceipt("first"); fixture.WriteReceipt("second");
        var expected = "Authority task context requires a fresh graph";
        if (scenario is "stale" or "failed") fixture.GraphOverride = new AuthorityGraphFailure(fixture.Authority.Path, scenario);
        if (scenario == "no-checker") { fixture.Checks = []; expected = "Authority engineering alignment checker is unavailable"; }
        if (scenario == "alignment-error") { fixture.Checks = [new AuthorityAlignmentFailure()]; expected = "authority: Missing adopted standard"; }
        if (scenario == "changed-contract")
        {
            fixture.GraphAction = _ => fixture.Task = fixture.Task with { Title = "Changed acceptance scope" };
            expected = "Authority or task inputs changed during completion review";
        }
        Assert.Contains(fixture.Review(), error => error.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyCompletionDoesNotImposeEngineeringTargetRouting(bool standalone)
    {
        using var fixture = new ParticipantClosureFixture();
        foreach (var id in new[] { "authority", "first", "second" }) fixture.OptOut(id);
        if (standalone) File.Delete(Path.Combine(fixture.Authority.Path, ".cis/workspace.yml"));
        fixture.Task = fixture.Task with { Targets = standalone ? ["legacy-feature-component"] : [] };
        Assert.Empty(fixture.Review());
    }

    [Theory]
    [InlineData("authority")]
    [InlineData("first")]
    public void CombinedCompletionRequiresAdoptionAcrossAllDeclaredTargets(string adopted)
    {
        using var fixture = new ParticipantClosureFixture();
        fixture.Task = fixture.Task with { Targets = ["authority", "first", "second"] };
        foreach (var id in new[] { "authority", "first", "second" }.Where(id => id != adopted)) fixture.OptOut(id);
        fixture.WriteReceipt(adopted);
        var errors = fixture.Review();
        foreach (var id in new[] { "authority", "first", "second" }.Where(id => id != adopted))
            Assert.Contains(errors, error => error.StartsWith(id + ": Combined task completion requires reviewed engineering adoption", StringComparison.Ordinal));
        fixture.GraphOverride = new AuthorityGraphFailure(fixture.Authority.Path, "stale");
        Assert.Contains(fixture.Review(), error => error.Contains("Authority task context requires a fresh graph", StringComparison.Ordinal));
    }

    [Fact]
    public void MixedTargetsKeepAuthorityReceiptSeparateFromParticipantReceipts()
    {
        using var fixture = new ParticipantClosureFixture();
        fixture.Task = fixture.Task with { Targets = ["authority", "first"] };
        Assert.Equal("docs/cis/changes/CIS-0001/verification/WORK-030.json", fixture.Prepare("authority").ReceiptPath);
        fixture.WriteReceipt("authority"); fixture.WriteReceipt("first");
        Assert.Empty(fixture.Review());
    }

    [Theory]
    [InlineData("native")]
    [InlineData("human")]
    [InlineData("authority-build")]
    public void ParticipantCompletionResolvesOrdinaryEvidenceAtItsTarget(string scenario)
    {
        using var fixture = new ParticipantClosureFixture { RequireBuild = true };
        fixture.WriteReceipt("first", scenario); fixture.WriteReceipt("second");
        if (scenario == "authority-build")
            Assert.Contains(fixture.Review(), error => error.StartsWith("first: Gate 'build' artifact", StringComparison.Ordinal));
        else Assert.Empty(fixture.Review());
    }

    [Theory]
    [InlineData("proposal.md")]
    [InlineData("impact.md")]
    [InlineData("design.md")]
    [InlineData("decisions.md")]
    [InlineData("test-cases.md")]
    [InlineData("verification.md")]
    [InlineData("missing-dossier")]
    public void ParticipantReviewExpiresWhenDossierEvidenceChanges(string name)
    {
        using var fixture = new ParticipantClosureFixture();
        if (name != "missing-dossier") fixture.Authority.Write("docs/cis/changes/CIS-0001/" + name, "Original evidence.");
        fixture.WriteReceipt("first", name); fixture.WriteReceipt("second");
        if (name != "missing-dossier")
        {
            Assert.Empty(fixture.Review());
            fixture.Authority.Write("docs/cis/changes/CIS-0001/" + name, "Changed evidence after review.");
        }
        Assert.Contains(fixture.Review(), error => error.Contains("authority dossier context is missing or stale", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParticipantReviewExpiresWhenDossierDocumentsAreAddedOrRemoved(bool remove)
    {
        using var fixture = new ParticipantClosureFixture();
        const string path = "docs/cis/changes/CIS-0001/impact.md";
        if (remove) fixture.Authority.Write(path, "Reviewed operational impact.");
        fixture.WriteReceipt("first"); fixture.WriteReceipt("second");
        Assert.Empty(fixture.Review());
        if (remove) File.Delete(Path.Combine(fixture.Authority.Path, path));
        else fixture.Authority.Write(path, "New operational impact.");
        Assert.Contains(fixture.Review(), error => error.Contains("authority dossier context is missing or stale", StringComparison.Ordinal));
    }

    [Fact]
    public void AuthorityDossierIgnoresOnlyGeneratedUsageRows()
    {
        using var fixture = new ParticipantClosureFixture();
        const string path = "docs/cis/changes/CIS-0001/verification.md";
        fixture.Authority.Write(path, "Verified scope.\n");
        var before = CisTaskAuthorityContext.Capture(fixture.Context("authority"), "CIS-0001");
        var row = "| WORK-030 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1; failed=0; possibleTokenSavings=0; ledgerDigest=sha256:" + new string('a', 64) + " |\n";
        fixture.Authority.Write(path, "Verified scope.\n" + row);
        Assert.Equal(before, CisTaskAuthorityContext.Capture(fixture.Context("authority"), "CIS-0001"));
        foreach (var altered in new[] { row.Replace("invocations=1", "invocations=an altered acceptance claim", StringComparison.Ordinal),
            row.Replace(new string('a', 64), "fixture", StringComparison.Ordinal), row.Replace(" |\n", "; arbitrary requirement |\n", StringComparison.Ordinal) })
        {
            fixture.Authority.Write(path, "Verified scope.\n" + altered);
            Assert.NotEqual(before, CisTaskAuthorityContext.Capture(fixture.Context("authority"), "CIS-0001"));
        }
        fixture.Authority.Write(path, "Verified scope.\n| WORK-030 | Business acceptance | Passed | Changed result |\n");
        Assert.NotEqual(before, CisTaskAuthorityContext.Capture(fixture.Context("authority"), "CIS-0001"));
    }

    [Theory]
    [InlineData("oversized")]
    [InlineData("invalid-utf8")]
    [InlineData("escaping-id")]
    public void AuthorityDossierRefusesUnboundedOrInvalidEvidence(string scenario)
    {
        using var repository = TemporaryRepository.Create(buildGraph: false);
        var context = new Cis.Modules.Repository.CisRepositoryContextResolver().Resolve(repository.Path).Context!;
        const string relative = "docs/cis/changes/CIS-0001/impact.md";
        repository.Write(relative, scenario == "oversized" ? new string('x', 256 * 1024 + 1) : "Bounded evidence.");
        if (scenario == "invalid-utf8") File.WriteAllBytes(Path.Combine(repository.Path, relative), [0xff]);
        Assert.Throws<InvalidDataException>(() => CisTaskAuthorityContext.Capture(context, scenario == "escaping-id" ? "../other" : "CIS-0001"));
    }

    [Fact]
    public void AuthorityRegistrationAcceptsEquivalentPlatformPaths()
    {
        using var fixture = new ParticipantClosureFixture();
        var path = fixture.Authority.Path.Replace('\\', '/');
        var equivalent = (OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path) + "/";
        var registration = Path.Combine(fixture.Authority.Path, ".cis/workspace.yml");
        File.WriteAllText(registration, File.ReadAllText(registration).Replace("path: " + path + "\n", "path: " + equivalent + "\n", StringComparison.Ordinal));
        fixture.Task = fixture.Task with { Targets = ["authority", "first"] };
        fixture.WriteReceipt("authority"); fixture.WriteReceipt("first");
        Assert.Empty(fixture.Review());
    }

    private sealed class AuthorityGraphFailure(string authority, string scenario) : ICisGraphSnapshotReader
    {
        public CisGraphSnapshotReadResult Read(string repositoryPath) => throw new NotSupportedException();
        public CisGraphMetadataReadResult ReadMetadata(string repositoryPath)
            => repositoryPath == authority
                ? new("error", scenario == "failed" ? 5 : 0, repositoryPath, scenario == "failed" ? "fresh" : "stale",
                    new("graph-1", "synthetic", null, true, "complete"), null, [], [])
                : new ClosingGraph().ReadMetadata(repositoryPath);
    }

    private sealed class AuthorityAlignmentFailure : ICisRepositoryDoctorCheck
    {
        public string Name => "engineering-alignment";
        public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
            => context.RepositoryId == "authority" ? [new("fixture", "error", "alignment", "Missing adopted standard", [], "Reconcile", null, "manual")] : [];
    }
}
