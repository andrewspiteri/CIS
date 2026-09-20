using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cis.Host;
using Cis.Abstractions;

namespace Cis.Modules.Brd.Tests;

public sealed class BrdWorkflowTests
{
    [Fact]
    public void SourceSummaries_AreLocalCachedAndInvalidatedByDocumentChanges()
    {
        const string evidence = "Customers can open fixed term deposits and select maturity instructions.";
        using var environment = WorkspaceEnvironment.Create(1, (repository, _) =>
            repository.Write("BRD.md", "# Business requirements\n\n## Overview\n\n" + evidence));
        environment.Service.Initialize(environment.Authority.Path, "Product BRD");
        var before = File.ReadAllText(environment.CanonicalPath);
        var initial = Assert.Single(environment.Service.Status(environment.Authority.Path).Validation!.SourceReviews);
        Assert.Equal("excerpt", initial.Summary!.Kind); Assert.Contains(evidence, initial.Summary.Text, StringComparison.Ordinal);
        var local = new SourceSummaryGeneration(evidence);
        var service = new BrdSourceSummaryService(environment.Service, environment.Registry, local);
        var generated = service.Generate(environment.Authority.Path);
        Assert.Equal(1, generated.Generated); Assert.Equal(1, local.Calls);
        Assert.Equal("local-model", Assert.Single(generated.Sources).Summary.Kind);
        Assert.False(local.Request!.AllowRemote); Assert.Equal("ollama", local.Request.Provider);
        Assert.Contains("untrusted evidence", local.Request.Prompt, StringComparison.Ordinal);
        Assert.Equal(0, service.Generate(environment.Authority.Path).Generated); Assert.Equal(1, local.Calls);
        Assert.Equal("local-model", Assert.Single(environment.Service.Status(environment.Authority.Path).Validation!.SourceReviews).Summary!.Kind);
        Assert.Equal(before, File.ReadAllText(environment.CanonicalPath));
        environment.Participants[0].Write("BRD.md", "# Business requirements\n\nUpdated deposit scope.");
        environment.Builder.Build(environment.Participants[0].Path);
        var updated = Assert.Single(environment.Service.Status(environment.Authority.Path).Validation!.SourceReviews);
        Assert.Equal("excerpt", updated.Summary!.Kind); Assert.NotEqual(initial.Summary.ContentHash, updated.Summary.ContentHash);
    }

