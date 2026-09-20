using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cis.Host;
using Cis.Modules.Brd;
using Cis.Modules.Docs;
using Cis.Modules.Graph;
using Cis.Modules.Repository;
using Cis.Modules.TechnicalIntent;
using Xunit;

namespace Cis.Modules.TechnicalIntent.Tests;

public sealed class TechnicalIntentWorkflowTests
{
    [Fact]
    public void DecisionReview_ReusesRecordedAnswersByMeaningWithoutResolvingAdditionalChoices()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        environment.Intent.Initialize(environment.Authority.Path);
        var content = File.ReadAllText(environment.TechnicalIntentPath);
        content = content.Replace("<!-- cis:technical-intent-decision-evidence:end -->", "| TI-DEC-900 | Choose the financial correction and reconciliation policy when ledger and statements disagree. | Financial changes | Open | A human policy decision is required. |\n| TI-DEC-901 | Choose failure behavior for signing and replay protection. | Integration changes | Proposed | Recovery requires review. |\n| TI-DEC-902 | Confirm privacy export, erasure and retention scope. | Privacy changes | Open | Scope requires review. |\n| TI-DEC-903 | Choose ownership for a new document publishing workflow. | Workflow changes | Open | Ownership requires review. |\n<!-- cis:technical-intent-decision-evidence:end -->", StringComparison.Ordinal);
        File.WriteAllText(environment.TechnicalIntentPath, content);
        var reviews = environment.Intent.Status(environment.Authority.Path).Validation!.Decisions;
        var architecture = reviews.Single(item => item.Id == "TI-DEC-001");
        Assert.True(architecture.QuestionnaireOverlap); Assert.Equal(5, architecture.RelatedAnswers.Count);
        Assert.Contains(architecture.RelatedAnswers.Single(answer => answer.Id == "TI-Q-004").Answer, architecture.SuggestedResolution);
        Assert.NotNull(architecture.ReviewToken);
        var additional = reviews.Single(item => item.Id == "TI-DEC-900");
        Assert.False(additional.QuestionnaireOverlap); Assert.Empty(additional.RelatedAnswers); Assert.True(additional.NeedsReview);
        Assert.Contains("auditable adjustments", additional.SuggestedResolution);
        Assert.Contains("bounded backoff", reviews.Single(item => item.Id == "TI-DEC-901").SuggestedResolution);
        Assert.Contains("record-specific retention", reviews.Single(item => item.Id == "TI-DEC-902").SuggestedResolution);
        Assert.Contains("accountable owner", reviews.Single(item => item.Id == "TI-DEC-903").SuggestedResolution);
        Assert.All(reviews, item => Assert.False(string.IsNullOrWhiteSpace(item.SuggestedResolution)));
        Assert.Equal(content, File.ReadAllText(environment.TechnicalIntentPath));
    }

    [Fact]
    public void ResolveDecision_SavesOnlySelectedRowAndAuditWithPipesNewlinesAndHumanIdentity()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        environment.Intent.Initialize(environment.Authority.Path);
        var before = File.ReadAllText(environment.TechnicalIntentPath);
        var decisions = environment.Intent.Status(environment.Authority.Path).Validation!.Decisions;
        var selected = decisions.First(); var untouched = decisions.Skip(1).Select(item => item.Rationale).ToArray();
        var resolution = "Keep the reviewed API | worker boundary.\nPreserve contract ownership.";
        var saved = environment.Intent.ResolveDecision(environment.Authority.Path, new(selected.Id, resolution, "Retains the established product boundaries.", selected.ReviewToken!, "Design owner"));
        Assert.True(saved.Applied); Assert.Equal(0, saved.ExitCode);
        var after = File.ReadAllText(environment.TechnicalIntentPath);
        foreach (var rationale in untouched) Assert.Contains(rationale, after);
        Assert.Contains("API \\| worker", after); Assert.Contains("Recorded by: Design owner", after);
        Assert.Equal(before[..before.IndexOf("## Open technical decisions", StringComparison.Ordinal)], after[..after.IndexOf("## Open technical decisions", StringComparison.Ordinal)]);
        var review = environment.Intent.Status(environment.Authority.Path).Validation!.Decisions.Single(item => item.Id == selected.Id);
        Assert.Equal("Resolved", review.Status); Assert.False(review.NeedsReview);
        Assert.Equal(resolution, review.RecordedResolution); Assert.Equal("Design owner", review.RecordedBy);
        Assert.NotEqual(selected.ReviewToken, review.ReviewToken);
        Assert.Equal(untouched, environment.Intent.Status(environment.Authority.Path).Validation!.Decisions.Skip(1).Select(item => item.Rationale));
    }

    [Fact]
    public void ResolveDecision_RejectsStaleEvidenceAmbiguousRowsAndMissingHumanInput()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        environment.Intent.Initialize(environment.Authority.Path);
        var review = environment.Intent.Status(environment.Authority.Path).Validation!.Decisions.First();
        var input = new Cis.Abstractions.CisTechnicalDecisionResolution(review.Id, "Keep the existing architecture.", "Preserves the current boundaries.", review.ReviewToken!, "Owner");
        var before = File.ReadAllText(environment.TechnicalIntentPath);
        Assert.False(environment.Intent.ResolveDecision(environment.Authority.Path, input with { Resolution = "" }).Applied);
        Assert.False(environment.Intent.ResolveDecision(environment.Authority.Path, input with { Reason = "<!-- hidden -->" }).Applied);
        Assert.False(environment.Intent.ResolveDecision(environment.Authority.Path, input with { Actor = "" }).Applied);
        Assert.False(environment.Intent.ResolveDecision(environment.Authority.Path, input with { Resolution = "<!-- hidden -->" }).Applied);
        Assert.Equal(before, File.ReadAllText(environment.TechnicalIntentPath));
        environment.Questionnaire.Answer(environment.Authority.Path, "TI-Q-004", "Maintain the existing modular architecture.", "Owner");
        Assert.Equal("conflict", environment.Intent.ResolveDecision(environment.Authority.Path, input).Status);
        Assert.Equal(before, File.ReadAllText(environment.TechnicalIntentPath));
        var row = before.Split('\n').Single(line => line.StartsWith("| " + review.Id + " |", StringComparison.Ordinal));
        var duplicate = before.Replace("<!-- cis:technical-intent-decision-evidence:end -->", row + "\n<!-- cis:technical-intent-decision-evidence:end -->", StringComparison.Ordinal);
        File.WriteAllText(environment.TechnicalIntentPath, duplicate);
        Assert.All(environment.Intent.Status(environment.Authority.Path).Validation!.Decisions.Where(item => item.Id == review.Id), item => { Assert.Null(item.ReviewToken); Assert.True(item.NeedsReview); });
        Assert.Equal("conflict", environment.Intent.ResolveDecision(environment.Authority.Path, input).Status);
        Assert.Equal(duplicate, File.ReadAllText(environment.TechnicalIntentPath));
    }

    [Fact]
    public void ResolveDecision_CommandDispatchSavesReviewedJsonAndRejectsMalformedInput()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        environment.Intent.Initialize(environment.Authority.Path);
        var review = environment.Intent.Status(environment.Authority.Path).Validation!.Decisions.First();
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new WorkspaceModule())
            .AddModule(new DocsModule()).AddModule(new GraphModule()).AddModule(new BrdModule()).AddModule(new TechnicalIntentModule()).Build();
        var inputPath = Path.Combine(environment.Authority.Path, "decision-input.json");
        File.WriteAllText(inputPath, System.Text.Json.JsonSerializer.Serialize(new { resolution = "Retain the reviewed component boundaries to preserve established ownership.", reviewToken = review.ReviewToken }));
        Assert.Equal(0, application.Invoke(["technical-intent", "decisions", "resolve", review.Id, "--input", inputPath, "--actor", "Owner", "--workspace", environment.Authority.Path, "--format", "json"]));
        var saved = environment.Intent.Status(environment.Authority.Path).Validation!.Decisions.Single(item => item.Id == review.Id);
        Assert.Equal("Resolved", saved.Status);
        Assert.Equal("Retain the reviewed component boundaries to preserve established ownership.", saved.RecordedResolution);
        Assert.Equal("", saved.RecordedReason);
        Assert.DoesNotContain("Reason:", saved.Rationale);
        var content = File.ReadAllText(environment.TechnicalIntentPath);
        File.WriteAllText(inputPath, "[]");
        Assert.Equal(2, application.Invoke(["technical-intent", "decisions", "resolve", review.Id, "--input", inputPath, "--actor", "Owner", "--workspace", environment.Authority.Path, "--format", "json"]));
        Assert.Equal(content, File.ReadAllText(environment.TechnicalIntentPath));
    }

    [Fact]
    public void ExistingDiscovery_DraftsBeforeBusinessApprovalWithoutWeakeningApprovalOrAnswerGates()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: false);
        Assert.Equal(0, environment.Brd.Initialize(environment.Authority.Path, "Product BRD").ExitCode);
        var brdPath = Path.Combine(environment.Authority.Path, "docs/specs/business-requirements.md");
        var brd = File.ReadAllText(brdPath);

        var prepared = environment.Intent.PrepareExistingDraft(environment.Authority.Path);

        Assert.Empty(prepared.Errors);
        Assert.Contains("technical_intent_schema: 4", File.ReadAllText(environment.TechnicalIntentPath), StringComparison.Ordinal);
        Assert.Equal(brd, File.ReadAllText(brdPath));
        Assert.False(environment.Intent.Status(environment.Authority.Path).Validation!.Current);
        Assert.Equal("blocked", environment.Intent.Approve(environment.Authority.Path, "Owner", "Review").Status);
        Assert.False(environment.Intent.Evaluate(environment.Authority.Path).Ready);
        Assert.Contains(environment.Questionnaire.Status(environment.Authority.Path).Questions, item => item.Status == "Unanswered");
        Assert.Equal("blocked", environment.Questionnaire.Answer(environment.Authority.Path, "TI-Q-013", "Policy", "Owner").Status);
    }

    [Fact]
    public void ExistingDiscovery_PreservesInferredArchitectureAndOpenDecisionsWhenQuestionnaireCompletes()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true, completeQuestionnaire: false);
        Assert.Empty(environment.Intent.PrepareExistingDraft(environment.Authority.Path).Errors);
        var content = File.ReadAllText(environment.TechnicalIntentPath);
        content = content.Replace("<!-- cis:technical-intent-module-architecture:start -->", "<!-- cis:technical-intent-module-architecture:start -->\nObserved service owns its transaction boundary.", StringComparison.Ordinal)
            + "\n<!-- cis:technical-intent-implementation-authored -->\n";
        File.WriteAllText(environment.TechnicalIntentPath, content);
        foreach (var question in environment.Questionnaire.Status(environment.Authority.Path).Questions)
            Assert.Equal(0, environment.Questionnaire.Answer(environment.Authority.Path, question.Id, question.SuggestedAnswer, "Owner").ExitCode);

        environment.Intent.Initialize(environment.Authority.Path);

        var refreshed = File.ReadAllText(environment.TechnicalIntentPath);
        Assert.Contains("Observed service owns its transaction boundary.", refreshed, StringComparison.Ordinal);
        Assert.Contains("| Open |", refreshed, StringComparison.Ordinal);
        Assert.DoesNotContain("| Accepted |", refreshed, StringComparison.Ordinal);
        Assert.True(environment.Questionnaire.Status(environment.Authority.Path).Complete);
        var review = environment.Intent.Status(environment.Authority.Path).Validation!;
        Assert.False(review.Valid);
        Assert.NotEmpty(review.Decisions);
        Assert.All(review.Decisions, decision =>
        {
            Assert.True(decision.NeedsReview); Assert.NotEmpty(decision.Decision); Assert.NotEmpty(decision.Issues);
            Assert.True(decision.DocumentLine > 0);
            Assert.StartsWith("| " + decision.Id + " |", refreshed.Split('\n')[decision.DocumentLine!.Value - 1].TrimStart(), StringComparison.Ordinal);
        });
        Assert.Equal("blocked", environment.Intent.Approve(environment.Authority.Path, "Owner", "Review").Status);
    }

    [Fact]
    public void Commands_AreRegistered()
    {
        using var application = new CisHostBuilder()
            .AddModule(new RepositoryModule())
            .AddModule(new WorkspaceModule())
            .AddModule(new DocsModule())
            .AddModule(new GraphModule())
            .AddModule(new BrdModule())
            .AddModule(new TechnicalIntentModule())
            .Build();

        Assert.Equal(0, application.Invoke(["technical-intent", "init", "--help"]));
        Assert.Equal(0, application.Invoke(["technical-intent", "questions", "init", "--help"]));
        Assert.Equal(0, application.Invoke(["technical-intent", "questions", "status", "--help"]));
        Assert.Equal(0, application.Invoke(["technical-intent", "questions", "answer", "--help"]));
        Assert.Equal(0, application.Invoke(["technical-intent", "decisions", "resolve", "--help"]));
        Assert.Equal(0, application.Invoke(["technical-intent", "validate", "--help"]));
        Assert.Equal(0, application.Invoke(["technical-intent", "status", "--help"]));
        Assert.Equal(0, application.Invoke(["technical-intent", "refresh", "--help"]));
        Assert.Equal(0, application.Invoke(["technical-intent", "approve", "--help"]));
    }

    [Fact]
    public void Questionnaire_CapturesHumanChoicesBeforeGeneratingComponentAndInteractionMaps()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);

        var questionnaire = environment.Questionnaire.Status(environment.Authority.Path);
        Assert.True(questionnaire.Current);
        Assert.True(questionnaire.Complete);
        Assert.Equal(16, questionnaire.AnsweredCount);
        Assert.All(questionnaire.Questions, item => Assert.Equal("Technical owner", item.AnsweredBy));

        var initialized = environment.Intent.Initialize(environment.Authority.Path);
        var content = File.ReadAllText(environment.TechnicalIntentPath);

        Assert.Equal(0, initialized.ExitCode);
        Assert.Contains("technical_intent_schema: 4", content, StringComparison.Ordinal);
        Assert.Contains("## High-level technology and architecture choices", content, StringComparison.Ordinal);
        Assert.Contains("## Component and interaction map", content, StringComparison.Ordinal);
        Assert.Contains("## Product module architecture", content, StringComparison.Ordinal);
        Assert.Contains("## Integration point catalog", content, StringComparison.Ordinal);
        Assert.Contains("<!-- cis:technical-intent-questionnaire-evidence:start -->", content, StringComparison.Ordinal);
        Assert.Contains("<!-- cis:technical-intent-component-map:start -->", content, StringComparison.Ordinal);
        Assert.Contains("| TI-DEC-001 |", content, StringComparison.Ordinal);
        Assert.Contains("| Accepted |", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Initialize_DerivesDetailedModulesAndIntegrationPointsFromBusinessCapabilities()
    {
        using var environment = WorkspaceEnvironment.Create(
            approveBrd: true,
            includeImplementation: false,
            businessCapabilities: """
                1. **Risk-data preparation (BRD-FR-001):** Receive company-prefiltered data through the company BI system
                   and validate its source and permitted-use context.
                2. **Individual risk assessment (BRD-FR-001):** Assess payment transactions with a versioned model and produce an explanation.
                3. **Threshold handling and human review (BRD-FR-001):** Send the suggestion to the existing rule engine and refer threshold-exceeding activity.
                4. **Analyst feedback (BRD-FR-001):** Record accountable human outcomes as governed feedback.
                5. **Performance and drift oversight (BRD-FR-001):** Report classification performance, model drift, and daily operational outcomes.
                6. **Audit evidence (BRD-FR-001):** Retain immutable audit evidence in blob storage.
                7. **Customer transaction handling (BRD-FR-001):** Project approved payment transaction state to the customer.
                """);

        var initialized = environment.Intent.Initialize(environment.Authority.Path);
        var content = File.ReadAllText(environment.TechnicalIntentPath);

        Assert.Equal(0, initialized.ExitCode);
        Assert.Contains("technical_intent_schema: 4", content, StringComparison.Ordinal);
        Assert.Contains("<!-- cis:technical-intent-module-architecture:start -->", content, StringComparison.Ordinal);
        Assert.Contains("<!-- cis:technical-intent-integration-points:start -->", content, StringComparison.Ordinal);
        Assert.Contains("TI-MOD-RISK-DATA-PREPARATION", content, StringComparison.Ordinal);
        Assert.Contains("### Module responsibility profiles", content, StringComparison.Ordinal);
        Assert.Contains("TI-INT-RISK-DATA-PREPARATION-EXTERNAL-DATA", content, StringComparison.Ordinal);
        Assert.Contains("TI-INT-THRESHOLD-HANDLING-AND-HUMAN-REVIEW-RULE-ENGINE", content, StringComparison.Ordinal);
        Assert.Contains("TI-INT-AUDIT-EVIDENCE-EVIDENCE-STORE", content, StringComparison.Ordinal);
        Assert.Contains("Prepared and validated business facts", content, StringComparison.Ordinal);
        Assert.True(initialized.Validation!.Valid);
    }

    [Fact]
    public void Initialize_RecordsDependencyAsDirectionalIntegrationWithoutOwningItsTechnology()
    {
        using var environment = WorkspaceEnvironment.Create(
            approveBrd: true,
            seedDependency: repository =>
            {
                repository.Write("package.json", "{\"dependencies\":{\"express\":\"5.1.0\",\"pg\":\"8.0.0\"}}");
                repository.Write("src/core-api.ts", "export const coreApi = true;\n");
            });

        var initialized = environment.Intent.Initialize(environment.Authority.Path);
        var content = File.ReadAllText(environment.TechnicalIntentPath);
        var dependency = Assert.Single(environment.Registry.Resolve(environment.Authority.Path).Workspace!.DependencyRepositories);

        Assert.Equal(0, initialized.ExitCode);
        Assert.Contains($"| {dependency.Id} | participant | dependency | producer |", content, StringComparison.Ordinal);
        Assert.Contains("The dependency produces a capability, contract, event, or data result consumed by this product.", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Prefer repository-supported contract standards for `express`", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Initialize_UpgradesManagedDraftSchema3WithDetailedArchitectureWithoutTouchingHumanSections()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        Assert.Equal(0, environment.Intent.Initialize(environment.Authority.Path).ExitCode);
        var legacyDraft = File.ReadAllText(environment.TechnicalIntentPath)
            .Replace("technical_intent_schema: 4", "technical_intent_schema: 3", StringComparison.Ordinal);
        legacyDraft = Regex.Replace(legacyDraft,
            "(?ms)^## Product module architecture\\s*$.*?(?=^## )", string.Empty,
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        legacyDraft = Regex.Replace(legacyDraft,
            "(?ms)^## Integration point catalog\\s*$.*?(?=^## )", string.Empty,
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        legacyDraft = legacyDraft.Replace(
            "- Preserve the Active BRD as business authority; technical direction may satisfy it but may not silently add product scope.",
            "- Human refinement: preserve the Active BRD and the explicitly reviewed research boundary.",
            StringComparison.Ordinal);
        File.WriteAllText(environment.TechnicalIntentPath, legacyDraft);

        var upgraded = environment.Intent.Initialize(environment.Authority.Path);
        var content = File.ReadAllText(environment.TechnicalIntentPath);

        Assert.True(upgraded.Applied);
        Assert.Contains("technical_intent_schema: 4", content, StringComparison.Ordinal);
        Assert.Contains("## Product module architecture", content, StringComparison.Ordinal);
        Assert.Contains("## Integration point catalog", content, StringComparison.Ordinal);
        Assert.Contains("Human refinement: preserve the Active BRD", content, StringComparison.Ordinal);
        Assert.True(upgraded.Validation!.Valid);
    }

    [Fact]
    public void QuestionnaireAnswer_UpdatesOneDecisionWithExactHumanProvenance()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);

        var answered = environment.Questionnaire.Answer(environment.Authority.Path, "TI-Q-004",
            "Use a modular monolith with compiler-enforced module boundaries.", "Andrew Spiteri");

        Assert.Equal(0, answered.ExitCode);
        Assert.True(answered.Complete);
        var decision = Assert.Single(answered.Questions, item => item.Id == "TI-Q-004");
        Assert.Equal("Andrew Spiteri", decision.AnsweredBy);
        Assert.Equal("Use a modular monolith with compiler-enforced module boundaries.", decision.Answer);
        Assert.Contains("Use a modular monolith with compiler-enforced module boundaries.",
            File.ReadAllText(Path.Combine(environment.Authority.Path, "docs", "specs", "technical-intent-questionnaire.md")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Questionnaire_DerivesSupportedDirectionsFromAnExistingImplementation()
    {
        using var environment = WorkspaceEnvironment.Create(
            approveBrd: true,
            completeQuestionnaire: false,
            seedImplementation: repository =>
            {
                repository.Write("src/Product.Api/Product.Api.csproj", """
                    <Project Sdk="Microsoft.NET.Sdk.Web">
                      <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                      <ItemGroup>
                        <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.0" />
                        <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.14.0" />
                      </ItemGroup>
                    </Project>
                    """);
                repository.Write("src/Product.Api/Program.cs", "builder.Services.AddControllers(); builder.Services.AddHealthChecks();");
                repository.Write("web/package.json", "{\"name\":\"product-web\",\"dependencies\":{\"next\":\"16.0.0\",\"react\":\"19.0.0\"},\"devDependencies\":{\"vitest\":\"4.0.0\",\"@playwright/test\":\"1.58.0\"}}");
                repository.Write("infra/compose.yml", "services:\n  api:\n    build: ../src/Product.Api\n");
            });

        var questionnaire = environment.Questionnaire.Status(environment.Authority.Path);

        Assert.True(questionnaire.Current);
        Assert.False(questionnaire.Complete);
        Assert.True(questionnaire.AnsweredCount >= 7);
        Assert.All(new[] { "TI-Q-002", "TI-Q-003", "TI-Q-004", "TI-Q-005", "TI-Q-006", "TI-Q-010", "TI-Q-014" }, id =>
        {
            var question = Assert.Single(questionnaire.Questions, item => item.Id == id);
            Assert.Equal("Derived", question.Status);
            Assert.Equal("repository-evidence", question.ResolutionSource);
            Assert.False(string.IsNullOrWhiteSpace(question.Answer));
            Assert.NotEmpty(question.Evidence ?? []);
        });
        Assert.Contains("SQLite", Assert.Single(questionnaire.Questions, item => item.Id == "TI-Q-006").Answer, StringComparison.Ordinal);
        Assert.Equal("Unanswered", Assert.Single(questionnaire.Questions, item => item.Id == "TI-Q-013").Status);
        var canonical = File.ReadAllText(Path.Combine(environment.Authority.Path, "docs", "specs", "technical-intent-questionnaire.md"));
        Assert.Contains("project_mode: existing-project", canonical, StringComparison.Ordinal);
        Assert.Contains("- Resolution source: repository-evidence", canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void Questionnaire_LeavesGreenfieldDirectionsForHumanAnswers()
    {
        using var environment = WorkspaceEnvironment.Create(
            approveBrd: true,
            includeImplementation: false,
            completeQuestionnaire: false);

        var questionnaire = environment.Questionnaire.Status(environment.Authority.Path);

        Assert.True(questionnaire.Current);
        Assert.False(questionnaire.Complete);
        Assert.Equal(0, questionnaire.AnsweredCount);
        Assert.Equal(16, questionnaire.UnansweredCount);
        Assert.All(questionnaire.Questions, question =>
        {
            Assert.Equal("Unanswered", question.Status);
            Assert.Equal("unresolved", question.ResolutionSource);
        });
        Assert.Contains("project_mode: greenfield", File.ReadAllText(
            Path.Combine(environment.Authority.Path, "docs", "specs", "technical-intent-questionnaire.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceInit_SeedsWorkspaceScopedTechnicalIntent()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: false);

        var content = File.ReadAllText(environment.TechnicalIntentPath);

        Assert.Contains("scope: Workspace", content, StringComparison.Ordinal);
        Assert.Contains("technical_intent_schema: 2", content, StringComparison.Ordinal);
        Assert.Contains("## Open technical decisions", content, StringComparison.Ordinal);
    }

    [Fact]
    public void InactiveBrd_IsAWorkflowBlockRatherThanInvalidTechnicalIntentState()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: false);

        var status = environment.Intent.Status(environment.Authority.Path);
        var initialized = environment.Intent.Initialize(environment.Authority.Path);

        Assert.Equal("status", status.Status);
        Assert.Empty(status.Errors);
        Assert.False(status.Validation!.Current);
        Assert.Contains(status.Validation.Warnings, warning =>
            warning.Contains("Active, current BRD", StringComparison.Ordinal));
        Assert.Equal("blocked", initialized.Status);
        Assert.Equal(5, initialized.ExitCode);
    }

    [Fact]
    public void Lifecycle_RequiresActiveBrdResolvedContentAndExplicitApproval()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);

        var initialized = environment.Intent.Initialize(environment.Authority.Path);
        Assert.True(initialized.Applied);
        Assert.True(initialized.Validation!.Valid);
        Assert.NotEmpty(initialized.Validation.Decisions);
        Assert.All(initialized.Validation.Decisions, decision => { Assert.False(decision.NeedsReview); Assert.Empty(decision.Issues); });
        Assert.Equal("Ready for Approval", initialized.Validation.EffectiveStatus);
        var scaffold = File.ReadAllText(environment.TechnicalIntentPath);
        Assert.Contains("technical_intent_schema: 4", scaffold, StringComparison.Ordinal);
        Assert.Contains("## Architecture guidelines and applicable standards", scaffold, StringComparison.Ordinal);
        Assert.Contains("BRD-FR-001", scaffold, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO", scaffold, StringComparison.Ordinal);
        Assert.False(environment.Intent.Evaluate(environment.Authority.Path).Ready);

        var ready = environment.Intent.Validate(environment.Authority.Path);
        Assert.True(ready.Validation!.Valid);
        Assert.True(ready.Validation.Current);
        Assert.Equal("Ready for Approval", ready.Validation.EffectiveStatus);
        var pendingApproval = environment.Intent.Evaluate(environment.Authority.Path);
        Assert.False(pendingApproval.Ready);
        Assert.DoesNotContain(pendingApproval.Errors, error =>
            error.Contains("Authority repository graph is stale", StringComparison.Ordinal));

        var approved = environment.Intent.Approve(
            environment.Authority.Path,
            "Architecture owner",
            "The reviewed workspace technical direction is accepted.");
        Assert.True(approved.Applied);
        Assert.Equal("Active", approved.Validation!.EffectiveStatus);
        Assert.True(environment.Intent.Evaluate(environment.Authority.Path).Ready);

        File.AppendAllText(environment.TechnicalIntentPath, "\nMaterial unapproved direction.\n");
        var stale = environment.Intent.Status(environment.Authority.Path);
        Assert.Equal("Stale", stale.Validation!.EffectiveStatus);
        Assert.False(stale.Validation.Current);
        Assert.False(environment.Intent.Evaluate(environment.Authority.Path).Ready);
    }

    [Fact]
    public void Initialize_UpgradesUntouchedStarterFromBrdClassificationAndStandardsIdempotently()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);

        var initialized = environment.Intent.Initialize(environment.Authority.Path);
        var content = File.ReadAllText(environment.TechnicalIntentPath);

        Assert.Equal("initialized", initialized.Status);
        Assert.True(initialized.Applied);
        Assert.Contains("technical_intent_schema: 4", content, StringComparison.Ordinal);
        Assert.Contains("## Approved business baseline", content, StringComparison.Ordinal);
        Assert.Contains("## Architecture guidelines and applicable standards", content, StringComparison.Ordinal);
        Assert.Contains("<!-- cis:technical-intent-business-evidence:start -->", content, StringComparison.Ordinal);
        Assert.Contains("<!-- cis:technical-intent-surface-evidence:start -->", content, StringComparison.Ordinal);
        Assert.Contains("<!-- cis:technical-intent-standards-evidence:start -->", content, StringComparison.Ordinal);
        Assert.Contains("| standard |", content, StringComparison.Ordinal);
        Assert.Contains("Testing and Verification Standard", content, StringComparison.Ordinal);
        Assert.Contains("Product", content, StringComparison.Ordinal);
        Assert.Contains("BRD-FR-001", content, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO", content, StringComparison.Ordinal);

        var repeated = environment.Intent.Initialize(environment.Authority.Path);

        Assert.Equal("unchanged", repeated.Status);
        Assert.False(repeated.Applied);
        Assert.Equal(content, File.ReadAllText(environment.TechnicalIntentPath));
    }

    [Fact]
    public void Initialize_PreservesSubstantiveHumanSectionsWhileEnrichingRemainingStarterContent()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        var starter = File.ReadAllText(environment.TechnicalIntentPath).Replace(
            "- TODO: Record the principles that constrain implementation choices.",
            "- Human-authored principle: keep the research boundary provider neutral.",
            StringComparison.Ordinal);
        File.WriteAllText(environment.TechnicalIntentPath, starter);

        var initialized = environment.Intent.Initialize(environment.Authority.Path);
        var content = File.ReadAllText(environment.TechnicalIntentPath);

        Assert.True(initialized.Applied);
        Assert.Contains("Human-authored principle: keep the research boundary provider neutral.", content, StringComparison.Ordinal);
        Assert.Contains("## Architecture guidelines and applicable standards", content, StringComparison.Ordinal);
        Assert.Contains("BRD-FR-001", content, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ApprovedIntent_RemainsActiveForBrdProvenanceAndMigratesLegacyBaselineWithoutReapproval()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        Assert.Equal(0, environment.Intent.Initialize(environment.Authority.Path).ExitCode);
        CompleteTechnicalReview(environment.TechnicalIntentPath);
        Assert.Equal(0, environment.Intent.Approve(environment.Authority.Path, "Architecture owner",
            "The reviewed workspace technical direction is accepted.").ExitCode);

        var brdPath = Path.Combine(environment.Authority.Path, "docs", "specs", "business-requirements.md");
        var brd = File.ReadAllText(brdPath).Replace(
            "<!-- cis:feature-traceability:end -->",
            "<!-- provenance-only reconciliation -->\n<!-- cis:feature-traceability:end -->",
            StringComparison.Ordinal);
        File.WriteAllText(brdPath, brd);

        var provenanceOnly = environment.Intent.Status(environment.Authority.Path);
        Assert.Equal("Active", provenanceOnly.Validation!.EffectiveStatus);
        Assert.True(provenanceOnly.Validation.Current);

        var intent = File.ReadAllText(environment.TechnicalIntentPath);
        var legacyHash = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            brd.Replace("\r\n", "\n", StringComparison.Ordinal)))).ToLowerInvariant();
        intent = Regex.Replace(intent, @"(?m)^(\| brd \| [^|]+ \| )semantic-v1:sha256:[a-f0-9]{64}( \|)", $"$1{legacyHash}$2",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        File.WriteAllText(environment.TechnicalIntentPath, intent);
        var legacy = environment.Intent.Status(environment.Authority.Path);
        Assert.Equal("Stale", legacy.Validation!.EffectiveStatus);
        Assert.DoesNotContain(legacy.Validation.Warnings, warning =>
            warning.Contains("content changed after approval", StringComparison.OrdinalIgnoreCase));

        var migrated = environment.Intent.Initialize(environment.Authority.Path);
        Assert.True(migrated.Applied);
        Assert.Equal("Active", migrated.Validation!.EffectiveStatus);
        var migratedContent = File.ReadAllText(environment.TechnicalIntentPath);
        Assert.Contains("semantic-v1:sha256:", migratedContent, StringComparison.Ordinal);
        Assert.Contains("approved_by: \"Architecture owner\"", migratedContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Initialize_PreservesApprovalWhenOnlyRepositoryBaselineVersionChanges()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        Assert.Equal(0, environment.Intent.Initialize(environment.Authority.Path).ExitCode);
        CompleteTechnicalReview(environment.TechnicalIntentPath);
        Assert.Equal(0, environment.Intent.Approve(
            environment.Authority.Path,
            "Architecture owner",
            "The reviewed workspace technical direction is accepted.").ExitCode);
        var content = File.ReadAllText(environment.TechnicalIntentPath);
        content = Regex.Replace(
            content,
            @"(?m)^(\| repository \| [^|]+ \| )sha256:[a-f0-9]{64}( \|)",
            "$1sha256:0000000000000000000000000000000000000000000000000000000000000000$2",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        File.WriteAllText(environment.TechnicalIntentPath, content);

        var stale = environment.Intent.Status(environment.Authority.Path);
        Assert.Equal("Stale", stale.Validation!.EffectiveStatus);

        var reconciled = environment.Intent.Initialize(environment.Authority.Path);

        Assert.Equal(0, reconciled.ExitCode);
        Assert.True(reconciled.Applied);
        Assert.Equal("Active", reconciled.Validation!.EffectiveStatus);
        Assert.True(reconciled.Validation.Current);
        var reconciledContent = File.ReadAllText(environment.TechnicalIntentPath);
        Assert.Contains("approved_by: \"Architecture owner\"", reconciledContent, StringComparison.Ordinal);
        Assert.Contains("approval_reason: \"The reviewed workspace technical direction is accepted.\"",
            reconciledContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveStandardContentDriftRequiresRenewedTechnicalReview()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        Assert.Equal(0, environment.Intent.Initialize(environment.Authority.Path).ExitCode);
        CompleteTechnicalReview(environment.TechnicalIntentPath);
        Assert.Equal(0, environment.Intent.Approve(environment.Authority.Path, "Architecture owner",
            "The reviewed workspace technical direction is accepted.").ExitCode);
        var standardPath = Path.Combine(environment.Participant.Path, "docs", "cis", "standards", "testing-standard.md");
        Assert.True(File.Exists(standardPath));
        File.AppendAllText(standardPath, "\n<!-- reviewed standard clarification -->\n");
        environment.RebuildParticipantGraph();
        Assert.Equal("Active", environment.Brd.Reconcile(environment.Authority.Path).Validation!.EffectiveStatus);

        var stale = environment.Intent.Status(environment.Authority.Path);

        Assert.Equal("Stale", stale.Validation!.EffectiveStatus);
        Assert.Contains(stale.Validation.Warnings, warning => warning.Contains("baseline differs from standard", StringComparison.Ordinal));

        var reconciled = environment.Intent.Initialize(environment.Authority.Path);

        Assert.True(reconciled.Applied);
        Assert.Equal("Ready for Approval", reconciled.Validation!.EffectiveStatus);
        var reconciledContent = File.ReadAllText(environment.TechnicalIntentPath);
        Assert.DoesNotContain("approved_by: \"Architecture owner\"", reconciledContent, StringComparison.Ordinal);
        Assert.Contains("reviewed standard clarification", File.ReadAllText(standardPath), StringComparison.Ordinal);
        var expectedDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            File.ReadAllText(standardPath).Replace("\r\n", "\n", StringComparison.Ordinal)))).ToLowerInvariant();
        Assert.Contains($"`{expectedDigest}`", reconciledContent, StringComparison.Ordinal);
    }

    [Fact]
    public void GovernanceRefresh_PreservesAuthorityAcrossImplementationOnlyGraphDrift()
    {
        using var environment = WorkspaceEnvironment.Create(approveBrd: true);
        Assert.Equal(0, environment.Intent.Initialize(environment.Authority.Path).ExitCode);
        CompleteTechnicalReview(environment.TechnicalIntentPath);
        Assert.Equal(0, environment.Intent.Approve(environment.Authority.Path, "Architecture owner",
            "The reviewed workspace technical direction is accepted.").ExitCode);
        var backlog = environment.CreateBacklog();
        Assert.Equal("built", backlog.Build(environment.Authority.Path).Status);
        Assert.Equal("approved", backlog.Approve(environment.Authority.Path, "Product owner",
            "The reviewed high-level outcomes are accepted.").Status);

        environment.Participant.Write("src/Product/Product.cs", "public sealed class Product { public string Name => \"changed implementation\"; }");
        environment.RebuildParticipantGraph();
        Assert.Equal("Stale", environment.Brd.Status(environment.Authority.Path).Validation!.EffectiveStatus);
        Assert.Equal("Stale", environment.Intent.Status(environment.Authority.Path).Validation!.EffectiveStatus);

        var refreshed = new GovernanceRefreshService(environment.Brd, environment.Intent, backlog)
            .Refresh(environment.Authority.Path);

        Assert.Equal(0, refreshed.ExitCode);
        Assert.Equal("refreshed", refreshed.Status);
        Assert.Equal("Active", refreshed.Brd.Validation!.EffectiveStatus);
        Assert.Equal("Active", refreshed.TechnicalIntent!.Validation!.EffectiveStatus);
        Assert.Equal("Active", refreshed.Backlog!.Validation!.EffectiveStatus);
        Assert.Contains("approved_by: \"Product owner\"",
            File.ReadAllText(Path.Combine(environment.Authority.Path, "docs", "plans", "high-level-backlog.md")),
            StringComparison.Ordinal);
    }

    private static void CompleteTechnicalReview(string path)
    {
        var content = File.ReadAllText(path);
        if (!content.Contains("## Approved business baseline", StringComparison.Ordinal))
        {
            content = content.Replace(
                "## Design goals and principles",
                "## Approved business baseline\n\nThe active BRD scope and exclusions are preserved.\n\n## Design goals and principles",
                StringComparison.Ordinal);
        }
        content = Regex.Replace(content, "TODO:[^\r\n]*", "Reviewed technical direction.",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        content = content.Replace("| TODO | TODO | TODO | TODO |", "| workspace | `.` | markdown | authority |", StringComparison.Ordinal);
        content = content.Replace("| Reliability | TODO | TODO |", "| Reliability | Deterministic behavior | Integration tests |", StringComparison.Ordinal);
        content = content.Replace("| Performance | TODO | TODO |", "| Performance | Bounded latency | Load test |", StringComparison.Ordinal);
        content = content.Replace("| Maintainability | TODO | TODO |", "| Maintainability | Explicit boundaries | Architecture tests |", StringComparison.Ordinal);
        content = content.Replace("| Accessibility | TODO | TODO |", "| Accessibility | WCAG intent | Accessibility review |", StringComparison.Ordinal);
        content = content.Replace("| Security | TODO | TODO |", "| Security | Default deny | Authorization tests |", StringComparison.Ordinal);
        content = content.Replace("| TI-DEC-001 | TODO | Change dossier creation | Open | TODO |",
            "| TI-DEC-001 | Contract ownership | Change dossier creation | Resolved | OpenAPI is canonical and drift-tested. |", StringComparison.Ordinal);
        content = Regex.Replace(content,
            @"(?m)^\| (?<id>TI-DEC-\d+) \| (?<decision>.+?) \| (?<gate>.+?) \| (?:Open|Proposed) \| .+? \|$",
            match => $"| {match.Groups["id"].Value} | {match.Groups["decision"].Value} | {match.Groups["gate"].Value} | Resolved | Reviewed direction is recorded in the owning ADR. |",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        File.WriteAllText(path, content);
    }

    private sealed class WorkspaceEnvironment : IDisposable
    {
        private WorkspaceEnvironment(TemporaryRepository authority, TemporaryRepository participant, TemporaryRepository? dependency,
            TechnicalIntentService intent, TechnicalIntentQuestionnaireService questionnaire, BrdService brd, WorkspaceRegistry registry,
            CisRepositoryContextResolver resolver, DocumentationCatalogMerger merger)
        {
            Authority = authority;
            Participant = participant;
            Dependency = dependency;
            Intent = intent;
            Questionnaire = questionnaire;
            Brd = brd;
            Registry = registry;
            Resolver = resolver;
            Merger = merger;
        }

        public TemporaryRepository Authority { get; }
        public TemporaryRepository Participant { get; }
        public TemporaryRepository? Dependency { get; }
        public TechnicalIntentService Intent { get; }
        public TechnicalIntentQuestionnaireService Questionnaire { get; }
        public BrdService Brd { get; }
        public WorkspaceRegistry Registry { get; }
        public CisRepositoryContextResolver Resolver { get; }
        public DocumentationCatalogMerger Merger { get; }
        public string TechnicalIntentPath => Path.Combine(Authority.Path, "docs", "specs", "technical-intent-spec.md");

        public static WorkspaceEnvironment Create(
            bool approveBrd,
            bool includeImplementation = true,
            bool completeQuestionnaire = true,
            Action<TemporaryRepository>? seedImplementation = null,
            string? businessCapabilities = null,
            Action<TemporaryRepository>? seedDependency = null)
        {
            var authority = TemporaryRepository.Create();
            var participant = TemporaryRepository.Create();
            var dependency = seedDependency is null ? null : TemporaryRepository.Create();
            seedDependency?.Invoke(dependency!);
            if (includeImplementation)
            {
                if (seedImplementation is null)
                {
                    participant.Write("src/Product/Product.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
                    participant.Write("src/Product/Product.cs", "public sealed class Product { }");
                }
                else
                {
                    seedImplementation(participant);
                }
            }
            var resolver = new CisRepositoryContextResolver();
            var registry = new WorkspaceRegistry(resolver);
            var workspaceInit = new WorkspaceInitializer(new RepositoryInitializer(), registry).Initialize(
                new WorkspaceInitRequest(authority.Path, "docs", false, true,
                    "fixture", "fixture", "Fixture", "Fixture"));
            Assert.Equal(0, workspaceInit.ExitCode);
            if (includeImplementation)
            {
                var imported = new RepositoryImporter(new RepositoryInitializer(), registry).Import(
                    new RepositoryImportRequest(authority.Path, "docs/cis", [participant.Path], false, true,
                        "owned", "none"));
                Assert.Equal(0, imported.ExitCode);
            }
            if (dependency is not null)
            {
                var imported = new RepositoryImporter(new RepositoryInitializer(), registry).Import(
                    new RepositoryImportRequest(authority.Path, "docs/cis", [dependency.Path], false, true,
                        "dependency", "producer"));
                Assert.Equal(0, imported.ExitCode);
            }
            var builder = CreateBuilder(resolver);
            Assert.Equal(0, builder.Build(authority.Path).ExitCode);
            if (includeImplementation) Assert.Equal(0, builder.Build(participant.Path).ExitCode);
            if (dependency is not null) Assert.Equal(0, builder.Build(dependency.Path).ExitCode);
            var graphReader = new GraphSnapshotReader(resolver);
            var merger = new DocumentationCatalogMerger();
            var brd = new BrdService(registry, resolver, new GraphValidator(resolver), graphReader, merger,
                () => new DateTimeOffset(2026, 8, 16, 10, 0, 0, TimeSpan.Zero));
            if (approveBrd)
            {
                Assert.Equal(0, brd.Initialize(authority.Path, "Product BRD").ExitCode);
                var brdPath = Path.Combine(authority.Path, "docs", "specs", "business-requirements.md");
                var content = File.ReadAllText(brdPath);
                content = Regex.Replace(content, "(?m)^TODO: Complete .+$",
                    "Stakeholders recorded complete and testable requirements.",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                content = Regex.Replace(content,
                    "(?ms)^## Functional requirements\\s*$.*?(?=^## )",
                    "## Functional requirements\n\n| ID | Requirement | Priority | Acceptance intent |\n| --- | --- | --- | --- |\n| BRD-FR-001 | A user shall view the product. | Must | The reviewed product view is available. |\n\n",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (!string.IsNullOrWhiteSpace(businessCapabilities))
                    content = Regex.Replace(content,
                        "(?ms)^## Business capabilities and processes\\s*$.*?(?=^## )",
                        $"## Business capabilities and processes\n\n{businessCapabilities.Trim()}\n\n",
                        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                File.WriteAllText(brdPath, content);
                Assert.Equal(0, brd.Approve(authority.Path, "Product owner", "Requirements accepted.").ExitCode);
            }
            var questionnaire = new TechnicalIntentQuestionnaireService(registry, resolver, brd, merger,
                () => new DateTimeOffset(2026, 8, 16, 10, 30, 0, TimeSpan.Zero));
            if (approveBrd) Assert.Equal(0, questionnaire.Initialize(authority.Path).ExitCode);
            if (approveBrd && completeQuestionnaire)
            {
                foreach (var question in questionnaire.Status(authority.Path).Questions)
                {
                    Assert.Equal(0, questionnaire.Answer(authority.Path, question.Id,
                        question.SuggestedAnswer, "Technical owner").ExitCode);
                }
                Assert.True(questionnaire.Status(authority.Path).Complete);
            }
            var intent = new TechnicalIntentService(registry, resolver, graphReader, brd, merger,
                () => new DateTimeOffset(2026, 8, 16, 11, 0, 0, TimeSpan.Zero));
            return new WorkspaceEnvironment(authority, participant, dependency, intent, questionnaire, brd, registry, resolver, merger);
        }

        public BrdBacklogService CreateBacklog()
            => new(Brd, Registry, Resolver, Merger, [Intent],
                () => new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero));

        public void RebuildParticipantGraph()
            => Assert.Equal(0, CreateBuilder(Resolver).Build(Participant.Path).ExitCode);

        public void Dispose()
        {
            Dependency?.Dispose();
            Participant.Dispose();
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

    private sealed class TemporaryRepository : IDisposable
    {
        private TemporaryRepository(string path) => Path = path;
        public string Path { get; }

        public static TemporaryRepository Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-technical-intent-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryRepository(path);
        }

        public void Write(string relativePath, string content)
        {
            var path = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            var path = System.IO.Path.GetFullPath(Path);
            var parent = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-technical-intent-tests")) + System.IO.Path.DirectorySeparatorChar;
            if (path.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) Directory.Delete(path, true);
        }
    }
}
