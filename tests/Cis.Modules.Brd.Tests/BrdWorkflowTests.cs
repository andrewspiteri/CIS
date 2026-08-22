using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cis.Host;

namespace Cis.Modules.Brd.Tests;

public sealed class BrdWorkflowTests
{
    [Fact]
    public void BrdCommands_AreRegistered()
    {
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new WorkspaceModule())
            .AddModule(new DocsModule())
            .AddModule(new GraphModule())
            .AddModule(new BrdModule())
            .Build();

        Assert.Equal(0, application.Invoke(["brd", "discover", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "init", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "reconcile", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "status", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "validate", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "approve", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "backlog", "build", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "backlog", "validate", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "backlog", "status", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "backlog", "approve", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "backlog", "start", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "feature", "validate", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "feature", "status", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "feature", "approve", "--help"]));
    }

    [Fact]
    public void Discover_ReportsMissingWhenNoCandidateExists()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 3);

        var result = environment.Service.Discover(environment.Authority.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("missing", result.Status);
        Assert.Empty(result.Candidates);
        Assert.Equal(4, result.Baselines.Count);
        Assert.Single(result.Baselines, baseline => baseline.Role == "authority");
        Assert.Equal(3, result.Baselines.Count(baseline => baseline.Role == "participant"));
    }

    [Fact]
    public void Validate_DoesNotTreatTodoProductNameAsAnAuthoringPlaceholder()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        Assert.Equal(0, environment.Service.Initialize(
            environment.Authority.Path,
            "Friends Todo Business Requirements").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        var content = File.ReadAllText(environment.CanonicalPath).Replace(
            "Stakeholders reviewed this section and recorded explicit, testable business requirements.",
            "Stakeholders reviewed the Friends Todo application and recorded explicit requirements.",
            StringComparison.Ordinal);
        File.WriteAllText(environment.CanonicalPath, content);

        var result = environment.Service.Validate(environment.Authority.Path);

        Assert.True(result.Validation!.Valid);
        Assert.Equal("Ready for Approval", result.Validation.EffectiveStatus);
    }

    [Fact]
    public void Discover_FindsExistingBrdAsReviewRequiredEvidence()
    {
        using var environment = WorkspaceEnvironment.Create(
            participants: 3,
            configureParticipant: (repository, index) =>
            {
                if (index == 1)
                {
                    repository.Write(
                        "legacy/BRD.md",
                        "# Business Requirements Document\n\nHistorical statements that require review.\n");
                }
            });

        var result = environment.Service.Discover(environment.Authority.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("review-required", result.Status);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("legacy/BRD.md", candidate.Path);
        Assert.False(candidate.Canonical);
        Assert.Contains("file-name", candidate.Signals);
    }

    [Fact]
    public void Discover_FindsGameDesignDocumentAsProductDesignEvidence()
    {
        using var environment = WorkspaceEnvironment.Create(
            participants: 1,
            configureParticipant: (repository, _) => repository.Write(
                "docs/Product_GDD_v2.md",
                "---\ntitle: Product Game Design Document\ntype: product-design\nstatus: Active\n---\n\n" +
                "# Product Game Design Document (GDD v2)\n\nCanonical gameplay and product intent.\n"));

        var result = environment.Service.Discover(environment.Authority.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("review-required", result.Status);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("product-design", candidate.Kind);
        Assert.Contains("game-design-document-file-name", candidate.Signals);
        Assert.Contains("product-design-type", candidate.Signals);
    }

    [Fact]
    public void Initialize_CreatesCanonicalReviewRequiredBrdForMissingEvidence()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 3);

        var result = environment.Service.Initialize(
            environment.Authority.Path,
            "Commerce Business Requirements");

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Applied);
        var path = environment.CanonicalPath;
        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("status: Review Required", content, StringComparison.Ordinal);
        Assert.Contains("No BRD or feature-specification evidence was discovered", content, StringComparison.Ordinal);
        Assert.Contains("TODO: Complete executive summary", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            $"id: {environment.AuthorityId}:spec:business-requirements",
            File.ReadAllText(Path.Combine(environment.Authority.Path, "docs", "catalog.yml")),
            StringComparison.Ordinal);
        Assert.Equal("Review Required", result.Validation!.EffectiveStatus);
        Assert.False(result.Validation.Valid);
        var graphBuild = environment.Builder.Build(environment.Authority.Path);
        Assert.Equal(0, graphBuild.ExitCode);
    }

    [Fact]
    public void Initialize_RegistersCandidateWithoutTreatingItAsCurrent()
    {
        using var environment = WorkspaceEnvironment.Create(
            participants: 1,
            configureParticipant: (repository, _) => repository.Write(
                "docs-existing/business-requirements.md",
                "# Business Requirements\n\nUnverified requirements.\n"));

        var result = environment.Service.Initialize(environment.Authority.Path, "Product BRD");

        Assert.Equal(0, result.ExitCode);
        var content = File.ReadAllText(environment.CanonicalPath);
        Assert.Contains("BRD-SRC-", content, StringComparison.Ordinal);
        Assert.Contains("Unreviewed", content, StringComparison.Ordinal);
        Assert.Contains("TODO", content, StringComparison.Ordinal);
        Assert.False(result.Validation!.Valid);
        Assert.NotEqual("Active", result.Validation.EffectiveStatus);
    }

    [Fact]
    public void Approve_RequiresCompletedContentAndRecordsHumanAuthority()
    {
        using var environment = WorkspaceEnvironment.Create(
            participants: 1,
            configureParticipant: (repository, _) => repository.Write(
                "legacy/BRD.md",
                "# Business Requirements Document\n\nHistorical source.\n"));
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);

        var blocked = environment.Service.Approve(
            environment.Authority.Path,
            "Andrew Spiteri",
            "Reviewed with stakeholders");
        Assert.Equal(5, blocked.ExitCode);
        Assert.Equal("blocked", blocked.Status);

        CompleteHumanReview(environment.CanonicalPath, hasCandidate: true);
        var ready = environment.Service.Validate(environment.Authority.Path);
        Assert.True(ready.Validation!.Valid);
        Assert.Equal("Ready for Approval", ready.Validation.EffectiveStatus);

        var approved = environment.Service.Approve(
            environment.Authority.Path,
            "Andrew Spiteri",
            "Stakeholders confirmed scope, outcomes, and traceability");

        Assert.Equal(0, approved.ExitCode);
        Assert.Equal("approved", approved.Status);
        Assert.True(approved.Applied);
        var content = File.ReadAllText(environment.CanonicalPath);
        Assert.Contains("status: Active", content, StringComparison.Ordinal);
        Assert.Contains("approved_by: \"Andrew Spiteri\"", content, StringComparison.Ordinal);
        Assert.Contains("approval_reason:", content, StringComparison.Ordinal);
        var status = environment.Service.Status(environment.Authority.Path);
        Assert.Equal("Active", status.Validation!.EffectiveStatus);
        Assert.True(status.Validation.Current);
        var repeated = environment.Service.Approve(
            environment.Authority.Path,
            "Andrew Spiteri",
            "Stakeholders confirmed scope, outcomes, and traceability");
        Assert.Equal("unchanged", repeated.Status);
        Assert.False(repeated.Applied);
    }

    [Fact]
    public void Status_MarksApprovedBrdStaleAfterParticipantGraphChanges()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        Assert.Equal(0, environment.Service.Approve(
            environment.Authority.Path,
            "Business owner",
            "Requirements reviewed").ExitCode);
        var participant = environment.Participants[0];
        participant.Write("src/Product/Changed.cs", "public sealed class Changed { }");
        Assert.Equal(0, environment.Builder.Build(participant.Path).ExitCode);

        var status = environment.Service.Status(environment.Authority.Path);

        Assert.Equal(0, status.ExitCode);
        Assert.Equal("Stale", status.Validation!.EffectiveStatus);
        Assert.False(status.Validation.Current);
        Assert.Contains(status.Validation.Warnings, warning =>
            warning.Contains("baseline differs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Reconcile_PreservesApprovalWhenOnlyParticipantImplementationBaselineChanges()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        Assert.Equal(0, environment.Service.Approve(
            environment.Authority.Path,
            "Business owner",
            "Requirements reviewed").ExitCode);
        var participant = environment.Participants[0];
        participant.Write("src/Product/ImplementedFeature.cs", "public sealed class ImplementedFeature { }");
        Assert.Equal(0, environment.Builder.Build(participant.Path).ExitCode);

        var stale = environment.Service.Status(environment.Authority.Path);
        Assert.Equal("Stale", stale.Validation!.EffectiveStatus);

        var reconciled = environment.Service.Reconcile(environment.Authority.Path);

        Assert.Equal(0, reconciled.ExitCode);
        Assert.True(reconciled.Applied);
        Assert.True(reconciled.Validation!.Valid);
        Assert.True(reconciled.Validation.Current);
        Assert.Equal("Active", reconciled.Validation.EffectiveStatus);
        var content = File.ReadAllText(environment.CanonicalPath);
        Assert.Contains("approved_by: \"Business owner\"", content, StringComparison.Ordinal);
        Assert.Contains("approval_reason: \"Requirements reviewed\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_MarksApprovedBrdStaleAfterCanonicalContentChanges()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        Assert.Equal(0, environment.Service.Approve(
            environment.Authority.Path,
            "Business owner",
            "Requirements reviewed").ExitCode);
        var approved = File.ReadAllText(environment.CanonicalPath);
        Assert.Contains("approved_content_hash: \"sha256:", approved, StringComparison.Ordinal);
        File.WriteAllText(environment.CanonicalPath, approved.Replace(
            "Stakeholders reviewed this section and recorded explicit, testable business requirements.",
            "Stakeholders changed this section after approval and recorded different business requirements.",
            StringComparison.Ordinal));

        var status = environment.Service.Status(environment.Authority.Path);

        Assert.Equal(0, status.ExitCode);
        Assert.Equal("Stale", status.Validation!.EffectiveStatus);
        Assert.False(status.Validation.Current);
        Assert.Contains(status.Validation.Warnings, warning =>
            warning.Contains("content changed after approval", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Backlog_BuildsTraceableHighLevelItemsAndPreservesApprovalLifecycle()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 2);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        var content = File.ReadAllText(environment.CanonicalPath);
        content = Regex.Replace(content,
            "(?ms)^## Functional requirements\\s*$.*?(?=^## )",
            "## Functional requirements\n\n| ID | Requirement | Priority | Acceptance intent |\n| --- | --- | --- | --- |\n| BRD-FR-001 | A visitor shall sign in and maintain a renewable session. | Must | Authentication is verified by the API. |\n| BRD-FR-002 | An authenticated user shall create and manage a todo list. | Must | Only current members can see the list. |\n| BRD-FR-003 | A user shall see personal and shared lists. | Must | Revoked lists are excluded. |\n\n",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        File.WriteAllText(environment.CanonicalPath, content);
        var backlog = new BrdBacklogService(environment.Service, environment.Registry,
            new CisRepositoryContextResolver(), new DocumentationCatalogMerger(), [new ReadyCheck()],
            () => new DateTimeOffset(2026, 8, 9, 13, 0, 0, TimeSpan.Zero));

        var blocked = backlog.Build(environment.Authority.Path);

        Assert.Equal("blocked", blocked.Status);
        Assert.Equal(5, blocked.ExitCode);
        Assert.Contains(blocked.Errors, error => error.Contains("Active, current BRD", StringComparison.Ordinal));

        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Product owner", "Functional scope accepted").ExitCode);

        var built = backlog.Build(environment.Authority.Path);

        Assert.Equal(0, built.ExitCode);
        Assert.Equal("built", built.Status);
        Assert.Equal(3, built.Items.Count);
        Assert.Equal("Ready for Approval", built.Validation!.EffectiveStatus);
        Assert.Contains(built.Items, item => item.Id == "HLT-FR-001" && item.FrontendTypes.Contains("public"));
        Assert.Contains(built.Items, item => item.Id == "HLT-FR-002"
            && item.FrontendTypes.Contains("customer")
            && item.DependsOn.SequenceEqual(["HLT-FR-001"]));
        Assert.Contains(built.Items, item => item.Id == "HLT-FR-003"
            && item.DependsOn.SequenceEqual(["HLT-FR-002"]));
        Assert.Equal("unchanged", backlog.Build(environment.Authority.Path).Status);

        var approved = backlog.Approve(environment.Authority.Path, "Product owner", "High-level outcomes accepted");
        Assert.Equal("approved", approved.Status);
        Assert.Equal("Active", backlog.Status(environment.Authority.Path).Validation!.EffectiveStatus);

        var backlogPath = Path.Combine(environment.Authority.Path, "docs", "plans", "high-level-backlog.md");
        var provenanceOnlyBrd = File.ReadAllText(environment.CanonicalPath).Replace(
            "<!-- cis:feature-traceability:end -->",
            "<!-- approved evidence trace only -->\n<!-- cis:feature-traceability:end -->",
            StringComparison.Ordinal);
        File.WriteAllText(environment.CanonicalPath, provenanceOnlyBrd);
        Assert.Equal("Active", backlog.Status(environment.Authority.Path).Validation!.EffectiveStatus);

        var legacyBacklog = Regex.Replace(File.ReadAllText(backlogPath),
            "(?m)^  (brd_hash|technical_intent_hash): semantic-v1:sha256:[a-f0-9]{64}$",
            "  $1: sha256:legacy", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        File.WriteAllText(backlogPath, legacyBacklog);
        Assert.Equal("Stale", backlog.Status(environment.Authority.Path).Validation!.EffectiveStatus);
        var migrated = backlog.Build(environment.Authority.Path);
        Assert.True(migrated.Applied);
        Assert.Equal("Active", migrated.Validation!.EffectiveStatus);
        Assert.Contains("approved_by: \"Product owner\"", File.ReadAllText(backlogPath), StringComparison.Ordinal);

        var started = backlog.StartFeature(environment.Authority.Path, "HLT-FR-001");
        Assert.Equal("started", started.Status);
        Assert.Equal("docs/specs/features/hlt-fr-001/feature-specification.md", started.RelativePath);
        Assert.NotNull(started.RelativePath);
        var featurePath = Path.Combine(environment.Authority.Path,
            started.RelativePath!.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(featurePath));
        var featureContent = File.ReadAllText(featurePath);
        Assert.Contains("Architecture and structural tests", featureContent, StringComparison.Ordinal);
        Assert.Contains("Business acceptance tests", featureContent, StringComparison.Ordinal);
        Assert.Contains("Testcontainers-provisioned dependency", featureContent, StringComparison.Ordinal);
        Assert.Contains("Mutation testing and test-quality assurance", featureContent, StringComparison.Ordinal);
        Assert.Equal("Active", backlog.Status(environment.Authority.Path).Validation!.EffectiveStatus);
        Assert.Equal("unchanged", backlog.StartFeature(environment.Authority.Path, "HLT-FR-001").Status);

        File.WriteAllText(backlogPath, File.ReadAllText(backlogPath).Replace(
            "A visitor shall sign in and maintain a renewable session.",
            "A visitor shall sign in through a changed journey.", StringComparison.Ordinal));
        Assert.Equal("Stale", backlog.Status(environment.Authority.Path).Validation!.EffectiveStatus);
    }

    [Fact]
    public void BacklogCommand_PropagatesBlockedExitCode()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new WorkspaceModule())
            .AddModule(new DocsModule())
            .AddModule(new GraphModule())
            .AddModule(new BrdModule())
            .Build();

        var exitCode = application.Invoke([
            "brd", "backlog", "build",
            "--workspace", environment.Authority.Path,
            "--format", "agent",
        ]);

        Assert.Equal(5, exitCode);
    }

    [Fact]
    public void FeatureSpecification_RequiresCompleteContentAndPreservesApprovalEvidence()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        var brdContent = File.ReadAllText(environment.CanonicalPath);
        brdContent = Regex.Replace(brdContent,
            "(?ms)^## Functional requirements\\s*$.*?(?=^## )",
            "## Functional requirements\n\n| ID | Requirement | Priority | Acceptance intent |\n| --- | --- | --- | --- |\n| BRD-FR-001 | A visitor shall sign in and maintain a renewable session. | Must | Authentication is verified by the API. |\n\n",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        File.WriteAllText(environment.CanonicalPath, brdContent);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Product owner", "Functional scope accepted").ExitCode);
        var backlog = new BrdBacklogService(environment.Service, environment.Registry,
            new CisRepositoryContextResolver(), new DocumentationCatalogMerger(), [new ReadyCheck()],
            () => new DateTimeOffset(2026, 8, 9, 13, 0, 0, TimeSpan.Zero));
        Assert.Equal("built", backlog.Build(environment.Authority.Path).Status);
        Assert.Equal("approved", backlog.Approve(environment.Authority.Path, "Product owner", "High-level outcomes accepted").Status);
        var started = backlog.StartFeature(environment.Authority.Path, "HLT-FR-001");
        var path = Path.Combine(environment.Authority.Path, started.RelativePath!.Replace('/', Path.DirectorySeparatorChar));

        var incomplete = backlog.ValidateFeature(environment.Authority.Path, "HLT-FR-001");
        Assert.Equal(5, incomplete.ExitCode);
        Assert.False(incomplete.Validation!.Valid);
        Assert.Contains(incomplete.Validation.Errors, error => error.Contains("placeholder", StringComparison.OrdinalIgnoreCase));

        File.WriteAllText(path, File.ReadAllText(path).Replace("TODO", "Reviewed", StringComparison.Ordinal));
        var ready = backlog.ValidateFeature(environment.Authority.Path, "HLT-FR-001");
        Assert.True(ready.ExitCode == 0, string.Join(Environment.NewLine, ready.Validation?.Errors ?? ready.Errors));
        Assert.Equal("Ready for Approval", ready.Validation!.EffectiveStatus);

        var backlogPath = Path.Combine(environment.Authority.Path, "docs", "plans", "high-level-backlog.md");
        var approvedBacklog = File.ReadAllText(backlogPath);
        File.WriteAllText(backlogPath, Regex.Replace(approvedBacklog, "(?m)^  brd_hash:.*$", "  brd_hash: sha256:stale"));
        var upstreamStale = backlog.ValidateFeature(environment.Authority.Path, "HLT-FR-001");
        Assert.Equal("Review Required", upstreamStale.Validation!.EffectiveStatus);
        Assert.False(upstreamStale.Validation.Current);
        File.WriteAllText(backlogPath, approvedBacklog);

        var approved = backlog.ApproveFeature(environment.Authority.Path, "HLT-FR-001", "Product owner", "Feature scope accepted");
        Assert.Equal("approved", approved.Status);
        Assert.Equal("Active", approved.Validation!.EffectiveStatus);
        Assert.Equal("unchanged", backlog.ApproveFeature(environment.Authority.Path, "HLT-FR-001", "Product owner", "Feature scope accepted").Status);

        File.WriteAllText(path, File.ReadAllText(path).Replace("Deliver `HLT-FR-001`", "Provide `HLT-FR-001`", StringComparison.Ordinal));
        var stale = backlog.FeatureStatus(environment.Authority.Path, "HLT-FR-001");
        Assert.Equal("Stale", stale.Validation!.EffectiveStatus);
        Assert.False(stale.Validation.Current);
        var brdBeforeRenewal = environment.Service.Status(environment.Authority.Path);
        Assert.Equal("Active", brdBeforeRenewal.Validation!.EffectiveStatus);

        var renewed = backlog.ApproveFeature(environment.Authority.Path, "HLT-FR-001", "Product owner",
            "Revised feature scope accepted");
        Assert.Equal("approved", renewed.Status);
        Assert.Equal("Active", renewed.Validation!.EffectiveStatus);
        Assert.True(renewed.Validation.Current);
        var brdAfterRenewal = environment.Service.Status(environment.Authority.Path);
        Assert.Equal("Stale", brdAfterRenewal.Validation!.EffectiveStatus);
        Assert.Contains(brdAfterRenewal.Validation.Errors, error =>
            error.Contains("BRD candidate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Initialize_IsIdempotentWhenEvidenceAndBaselinesAreUnchanged()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 2);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);

        var repeated = environment.Service.Initialize(environment.Authority.Path, "Product BRD");

        Assert.Equal(0, repeated.ExitCode);
        Assert.Equal("unchanged", repeated.Status);
        Assert.False(repeated.Applied);
    }

    [Fact]
    public void Reconcile_AbsorbsNewFeatureSpecificationAsReviewedTraceableEvidence()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        Assert.Equal(0, environment.Service.Approve(
            environment.Authority.Path,
            "Business owner",
            "Initial requirements reviewed").ExitCode);
        var participant = environment.Participants[0];
        participant.Write(
            "docs/specs/customer-checkout-feature-spec.md",
            "---\ntitle: Customer checkout\ntype: feature-specification\nstatus: Draft\n---\n\n" +
            "# Customer checkout\n\n## Functional requirements\n\nCustomers can complete checkout.\n");
        Assert.Equal(0, environment.Builder.Build(participant.Path).ExitCode);

        var stale = environment.Service.Status(environment.Authority.Path);
        Assert.Equal("Stale", stale.Validation!.EffectiveStatus);
        Assert.Contains(stale.Validation.Errors, error =>
            error.Contains("not assessed", StringComparison.OrdinalIgnoreCase));

        var reconciled = environment.Service.Reconcile(environment.Authority.Path);
        Assert.Equal(0, reconciled.ExitCode);
        Assert.True(reconciled.Applied);
        Assert.Equal("Review Required", reconciled.Validation!.EffectiveStatus);
        var candidate = Assert.Single(
            reconciled.Discovery!.Candidates,
            item => item.Kind == "feature-specification");
        var content = File.ReadAllText(environment.CanonicalPath);
        Assert.Contains($"| {candidate.Id} | feature-specification |", content, StringComparison.Ordinal);
        Assert.Contains("| Unreviewed | TODO |", content, StringComparison.Ordinal);
        Assert.Contains("approved_by: null", content, StringComparison.Ordinal);

        content = content.Replace(
            "| Unreviewed | TODO |",
            "| Adopted | Reviewed with the product owner and incorporated into functional requirements |",
            StringComparison.Ordinal);
        content = content.Replace(
            "## Traceability\n\nStakeholders reviewed this section and recorded explicit, testable business requirements.",
            $"## Traceability\n\n- {candidate.Id}: incorporated into the checkout business requirements.",
            StringComparison.Ordinal);
        File.WriteAllText(environment.CanonicalPath, content);

        var ready = environment.Service.Validate(environment.Authority.Path);
        Assert.True(ready.Validation!.Valid);
        Assert.Equal("Ready for Approval", ready.Validation.EffectiveStatus);
        Assert.Equal(0, environment.Service.Approve(
            environment.Authority.Path,
            "Business owner",
            "Checkout feature absorbed into the BRD").ExitCode);
        Assert.Equal("Active", environment.Service.Status(environment.Authority.Path).Validation!.EffectiveStatus);
    }

    [Fact]
    public void Reconcile_AutoAdoptsCurrentHumanApprovedFeatureSpecification()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        Assert.Equal(0, environment.Service.Approve(
            environment.Authority.Path,
            "Business owner",
            "Initial requirements reviewed").ExitCode);
        var participant = environment.Participants[0];
        var feature = """
            ---
            title: Customer checkout
            type: feature-specification
            status: Active
            last_reviewed: 2026-08-20
            cis:
              approved_by: "Business owner"
              approved_at: "2026-08-20T10:00:00.0000000+00:00"
              approval_reason: "Checkout scope is accepted."
              approved_content_hash: null
            ---

            # Customer checkout

            ## Functional requirements

            Customers can complete checkout.
            """;
        feature = feature.Replace(
            "  approved_content_hash: null",
            $"  approved_content_hash: \"{ApprovalDigest(feature)}\"",
            StringComparison.Ordinal);
        participant.Write("docs/specs/customer-checkout-feature-spec.md", feature);
        Assert.Equal(0, environment.Builder.Build(participant.Path).ExitCode);

        var reconciled = environment.Service.Reconcile(environment.Authority.Path);

        Assert.Equal(0, reconciled.ExitCode);
        Assert.True(reconciled.Applied);
        Assert.True(reconciled.Validation!.Valid);
        Assert.Equal("Active", reconciled.Validation.EffectiveStatus);
        var candidate = Assert.Single(
            reconciled.Discovery!.Candidates,
            item => item.Kind == "feature-specification");
        var content = File.ReadAllText(environment.CanonicalPath);
        Assert.Contains(
            $"| {candidate.Id} | feature-specification |",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "| Adopted | Approved by Business owner: Checkout scope is accepted. |",
            content,
            StringComparison.Ordinal);
        Assert.Contains("<!-- cis:feature-traceability:start -->", content, StringComparison.Ordinal);
        Assert.Contains(candidate.Id, ExtractTraceability(content), StringComparison.Ordinal);
    }

    [Fact]
    public void Reconcile_PreservesBrdApprovalForApprovedAuthorityFeatureEvidenceOnly()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        Assert.Equal(0, environment.Service.Approve(
            environment.Authority.Path,
            "Business owner",
            "Initial requirements reviewed").ExitCode);
        var feature = """
            ---
            title: Customer checkout
            type: feature-specification
            status: Active
            last_reviewed: 2026-08-20
            cis:
              approved_by: "Business owner"
              approved_at: "2026-08-20T10:00:00.0000000+00:00"
              approval_reason: "Checkout scope is accepted."
              approved_content_hash: null
            ---

            # Customer checkout

            ## Functional requirements

            Customers can complete checkout without changing the approved business outcome.
            """;
        feature = feature.Replace(
            "  approved_content_hash: null",
            $"  approved_content_hash: \"{ApprovalDigest(feature)}\"",
            StringComparison.Ordinal);
        environment.Authority.Write("docs/specs/customer-checkout-feature-spec.md", feature);
        Assert.Equal(0, environment.Builder.Build(environment.Authority.Path).ExitCode);

        var reconciled = environment.Service.Reconcile(environment.Authority.Path);

        Assert.Equal(0, reconciled.ExitCode);
        Assert.True(reconciled.Applied);
        Assert.True(reconciled.Validation!.Valid);
        Assert.True(reconciled.Validation.Current);
        Assert.Equal("Active", reconciled.Validation.EffectiveStatus);
        var content = File.ReadAllText(environment.CanonicalPath);
        Assert.Contains("status: Active", content, StringComparison.Ordinal);
        Assert.Contains("approved_by: \"Business owner\"", content, StringComparison.Ordinal);
        Assert.Contains("| Adopted | Approved by Business owner: Checkout scope is accepted. |", content,
            StringComparison.Ordinal);
    }

    private static void CompleteHumanReview(string path, bool hasCandidate)
    {
        var content = File.ReadAllText(path);
        content = Regex.Replace(
            content,
            "(?m)^TODO: Complete .+$",
            "Stakeholders reviewed this section and recorded explicit, testable business requirements.",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        if (hasCandidate)
        {
            content = content.Replace(
                "| Unreviewed | TODO |",
                "| Reference | Reviewed as historical evidence; current statements are recorded below |",
                StringComparison.Ordinal);
        }

        File.WriteAllText(path, content);
    }

    private static string ApprovalDigest(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var key in new[] { "status", "last_reviewed" })
        {
            normalized = Regex.Replace(
                normalized,
                $"(?m)^{key}:.*$",
                $"{key}: <approval-metadata>",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }

        foreach (var key in new[] { "approved_by", "approved_at", "approval_reason", "approved_content_hash" })
        {
            normalized = Regex.Replace(
                normalized,
                $"(?m)^  {key}:.*$",
                $"  {key}: <approval-metadata>",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }

        return "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized.TrimEnd()))).ToLowerInvariant();
    }

    private static string ExtractTraceability(string content)
    {
        var match = Regex.Match(
            content,
            "(?ms)^## Traceability\\s*$\\n(?<body>.*?)(?=^## |\\z)",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["body"].Value : string.Empty;
    }

    private sealed class ReadyCheck : Cis.Abstractions.IChangeReadinessCheck
    {
        public Cis.Abstractions.ChangeReadinessResult Evaluate(string repositoryPath)
            => new("technical-intent", Applicable: true, Ready: true, []);
    }

    private sealed class WorkspaceEnvironment : IDisposable
    {
        private WorkspaceEnvironment(
            TemporaryRepository authority,
            IReadOnlyList<TemporaryRepository> participants,
            WorkspaceRegistry registry,
            GraphBuilder builder,
            BrdService service)
        {
            Authority = authority;
            Participants = participants;
            Registry = registry;
            Builder = builder;
            Service = service;
            AuthorityId = registry.Resolve(authority.Path).Workspace!.AuthorityRepository!.Id;
        }

        public TemporaryRepository Authority { get; }

        public IReadOnlyList<TemporaryRepository> Participants { get; }

        public WorkspaceRegistry Registry { get; }

        public GraphBuilder Builder { get; }

        public BrdService Service { get; }

        public string AuthorityId { get; }

        public string CanonicalPath => Path.Combine(
            Authority.Path,
            "docs",
            "specs",
            "business-requirements.md");

        public static WorkspaceEnvironment Create(
            int participants,
            Action<TemporaryRepository, int>? configureParticipant = null)
        {
            var authority = TemporaryRepository.Create();
            var participantRepositories = Enumerable.Range(1, participants)
                .Select(index =>
                {
                    var repository = TemporaryRepository.Create();
                    repository.Write(
                        $"src/Product{index}/Product{index}.csproj",
                        "<Project Sdk=\"Microsoft.NET.Sdk\" />");
                    repository.Write(
                        $"src/Product{index}/Product.cs",
                        $"public sealed class Product{index} {{ }}");
                    configureParticipant?.Invoke(repository, index);
                    return repository;
                })
                .ToArray();
            var resolver = new CisRepositoryContextResolver();
            var registry = new WorkspaceRegistry(resolver);
            var workspaceInitializer = new WorkspaceInitializer(new RepositoryInitializer(), registry);
            var initialized = workspaceInitializer.Initialize(new WorkspaceInitRequest(
                authority.Path,
                "docs",
                DryRun: false,
                Confirmed: true));
            Assert.Equal(0, initialized.ExitCode);
            var importer = new RepositoryImporter(new RepositoryInitializer(), registry);
            var imported = importer.Import(new RepositoryImportRequest(
                authority.Path,
                "docs/cis",
                participantRepositories.Select(repository => repository.Path).ToArray(),
                DryRun: false,
                Confirmed: true));
            Assert.Equal(0, imported.ExitCode);
            var builder = CreateBuilder(resolver);
            foreach (var repository in new[] { authority }.Concat(participantRepositories))
            {
                Assert.Equal(0, builder.Build(repository.Path).ExitCode);
            }

            var service = new BrdService(
                registry,
                resolver,
                new GraphValidator(resolver),
                new GraphSnapshotReader(resolver),
                new DocumentationCatalogMerger(),
                () => new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero));
            return new WorkspaceEnvironment(
                authority,
                participantRepositories,
                registry,
                builder,
                service);
        }

        public void Dispose()
        {
            foreach (var participant in Participants)
            {
                participant.Dispose();
            }

            Authority.Dispose();
        }

        private static GraphBuilder CreateBuilder(CisRepositoryContextResolver resolver)
        {
            var reader = new DocumentationCatalogReader();
            var inspector = new MarkdownDocumentInspector();
            var inventory = new DocumentationInventoryService(resolver, reader, inspector);
            var validation = new DocumentationValidationService(resolver, reader, inventory, inspector);
            return new GraphBuilder(resolver, reader, inventory, validation);
        }
    }

    public sealed class TemporaryRepository : IDisposable
    {
        private TemporaryRepository(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryRepository Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cis-brd-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryRepository(path);
        }

        public void Write(string relativePath, string content)
        {
            var absolutePath = System.IO.Path.Combine(
                Path,
                relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absolutePath)!);
            File.WriteAllText(absolutePath, content);
        }

        public void Dispose()
        {
            var fullPath = System.IO.Path.GetFullPath(Path);
            var safeParent = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-brd-tests")) +
                System.IO.Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
    }
}