    [Fact]
    public void SourceSummaries_KeepExcerptsWhenLocalModelIsUnavailableOrUnsupported()
    {
        const string evidence = "Customers can open fixed term deposits and select maturity instructions.";
        using var environment = WorkspaceEnvironment.Create(1, (repository, _) => repository.Write("BRD.md", "# Business requirements\n\n" + evidence));
        environment.Service.Initialize(environment.Authority.Path, "Product BRD");
        var remote = new SourceSummaryGeneration(evidence) { LocalAvailable = false };
        var result = new BrdSourceSummaryService(environment.Service, environment.Registry, remote).Generate(environment.Authority.Path);
        Assert.Equal(0, remote.Calls); Assert.Equal(0, result.Generated); Assert.NotEmpty(result.Warnings);
        Assert.Equal("excerpt", Assert.Single(result.Sources).Summary.Kind);
        var unsupported = new SourceSummaryGeneration("A quote that is absent from the document.");
        result = new BrdSourceSummaryService(environment.Service, environment.Registry, unsupported).Generate(environment.Authority.Path);
        Assert.Equal(0, result.Generated); Assert.NotEmpty(result.Warnings);
        var changed = new SourceSummaryGeneration(evidence) { BeforeReply = () => environment.Participants[0].Write("BRD.md", "# Business requirements\n\nChanged while summarizing.") };
        result = new BrdSourceSummaryService(environment.Service, environment.Registry, changed).Generate(environment.Authority.Path);
        Assert.Equal(0, result.Generated); Assert.Contains(result.Warnings, warning => warning.Contains("changed during summarization", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceSummaries_DoNotReadOutsideRootsOrSendSensitiveMaterial()
    {
        using var environment = WorkspaceEnvironment.Create(1, (repository, _) =>
            repository.Write("BRD.md", "# Business requirements\n\n-----BEGIN PRIVATE KEY-----\nSensitive fixture\n-----END PRIVATE KEY-----"));
        environment.Service.Initialize(environment.Authority.Path, "Product BRD");
        var local = new SourceSummaryGeneration("Sensitive fixture");
        var result = new BrdSourceSummaryService(environment.Service, environment.Registry, local).Generate(environment.Authority.Path);
        Assert.Equal(0, local.Calls); Assert.Equal("unavailable", Assert.Single(result.Sources).Summary.Kind);
        Assert.DoesNotContain("PRIVATE KEY", result.Sources[0].Summary.Text, StringComparison.Ordinal);
        var candidate = new BrdCandidate("source", "brd", "repo", environment.Authority.Path, "../outside.md", "hash", [], false);
        Assert.False(BrdSourceSummaryService.Describe(environment.Authority.Path, candidate).CanGenerate);
        Assert.Equal("repository", BrdSourceSummaryService.Describe(environment.Authority.Path, candidate with { Path = "workspace:repo" }).Kind);
    }

    [Fact]
    public void SourceSummaries_FallBackToOriginalSentencesAndExcludeDocumentMetadata()
    {
        const string first = "Customers can open fixed term deposits and select maturity instructions.";
        const string second = "Backoffice staff can review each deposit before its activation is completed.";
        using var environment = WorkspaceEnvironment.Create(1, (repository, _) => repository.Write("BRD.md",
            "# Design\n\n| Ticket | Author | Date | Status | Weight |\n|---|---|---|---|---|\n"
            + "| KAN-112 | Example | 2026-09-11 | Draft | Medium |\n\n## 1. Business Requirements\n\n"
            + "| ID | Requirement |\n|---|---|\n| BR-01 | " + first + " |\n| BR-02 | " + second + " |"));
        environment.Service.Initialize(environment.Authority.Path, "Product BRD");
        var local = new SourceSummaryGeneration("A fabricated quote which does not appear in the source.") { SelectSentences = true };
        var result = new BrdSourceSummaryService(environment.Service, environment.Registry, local).Generate(environment.Authority.Path);
        Assert.Equal(1, result.Generated); Assert.Equal(2, local.Calls); Assert.Empty(result.Warnings);
        Assert.Equal(first + " " + second, Assert.Single(result.Sources).Summary.Text);
        Assert.DoesNotContain("KAN-112", local.Request!.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("BR-01", local.Request.Prompt, StringComparison.Ordinal);
        Assert.False(local.Request.AllowRemote);
    }

    private sealed class SourceSummaryGeneration(string quote) : ICisTextGenerationService
    {
        public int Calls { get; private set; }
        public bool LocalAvailable { get; init; } = true;
        public bool SelectSentences { get; init; }
        public Action? BeforeReply { get; init; }
        public CisTextGenerationRequest? Request { get; private set; }
        public CisAiStatus GetStatus() => new([
            new("remote", "available", "https://remote.invalid", false, [new("remote-model")], null),
            new("ollama", LocalAvailable ? "available" : "unavailable", "http://localhost:11434", true, [new("fixture-model")], null),
        ]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request)
        {
            Calls++; Request = request; BeforeReply?.Invoke();
            if (SelectSentences && request.Prompt.Contains("sentenceIds", StringComparison.Ordinal))
                return new("generated", "ollama", "fixture-model", "{\"sentenceIds\":[1,2]}", null, true);
            return new("generated", "ollama", "fixture-model", System.Text.Json.JsonSerializer.Serialize(new {
                summary = "The document describes opening fixed term deposits and choosing what happens when they mature.", evidenceQuotes = new[] { quote },
            }), null, true);
        }
    }

    [Fact]
    public void SourceDecisions_SaveOnlyExplicitRowsAndPreserveTheRestOfTheDocument()
    {
        using var environment = WorkspaceEnvironment.Create(1, (repository, _) => {
            repository.Write("BRD-one.md", "# Business requirements\n\nDeposit opening.\n");
            repository.Write("BRD-two.md", "# Business requirements\n\nDeposit maturity.\n");
        });
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        var before = File.ReadAllText(environment.CanonicalPath);
        var sources = environment.Service.Status(environment.Authority.Path).Validation!.SourceReviews;
        var decisions = sources.Select((source, index) => new CisBrdSourceDecision(source.Id,
            index == 0 ? "Adopted" : "Reference", "Used for deposit rules | independently checked.\nBusiness context.", source.ReviewToken!)).ToArray();
        var saved = environment.Service.AssessSources(environment.Authority.Path, decisions);
        Assert.Equal(0, saved.ExitCode); Assert.True(saved.Applied);
        var after = File.ReadAllText(environment.CanonicalPath);
        var ignoredLines = sources.Select(source => source.AssessmentLine!.Value - 1).ToHashSet();
        Assert.Equal(before.Split('\n').Where((_, index) => !ignoredLines.Contains(index)),
            after.Split('\n').Where((_, index) => !ignoredLines.Contains(index)));
        var reviewed = environment.Service.Status(environment.Authority.Path).Validation!.SourceReviews;
        Assert.All(reviewed, source => {
            Assert.False(source.NeedsReview);
            Assert.Equal("Used for deposit rules | independently checked. Business context.", source.Rationale);
            Assert.NotEqual(sources.Single(item => item.Id == source.Id).ReviewToken, source.ReviewToken);
        });
        Assert.NotEqual("Active", environment.Service.Status(environment.Authority.Path).Validation!.DocumentStatus);
    }

    [Fact]
    public void SourceDecisions_RejectStaleDuplicateAndInvalidBatchesWithoutPartialWrites()
    {
        using var environment = WorkspaceEnvironment.Create(1, (repository, _) => {
            repository.Write("BRD-one.md", "# Business requirements\n\nDeposit opening.\n");
            repository.Write("BRD-two.md", "# Business requirements\n\nDeposit maturity.\n");
        });
        environment.Service.Initialize(environment.Authority.Path, "Product BRD");
        var sources = environment.Service.Status(environment.Authority.Path).Validation!.SourceReviews;
        var first = new CisBrdSourceDecision(sources[0].Id, "Reference", "Background for deposit operations.", sources[0].ReviewToken!);
        var second = new CisBrdSourceDecision(sources[1].Id, "Rejected", "Superseded policy outside this product.", sources[1].ReviewToken!);
        var before = File.ReadAllText(environment.CanonicalPath);
        foreach (var batch in new[] {
            new[] { first, second with { ReviewToken = "stale" } }, new[] { first, first },
            new[] { first, second with { Reason = "TODO" } }, new[] { first, second with { Assessment = "Anything" } },
            new[] { first, second with { Id = "unknown" } }, new[] { first, second with { Reason = "--> injected" } },
        }) {
            var rejected = environment.Service.AssessSources(environment.Authority.Path, batch);
            Assert.NotEqual(0, rejected.ExitCode); Assert.False(rejected.Applied);
            Assert.Equal(before, File.ReadAllText(environment.CanonicalPath));
        }
        Assert.True(environment.Service.AssessSources(environment.Authority.Path, [first]).Applied);
        var latest = File.ReadAllText(environment.CanonicalPath);
        Assert.False(environment.Service.AssessSources(environment.Authority.Path, [first, second]).Applied);
        Assert.Equal(latest, File.ReadAllText(environment.CanonicalPath));
        Assert.True(environment.Service.AssessSources(environment.Authority.Path, [second]).Applied);
    }

    [Fact]
    public void SourceDecisions_CommandDispatchAndChangedEvidenceAreChecked()
    {
        using var environment = WorkspaceEnvironment.Create(1, (repository, _) =>
            repository.Write("BRD.md", "# Business requirements\n\nDeposit opening.\n"));
        environment.Service.Initialize(environment.Authority.Path, "Product BRD");
        var source = Assert.Single(environment.Service.Status(environment.Authority.Path).Validation!.SourceReviews);
        var decision = new CisBrdSourceDecision(source.Id, "Reference", "Reviewed deposit context.", source.ReviewToken!);
        var input = Path.Combine(environment.Authority.Path, "decisions.json");
        File.WriteAllText(input, System.Text.Json.JsonSerializer.Serialize(new[] { decision },
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new WorkspaceModule())
            .AddModule(new DocsModule()).AddModule(new GraphModule()).AddModule(new BrdModule()).Build();
        Assert.Equal(0, application.Invoke(["brd", "sources", "assess", "--input", input, "--workspace", environment.Authority.Path, "--format", "json"]));
        var current = Assert.Single(environment.Service.Status(environment.Authority.Path).Validation!.SourceReviews);
        environment.Participants[0].Write("BRD.md", "# Business requirements\n\nDifferent deposit policy.\n");
        environment.Builder.Build(environment.Participants[0].Path);
        var before = File.ReadAllText(environment.CanonicalPath);
        Assert.False(environment.Service.AssessSources(environment.Authority.Path, [decision with { ReviewToken = current.ReviewToken! }]).Applied);
        Assert.Equal(before, File.ReadAllText(environment.CanonicalPath));
        File.WriteAllText(input, "not-json");
        Assert.NotEqual(0, application.Invoke(["brd", "sources", "assess", "--input", input, "--workspace", environment.Authority.Path]));
    }

    [Fact]
    public void Status_ProjectsSourceDecisionsAndEditorLocationsWithoutChangingTheBrd()
    {
        using var environment = WorkspaceEnvironment.Create(1, (repository, _) =>
            repository.Write("BRD.md", "# Business requirements\n\nCustomers open fixed term deposits.\n"));
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        var before = File.ReadAllText(environment.CanonicalPath);
        var status = environment.Service.Status(environment.Authority.Path);
        var source = Assert.Single(status.Validation!.SourceReviews, item => item.Path == "BRD.md");
        Assert.True(source.NeedsReview);
        Assert.False(source.RequiresReconciliation);
        Assert.Equal(2, source.Issues.Count);
        Assert.Contains(source.Id, before.Split('\n')[source.AssessmentLine!.Value - 1], StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(environment.CanonicalPath));
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: true);
        var reviewed = environment.Service.Status(environment.Authority.Path);
        Assert.False(Assert.Single(reviewed.Validation!.SourceReviews, item => item.Id == source.Id).NeedsReview);
    }

    [Fact]
    public void BacklogValidation_FailsWhenAnUpstreamGateMakesTheBacklogNonCurrent()
    {
        var result = new BrdBacklogResult("validated", "workspace", "authority", "docs/plans/high-level-backlog.md", [],
            new BrdBacklogValidation(true, false, "Review Required", "Review Required", [],
                ["The Active solution-design bundle is required."]), [], false);

        Assert.Equal(5, result.ExitCode);
    }

    [Fact]
    public void BacklogValidate_ReportsBlockedWhenSolutionDesignReadinessIsLost()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 0);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        var content = Regex.Replace(File.ReadAllText(environment.CanonicalPath),
            "(?ms)^## Functional requirements\\s*$.*?(?=^## )",
            "## Functional requirements\n\n- **BR-FR-001 — Assess risk:** An analyst shall assess one transaction.\n\n",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        File.WriteAllText(environment.CanonicalPath, content);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Product owner", "Scope accepted").ExitCode);
        var readiness = new MutableReadyCheck();
        var backlog = new BrdBacklogService(environment.Service, environment.Registry,
            new CisRepositoryContextResolver(), new DocumentationCatalogMerger(), [readiness]);
        Assert.Equal("built", backlog.Build(environment.Authority.Path).Status);

        readiness.Ready = false;
        var validation = backlog.Validate(environment.Authority.Path);

        Assert.Equal("blocked", validation.Status);
        Assert.Equal(5, validation.ExitCode);
        Assert.True(validation.Validation!.Valid);
        Assert.False(validation.Validation.Current);
    }

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
        Assert.Equal(0, application.Invoke(["brd", "sources", "assess", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "sources", "summarize", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "validate", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "questions", "list", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "questions", "guidance", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "questions", "suggest", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "questions", "answer", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "review", "init", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "review", "status", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "review", "freshness", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "review", "decide", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "review", "accept-all", "--help"]));
        Assert.Equal(0, application.Invoke(["brd", "review", "approve", "--help"]));
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
    public void OpenQuestions_AreStructuredAnsweredAndRequiredBeforeApproval()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        var content = File.ReadAllText(environment.CanonicalPath);
        content = Regex.Replace(content,
            "(?ms)^## Open questions\\s*$.*?(?=^## |\\z)",
            "## Open questions\n\n1. Who owns the product outcome?\n2. What launch boundary is accepted?\n",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        File.WriteAllText(environment.CanonicalPath, content);

        var listed = environment.Service.Questions(environment.Authority.Path);
        Assert.Equal("unanswered", listed.Status);
        Assert.Equal(2, listed.UnansweredCount);
        Assert.Equal("BRD-Q-001", listed.Questions[0].Id);
        var blocked = environment.Service.Validate(environment.Authority.Path);
        Assert.False(blocked.Validation!.Valid);
        Assert.Contains(blocked.Validation.Errors, error => error.Contains("BRD-Q-001 is unanswered", StringComparison.Ordinal));

        var first = environment.Service.AnswerQuestion(environment.Authority.Path, "BRD-Q-001",
            "The accountable business owner owns the outcome | delegated reviewers advise.", "Andrew Spiteri");
        Assert.True(first.Applied);
        Assert.Equal(1, first.UnansweredCount);
        Assert.Contains("| BRD-Q-001 | Who owns the product outcome? | The accountable business owner owns the outcome \\| delegated reviewers advise. | Andrew Spiteri |",
            File.ReadAllText(environment.CanonicalPath), StringComparison.Ordinal);
        Assert.Equal("The accountable business owner owns the outcome | delegated reviewers advise.",
            environment.Service.Questions(environment.Authority.Path).Questions[0].Answer);

        var second = environment.Service.AnswerQuestion(environment.Authority.Path, "BRD-Q-002",
            "A reviewed research prototype is the accepted launch boundary.", "Andrew Spiteri");
        Assert.Equal("answered", second.Status);
        Assert.Equal(0, second.UnansweredCount);
        Assert.True(environment.Service.Validate(environment.Authority.Path).Validation!.Valid);
        var unchanged = environment.Service.AnswerQuestion(environment.Authority.Path, "BRD-Q-002",
            "A reviewed research prototype is the accepted launch boundary.", "Andrew Spiteri");
        Assert.False(unchanged.Applied);
    }

    [Fact]
    public void QuestionGuidance_ProvidesContextAndRetainsOnlyDigestBoundAdvisorySuggestions()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        var content = File.ReadAllText(environment.CanonicalPath);
        content = Regex.Replace(content,
            "(?ms)^## Stakeholders and actors\\s*$.*?(?=^## |\\z)",
            "## Stakeholders and actors\n\nThe business sponsor is accountable for the product outcome.\n\n",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        content = Regex.Replace(content,
            "(?ms)^## Open questions\\s*$.*?(?=^## |\\z)",
            "## Open questions\n\n1. Who owns the product outcome?\n2. What launch date is accepted?\n",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        File.WriteAllText(environment.CanonicalPath, content);
        var generation = new FakeQuestionGenerationService();
        var service = new BrdQuestionGuidanceService(environment.Service, environment.Registry, generation,
            () => DateTimeOffset.Parse("2026-09-01T08:00:00Z"));

        var initial = service.Guidance(environment.Authority.Path);
        var generated = service.Suggest(environment.Authority.Path, null, null, allowRemote: false);
        environment.Service.AnswerQuestion(environment.Authority.Path, "BRD-Q-001",
            "The business sponsor owns the product outcome.", "Andrew Spiteri");
        var afterAnswer = service.Guidance(environment.Authority.Path);
        File.WriteAllText(environment.CanonicalPath, File.ReadAllText(environment.CanonicalPath)
            .Replace("business sponsor is accountable", "product council is accountable", StringComparison.Ordinal));
        var stale = service.Guidance(environment.Authority.Path);

        Assert.Equal("missing", initial.SuggestionStatus);
        Assert.Contains(initial.Questions[0].Context,
            item => item.Section == "Stakeholders and actors" && item.Excerpt.Contains("business sponsor", StringComparison.Ordinal));
        Assert.True(generated.Applied);
        Assert.Equal("current", generated.SuggestionStatus);
        Assert.Equal(1, generated.SuggestedCount);
        Assert.Equal("The business sponsor owns the product outcome.", generated.Questions[0].SuggestedAnswer);
        Assert.Null(generated.Questions[1].SuggestedAnswer);
        Assert.Equal("insufficient", generated.Questions[1].SuggestionConfidence);
        Assert.NotNull(generation.LastRequest);
        Assert.False(generation.LastRequest!.AllowRemote);
        Assert.True(generation.LastRequest.JsonMode);
        Assert.Contains("Do not invent owners, dates, targets", generation.LastRequest.Prompt, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(environment.Authority.Path,
            BrdQuestionGuidanceService.SuggestionPath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal("current", afterAnswer.SuggestionStatus);
        Assert.Equal("stale", stale.SuggestionStatus);
        Assert.All(stale.Questions, question => Assert.Null(question.SuggestedAnswer));
    }

    [Fact]
    public void QuestionGuidance_RetriesMalformedMultiQuestionOutputWithoutDiscardingValidSuggestions()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        var content = File.ReadAllText(environment.CanonicalPath);
        content = Regex.Replace(content,
            "(?ms)^## Stakeholders and actors\\s*$.*?(?=^## |\\z)",
            "## Stakeholders and actors\n\nThe business sponsor is accountable for the product outcome.\n\n",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        content = Regex.Replace(content,
            "(?ms)^## Open questions\\s*$.*?(?=^## |\\z)",
            "## Open questions\n\n1. Who owns the product outcome?\n2. What launch date is accepted?\n",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        File.WriteAllText(environment.CanonicalPath, content);
        var generation = new BatchRejectingQuestionGenerationService();
        var service = new BrdQuestionGuidanceService(environment.Service, environment.Registry, generation);

        var generated = service.Suggest(environment.Authority.Path, null, null, allowRemote: false);

        Assert.True(generated.Applied);
        Assert.Equal("current", generated.SuggestionStatus);
        Assert.Equal(1, generated.SuggestedCount);
        Assert.Equal("The business sponsor owns the product outcome.", generated.Questions[0].SuggestedAnswer);
        Assert.Null(generated.Questions[1].SuggestedAnswer);
        Assert.Contains("confidence was too low", generated.Questions[1].SuggestionReason, StringComparison.Ordinal);
        Assert.Equal(3, generation.Requests.Count);
        Assert.All(generation.Requests, request => Assert.True(request.JsonMode));
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
    public void RegisteredAuthoringEvidence_ReplacesTheNoSourceSentinelDuringReconciliation()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1,
            sourceProviders: [new FakeSourceEvidenceProvider()]);

        var result = environment.Service.Initialize(environment.Authority.Path, "Product BRD");

        Assert.Equal(0, result.ExitCode);
        var content = File.ReadAllText(environment.CanonicalPath);
        Assert.Contains("| BRD-SRC-001122334455 | authoring-reference |", content, StringComparison.Ordinal);
        Assert.Contains("| sha256:registered | Reference | Controller-selected proposal evidence |", content, StringComparison.Ordinal);
        Assert.DoesNotContain("No source evidence exists", content, StringComparison.Ordinal);
    }

    private sealed class FakeSourceEvidenceProvider : ICisBrdSourceEvidenceProvider
    {
        public IReadOnlyList<CisBrdSourceEvidence> Discover(CisRepositoryContext context) =>
        [new("BRD-SRC-001122334455", "authoring-reference", context.RepositoryId, context.RepositoryPath,
            "docs/ref/proposal.docx", "sha256:registered", "sha256:registered", "Reference",
            "Controller-selected proposal evidence", false, ["registered-source-evidence", "docx-projection"])];
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
    public void Discover_ExcludesAgentInstructionsAndSkillsFromBusinessSources()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1,
            configureParticipant: (repository, _) =>
            {
                foreach (var path in new[] { ".claude/skills/tdd/SKILL.md", ".agents/skills/brd/reference.md",
                    ".codex/BRD.md", "skills/tdd/references/BRD.md", "AGENTS.md", "CLAUDE.md", "SKILL.md",
                    "node_modules/library/BRD.md", "web/.next/output/BRD.md", "src/bin/output/BRD.md" })
                    repository.Write(path, "# Business Requirements Document\n\nInstructions for producing a BRD, not business source evidence.\n");
                repository.Write("legacy/BRD.md", "# Business Requirements Document\n\nHistorical product requirements.\n");
            });

        var result = environment.Service.Discover(environment.Authority.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("legacy/BRD.md", Assert.Single(result.Candidates).Path);
    }

    [Fact]
    public void Discover_DoesNotAbsorbBusinessAuthorityFromDependencyRepository()
    {
        using var environment = WorkspaceEnvironment.Create(
            participants: 1,
            configureParticipant: (repository, _) => repository.Write(
                "legacy/BRD.md",
                "# Core Banking Business Requirements\n\nRequirements owned by another product.\n"),
            dependencyIndexes: new HashSet<int> { 1 });

        var result = environment.Service.Discover(environment.Authority.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("missing", result.Status);
        Assert.Empty(result.Candidates);
        Assert.Single(result.Baselines, baseline => baseline.Role == "dependency");
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
    public void Status_DetectsParticipantContentChangesWithUnchangedLengthAndTimestamp()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1,
            configureParticipant: (repository, _) => repository.Write("src/Product/Changed.cs", "public sealed class BeforeX { }"));
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        var path = Path.Combine(environment.Participants[0].Path, "src/Product/Changed.cs");
        var timestamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "public sealed class AfterXX { }");
        File.SetLastWriteTimeUtc(path, timestamp);

        var result = environment.Service.Status(environment.Authority.Path);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("graph is stale", StringComparison.Ordinal));
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
    public void Backlog_NoPlannedWorkRequiresHumanChoicePreservesScopeAndDetectsDrift()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 0);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Existing product").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        var brd = Regex.Replace(File.ReadAllText(environment.CanonicalPath), "(?ms)^## Functional requirements\\s*$.*?(?=^## )",
            "## Functional requirements\n\n- **BR-FR-001 — View deposits:** A customer shall view deposits.\n\n");
        File.WriteAllText(environment.CanonicalPath, brd);
        var service = new BrdBacklogService(environment.Service, environment.Registry,
            new CisRepositoryContextResolver(), new DocumentationCatalogMerger(), [new ReadyCheck()]);
        Assert.NotEqual(0, service.Build(environment.Authority.Path, "unknown", null).ExitCode);
        Assert.NotEqual(0, service.Build(environment.Authority.Path, "no-planned-work", null).ExitCode);
        Assert.Equal(5, service.Build(environment.Authority.Path, "no-planned-work", "Owner").ExitCode);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Owner", "Baseline reviewed").ExitCode);
        var result = service.Build(environment.Authority.Path, "no-planned-work", "Owner");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Items);
        Assert.True(result.Validation!.Valid);
        Assert.True(result.Validation.Current);
        Assert.Equal("Ready for Approval", result.Validation.EffectiveStatus);
        Assert.Equal("no-planned-work", result.Mode);
        Assert.Equal("Owner", result.NoPlannedWorkBy);
        var path = Path.Combine(environment.Authority.Path, result.RelativePath!);
        var recorded = File.ReadAllText(path);
        Assert.False(service.Build(environment.Authority.Path).Applied);
        Assert.False(service.Build(environment.Authority.Path, "no-planned-work", "Owner").Applied);
        Assert.Equal(recorded, File.ReadAllText(path));
        Assert.Equal("Active", service.Approve(environment.Authority.Path, "Owner", "Empty scope reviewed").Validation!.EffectiveStatus);
        var approved = File.ReadAllText(path);
        Assert.Empty(service.Build(environment.Authority.Path).Items);
        Assert.Equal(approved, File.ReadAllText(path));
        File.AppendAllText(path, "\n## Human notes\nKeep the support roadmap separate.\n");
        Assert.False(service.Status(environment.Authority.Path).Validation!.Current);
        var technical = Path.Combine(Path.GetDirectoryName(environment.CanonicalPath)!, "technical-intent-spec.md");
        File.AppendAllText(technical, "\nA newly reviewed technical constraint.\n");
        var beforeRefresh = File.ReadAllText(path);
        Assert.Equal(5, service.Build(environment.Authority.Path).ExitCode);
        Assert.Equal(beforeRefresh, File.ReadAllText(path));
        var renewed = service.Build(environment.Authority.Path, "no-planned-work", "Owner");
        Assert.Equal(0, renewed.ExitCode);
        Assert.True(renewed.Validation!.Current);
        Assert.Equal("Ready for Approval", renewed.Validation.EffectiveStatus);
        Assert.Contains("Keep the support roadmap separate.", File.ReadAllText(path), StringComparison.Ordinal);
        var good = File.ReadAllText(path);
        File.WriteAllText(path, Regex.Replace(good, "(?m)^  no_planned_work_by:.*$", "  no_planned_work_by: null"));
        Assert.False(service.Validate(environment.Authority.Path).Validation!.Valid);
        File.WriteAllText(path, good);
        var candidates = service.Build(environment.Authority.Path, "requirements", null);
        Assert.Single(candidates.Items);
        Assert.Equal("requirements", candidates.Mode);
        Assert.Contains("Keep the support roadmap separate.", File.ReadAllText(path), StringComparison.Ordinal);
        var planned = File.ReadAllText(path);
        Assert.Equal(5, service.Build(environment.Authority.Path, "no-planned-work", "Owner").ExitCode);
        Assert.Equal(planned, File.ReadAllText(path));
        File.WriteAllText(path, planned.Replace("backlog_mode: requirements", "backlog_mode: no-planned-work", StringComparison.Ordinal));
        Assert.False(service.Validate(environment.Authority.Path).Validation!.Valid);
    }

    [Fact]
    public void Backlog_BuildsFromNarrativeBusinessRequirementsAndPreservesReadableOutcomes()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Research prototype BRD").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        var content = File.ReadAllText(environment.CanonicalPath);
        content = Regex.Replace(content,
            "(?ms)^## Functional requirements\\s*$.*?(?=^## )",
            "## Functional requirements\n\n" +
            "- **BR-FR-001 — Customer risk assessment:** The prototype shall assess a customer using authorized BI data\n" +
            "  and retain the source correlation for review.\n" +
            "- **BR-FR-002:** The prototype shall explain the influential relationships to an analyst.\n\n",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        content = Regex.Replace(content,
            "(?ms)^## Quality, regulatory, and operational requirements\\s*$.*?(?=^## )",
            "## Quality, regulatory, and operational requirements\n\n" +
            "- **BR-NFR-001 — Audit retention:** Assessment evidence shall be retained for ten years.\n\n",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        File.WriteAllText(environment.CanonicalPath, content);
        Assert.Equal(0, environment.Service.Approve(
            environment.Authority.Path,
            "Product owner",
            "Narrative requirements accepted").ExitCode);
        var backlog = new BrdBacklogService(environment.Service, environment.Registry,
            new CisRepositoryContextResolver(), new DocumentationCatalogMerger(), [new ReadyCheck()],
            () => new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));

        var built = backlog.Build(environment.Authority.Path);

        Assert.Equal(0, built.ExitCode);
        Assert.Equal("built", built.Status);
        Assert.Equal(2, built.Items.Count);
        Assert.Contains(built.Items, item => item.Id == "HLT-FR-001"
            && item.RequirementId == "BR-FR-001"
            && item.Outcome == "Customer risk assessment");
        Assert.Contains(built.Items, item => item.Id == "HLT-FR-002"
            && item.RequirementId == "BR-FR-002"
            && item.Outcome == "The prototype shall explain the influential relationships to an analyst.");
        var backlogPath = Path.Combine(environment.Authority.Path, "docs", "plans", "high-level-backlog.md");
        var backlogContent = File.ReadAllText(backlogPath);
        Assert.Contains("BR-NFR-001", backlogContent, StringComparison.Ordinal);
        Assert.Contains("Assessment evidence shall be retained for ten years.", backlogContent, StringComparison.Ordinal);
        Assert.Equal("Ready for Approval", built.Validation!.EffectiveStatus);
    }

    [Fact]
    public void Backlog_RoutesGreenfieldAuthorityWhenNoParticipantRepositoryExists()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 0);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Greenfield BRD").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        var content = Regex.Replace(File.ReadAllText(environment.CanonicalPath),
            "(?ms)^## Functional requirements\\s*$.*?(?=^## )",
            "## Functional requirements\n\n" +
            "- **BR-FR-001 — Create assessment:** An authorized analyst shall create a risk assessment.\n\n",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        File.WriteAllText(environment.CanonicalPath, content);
        Assert.Equal(0, environment.Service.Approve(
            environment.Authority.Path,
            "Product owner",
            "Greenfield requirement accepted").ExitCode);
        var backlog = new BrdBacklogService(environment.Service, environment.Registry,
            new CisRepositoryContextResolver(), new DocumentationCatalogMerger(), [new ReadyCheck()],
            () => new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));

        var built = backlog.Build(environment.Authority.Path);

        Assert.Equal(0, built.ExitCode);
        var item = Assert.Single(built.Items);
        Assert.Equal([environment.AuthorityId], item.Repositories);
        Assert.True(built.Validation!.Valid, string.Join(Environment.NewLine, built.Validation.Errors));
        Assert.Equal("Ready for Approval", built.Validation.EffectiveStatus);
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
        var productDefinition = new MutableProductDefinitionAuthority
        {
            Active = false,
            BaselineHash = "sha256:product-definition-v1",
            Errors = ["The complete high-level product definition has not been activated."],
        };
        var backlog = new BrdBacklogService(environment.Service, environment.Registry,
            new CisRepositoryContextResolver(), new DocumentationCatalogMerger(), [new ReadyCheck()],
            () => new DateTimeOffset(2026, 8, 9, 13, 0, 0, TimeSpan.Zero),
            productDefinitionAuthorities: [productDefinition]);
        Assert.Equal("built", backlog.Build(environment.Authority.Path).Status);
        Assert.Equal("approved", backlog.Approve(environment.Authority.Path, "Product owner", "High-level outcomes accepted").Status);

        var blocked = backlog.StartFeature(environment.Authority.Path, "HLT-FR-001");
        Assert.Equal("blocked", blocked.Status);
        Assert.Contains(blocked.Errors, error => error.Contains("product definition", StringComparison.OrdinalIgnoreCase));
        productDefinition.Active = true;
        productDefinition.Errors = [];
        var started = backlog.StartFeature(environment.Authority.Path, "HLT-FR-001");
        var path = Path.Combine(environment.Authority.Path, started.RelativePath!.Replace('/', Path.DirectorySeparatorChar));
        Assert.Contains("product_definition_hash: sha256:product-definition-v1", File.ReadAllText(path), StringComparison.Ordinal);

        var incomplete = backlog.ValidateFeature(environment.Authority.Path, "HLT-FR-001");
        Assert.Equal(5, incomplete.ExitCode);
        Assert.False(incomplete.Validation!.Valid);
        Assert.Contains(incomplete.Validation.Errors, error => error.Contains("placeholder", StringComparison.OrdinalIgnoreCase));

        File.WriteAllText(path, File.ReadAllText(path).Replace("TODO", "Reviewed", StringComparison.Ordinal));
        var ready = backlog.ValidateFeature(environment.Authority.Path, "HLT-FR-001");
        Assert.True(ready.ExitCode == 0, string.Join(Environment.NewLine, ready.Validation?.Errors ?? ready.Errors));
        Assert.Equal("Ready for Approval", ready.Validation!.EffectiveStatus);

        productDefinition.BaselineHash = "sha256:product-definition-v2";
        var wrongProductBaseline = backlog.ValidateFeature(environment.Authority.Path, "HLT-FR-001");
        Assert.False(wrongProductBaseline.Validation!.Valid);
        Assert.Equal("Review Required", wrongProductBaseline.Validation.EffectiveStatus);
        Assert.Contains(wrongProductBaseline.Validation.Errors, error =>
            error.Contains("Product-definition baseline", StringComparison.OrdinalIgnoreCase));
        productDefinition.BaselineHash = "sha256:product-definition-v1";

        var backlogPath = Path.Combine(environment.Authority.Path, "docs", "plans", "high-level-backlog.md");
        var approvedBacklog = File.ReadAllText(backlogPath);
        File.WriteAllText(backlogPath, Regex.Replace(approvedBacklog, "(?m)^  brd_hash:.*$", "  brd_hash: sha256:stale"));
        var upstreamStale = backlog.ValidateFeature(environment.Authority.Path, "HLT-FR-001");
        Assert.Equal("Review Required", upstreamStale.Validation!.EffectiveStatus);
        Assert.False(upstreamStale.Validation.Current);
        File.WriteAllText(backlogPath, approvedBacklog);

        // Approval metadata must remain portable when a Windows checkout writes CRLF.
        var windowsFeature = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal);
        File.WriteAllText(path, windowsFeature);
        var approved = backlog.ApproveFeature(environment.Authority.Path, "HLT-FR-001", "Product owner", "Feature scope accepted");
        Assert.Equal("approved", approved.Status);
        Assert.Equal("Active", approved.Validation!.EffectiveStatus);
        Assert.Equal("unchanged", backlog.ApproveFeature(environment.Authority.Path, "HLT-FR-001", "Product owner", "Feature scope accepted").Status);
        var carriedAuthority = new BrdFeatureApprovalAuthority(backlog).Evaluate(
            environment.Authority.Path, started.RelativePath!);
        Assert.True(carriedAuthority.Applicable, string.Join(Environment.NewLine, carriedAuthority.Errors));
        Assert.True(carriedAuthority.Ready, string.Join(Environment.NewLine, carriedAuthority.Errors));
        Assert.Equal("Product owner", carriedAuthority.Reviewer);
        Assert.Equal("HLT-FR-001", carriedAuthority.ItemId);

        File.WriteAllText(path, File.ReadAllText(path).Replace("Deliver `HLT-FR-001`", "Provide `HLT-FR-001`", StringComparison.Ordinal));
        var stale = backlog.FeatureStatus(environment.Authority.Path, "HLT-FR-001");
        Assert.Equal("Stale", stale.Validation!.EffectiveStatus);
        Assert.False(stale.Validation.Current);
        Assert.False(new BrdFeatureApprovalAuthority(backlog)
            .Evaluate(environment.Authority.Path, started.RelativePath!).Ready);
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
    public void Reconcile_HidesLegacyEvidenceWithoutChangingBusinessDigestOrApproval()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        var legacy = CisBrdPresentation.RestoreManagedEvidence(File.ReadAllText(environment.CanonicalPath));
        File.WriteAllText(environment.CanonicalPath, legacy);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Business owner", "Product reviewed").ExitCode);
        var before = File.ReadAllText(environment.CanonicalPath);

        var reconciled = environment.Service.Reconcile(environment.Authority.Path);
        var after = File.ReadAllText(environment.CanonicalPath);

        Assert.Equal(0, reconciled.ExitCode);
        Assert.True(reconciled.Validation!.Valid);
        Assert.Equal("Active", reconciled.Validation.EffectiveStatus);
        Assert.Contains("<!-- cis:brd-evidence", after, StringComparison.Ordinal);
        Assert.Equal(BrdDocumentDigest.Compute(before), BrdDocumentDigest.Compute(after));
        Assert.DoesNotContain("| Repository |", Regex.Replace(after, "<!--.*?-->", string.Empty, RegexOptions.Singleline), StringComparison.Ordinal);
        Assert.Equal("unchanged", environment.Service.Reconcile(environment.Authority.Path).Status);
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

    private sealed class MutableReadyCheck : Cis.Abstractions.IChangeReadinessCheck
    {
        public bool Ready { get; set; } = true;

        public Cis.Abstractions.ChangeReadinessResult Evaluate(string repositoryPath)
            => new("solution-design", Applicable: true, Ready,
                Ready ? [] : ["An Active, current overall solution design and component sheet are required."]);
    }

    private sealed class FakeQuestionGenerationService : ICisTextGenerationService
    {
        public CisTextGenerationRequest? LastRequest { get; private set; }
        public CisAiStatus GetStatus() => new([new("fake", "available", "local", true, [new("small")], null)]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request)
        {
            LastRequest = request;
            return new("generated", "fake", "small", """
                {"suggestions":[
                  {"questionId":"BRD-Q-001","answer":"The business sponsor owns the product outcome.","confidence":"high","reason":"The stakeholder section assigns accountability.","contextIds":["BRD-Q-001-CTX-1"]},
                  {"questionId":"BRD-Q-002","answer":null,"confidence":"insufficient","reason":"No accepted launch date appears in the supplied context.","contextIds":[]}
                ]}
                """, null, true);
        }
    }

    private sealed class BatchRejectingQuestionGenerationService : ICisTextGenerationService
    {
        public List<CisTextGenerationRequest> Requests { get; } = [];
        public CisAiStatus GetStatus() => new([new("fake", "available", "local", true, [new("small")], null)]);
        public CisTextGenerationResult Generate(CisTextGenerationRequest request)
        {
            Requests.Add(request);
            var ids = Regex.Matches(request.Prompt, "QUESTION (BRD-Q-[0-9]+):",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                .Select(match => match.Groups[1].Value).ToArray();
            if (ids.Length != 1)
                return new("generated", "fake", "small", "The response shape was lost.", null, true);
            var body = ids[0] == "BRD-Q-001"
                ? "\"answer\":\"The business sponsor owns the product outcome.\",\"confidence\":\"high\",\"reason\":\"Accountability is explicit.\",\"contextIds\":[\"BRD-Q-001-CTX-1\"]"
                : "\"answer\":\"Launch next Friday.\",\"confidence\":\"low\",\"reason\":\"A speculative date.\",\"contextIds\":[]";
            return new("generated", "fake", "small",
                $"{{\"suggestions\":[{{\"questionId\":\"{ids[0]}\",{body}}}]}}", null, true);
        }
    }

    private sealed class MutableProductDefinitionAuthority : ICisProductDefinitionAuthority
    {
        public bool Active { get; set; }
        public string? BaselineHash { get; set; }
        public IReadOnlyList<string> Errors { get; set; } = [];

        public CisProductDefinitionAuthority Evaluate(string repositoryPath)
            => new(true, Active, "DEF-TEST", Active ? "2026-09-04T10:00:00Z" : null, BaselineHash, Errors);
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
            Action<TemporaryRepository, int>? configureParticipant = null,
            IEnumerable<ICisBrdSourceEvidenceProvider>? sourceProviders = null,
            IReadOnlySet<int>? dependencyIndexes = null)
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
                Confirmed: true,
                "fixture",
                "fixture",
                "Fixture",
                "Fixture"));
            Assert.Equal(0, initialized.ExitCode);
            if (participantRepositories.Length > 0)
            {
                var importer = new RepositoryImporter(new RepositoryInitializer(), registry);
                foreach (var pair in participantRepositories.Select((repository, index) => (Repository: repository, Index: index + 1)))
                {
                    var dependency = dependencyIndexes?.Contains(pair.Index) == true;
                    var imported = importer.Import(new RepositoryImportRequest(
                        authority.Path,
                        "docs/cis",
                        [pair.Repository.Path],
                        DryRun: false,
                        Confirmed: true,
                        dependency ? "dependency" : "owned",
                        dependency ? "producer" : "none",
                        dependency ? ["core-api"] : []));
                    Assert.Equal(0, imported.ExitCode);
                }
            }
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
                sourceProviders ?? [],
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
