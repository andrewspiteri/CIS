using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Plan;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Theory]
    [InlineData("Without network access, persistence or an integration provider, keep the current value.")]
    [InlineData("Keep the current value without network access, persistence or an integration provider.")]
    [InlineData("Without network access, persistence or an integration provider, keep current values.")]
    [InlineData("Without network access, an integration provider, or a background worker, keep the current value.")]
    [InlineData("Without network access, an integration provider, or a background worker, keep current values.")]
    [InlineData("Without network access, the call succeeds or fails.")]
    [InlineData("Without network access, external integration, or persistence, keep current values.")]
    [InlineData("Keep current values without network access, external integration, or persistence.")]
    public void PlanImportSpec_ProportionateExclusionListPlacementsKeepOnlyBackendBehavior(string requirement)
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(repository.Path,
            "Bounded local value", "Keep the current value within the component.", [new ChangeRoot("orders-api", "component")])).Change);
        foreach (var finding in services.Impacts.Analyse(new(repository.Path, change.Id, [], 1, 100, false)).Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed the bounded value behavior and exclusions.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        const string source = "docs/cis/specs/local-value.md";
        repository.Write(source, $$"""
            ---
            title: Bounded local value
            type: feature-specification
            ---

            ## Functional requirements

            | ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- |
            | VALUE-001 | backend | {{requirement}} | Replacing a value returns its previous value. |
            """);

        var result = services.Plans.ImportSpec(new(repository.Path, change.Id, source));

        Assert.True(result.ExitCode == 0, string.Join("; ", result.Errors.Concat(result.Validation?.Errors ?? [])));
        Assert.DoesNotContain(result.WorkItems, item => item.Category == "integration");
        Assert.Equal(new[] { "assurance", "backend", "coordination", "delivery", "documentation", "verification" },
            result.WorkItems.Select(item => item.Category).Order(StringComparer.Ordinal));
        var backend = Assert.Single(result.WorkItems, item => item.Category == "backend");
        Assert.Equal(new[] { "VALUE-001" }, backend.RequirementIds);
        var context = File.ReadAllText(Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!, backend.TaskPath!));
        Assert.Contains(requirement, context, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Without changing existing callers, persist the current record.")]
    [InlineData("Persist the current record without changing existing callers.")]
    [InlineData("Without changing existing callers, the service persists the current record.")]
    [InlineData("Without changing existing callers, persist each record, then return success.")]
    [InlineData("Persist each record, then return success without changing existing callers.")]
    [InlineData("Without changing existing callers, persist each record and return the result.")]
    [InlineData("Persist each record and return the result without changing existing callers.")]
    [InlineData("Without network access or changing existing callers, persist each record, and the timestamp.")]
    public void PlanImportSpec_ProportionateQualifierPlacementPreservesBackendAndPersistence(string requirement)
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(repository.Path,
            "Compatible record update", "Keep the accepted update available.", [new ChangeRoot("orders-api", "component")])).Change);
        foreach (var finding in services.Impacts.Analyse(new(repository.Path, change.Id, [], 1, 100, false)).Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed the compatible record behavior.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        const string source = "docs/cis/specs/compatible-record.md";
        repository.Write(source, $$"""
            ---
            title: Compatible record update
            type: feature-specification
            ---

            ## Functional requirements

            | ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- |
            | RECORD-001 | backend | {{requirement}} | A successful update remains available after restarting the application. |
            """);

        var result = services.Plans.ImportSpec(new(repository.Path, change.Id, source));

        Assert.True(result.ExitCode == 0, string.Join("; ", result.Errors.Concat(result.Validation?.Errors ?? [])));
        foreach (var category in new[] { "backend", "data" })
        {
            var task = Assert.Single(result.WorkItems, item => item.Category == category);
            Assert.Equal(new[] { "RECORD-001" }, task.RequirementIds);
            var context = File.ReadAllText(Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!, task.TaskPath!));
            Assert.Contains(requirement, context, StringComparison.Ordinal);
            Assert.Contains("changing existing callers", context, StringComparison.Ordinal);
        }
        Assert.Equal(new[] { "assurance", "backend", "coordination", "data", "delivery", "documentation", "verification" },
            result.WorkItems.Select(item => item.Category).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void PlanImportSpec_ProportionateQualifierKeepsJoinedEndpointAndLoadingConstraint()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(repository.Path,
            "Cached value access", "Make the loaded value available.", [new ChangeRoot("orders-api", "component")])).Change);
        foreach (var finding in services.Impacts.Analyse(new(repository.Path, change.Id, [], 1, 100, false)).Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed the cached-value boundary.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        const string source = "docs/cis/specs/cached-value-access.md";
        const string requirement = "Load the cached value without network access and expose it through an HTTP endpoint.";
        repository.Write(source, $$"""
            ---
            title: Cached value access
            type: feature-specification
            ---

            ## Functional requirements

            | ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- |
            | VALUE-001 | backend | {{requirement}} | The loaded value is available to the caller. |
            """);

        var result = services.Plans.ImportSpec(new(repository.Path, change.Id, source));

        Assert.True(result.ExitCode == 0, string.Join("; ", result.Errors.Concat(result.Validation?.Errors ?? [])));
        Assert.False(result.Source!.PublicEndpoints);
        var contract = Assert.Single(result.WorkItems, item => item.Category == "contract");
        Assert.Equal(new[] { "VALUE-001" }, contract.RequirementIds);
        var context = File.ReadAllText(Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!, contract.TaskPath!));
        Assert.Contains(requirement, context, StringComparison.Ordinal);
        Assert.Contains("without network access", context, StringComparison.Ordinal);
        Assert.DoesNotContain("PUBLIC-ENDPOINT-CACHE", contract.AcceptanceCriteria, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlanImportSpec_ProportionateScopeIgnoresMetadataExamplesAndAbsentCapabilities(bool explicitAbsence)
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(repository.Path,
            "Bounded value component", "Callers retain and replace the current value.", [new ChangeRoot("orders-api", "component")])).Change);
        foreach (var finding in services.Impacts.Analyse(new(repository.Path, change.Id, [], 1, 100, false)).Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed bounded component scope.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        const string source = "docs/cis/specs/value-component.md";
        var absence = explicitAbsence
            ? "| VALUE-002 | security | No authentication, session storage, integration provider, infrastructure deployment or background lifecycle is required. | The component does not implement authentication, persistence, metrics or background lifecycle. |\n"
            : "";
        repository.Write(source, $$"""
            ---
            title: Bounded value component
            type: feature-specification
            owner: Authentication session token provider team
            references:
              - docs/cis/specs/authentication-provider-lifecycle-recovery.md
            ---

            ## Reference examples of excluded behavior

            | ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- |
            | EXAMPLE-001 | security | Introduce an authentication provider with session storage and background lifecycle. | The deployment emits metrics and integrates an external system. |

            ## Functional requirements

            ### Excluded examples

            | ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- |
            | EXAMPLE-002 | security | Introduce an authentication provider. | The provider archives its session lifecycle. |

            ### Accepted requirements

            | ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- |
            | VALUE-001 | backend | Keep the current value and an ordinary owner field in a direct value map. | Replacing a value returns the previous value; the owner field is preserved exactly. |
            {{absence}}
            ## Non-goals and explicit exclusions

            - Do not introduce authentication, persistence, an integration provider or a background lifecycle.
            - Infrastructure deployment and telemetry are out of scope.

            ## Lifecycle

            Not applicable.

            ### Excluded lifecycle examples

            Archive sessions, restore provider state and run background lifecycle conversion.
            """);
        var result = services.Plans.ImportSpec(new(repository.Path, change.Id, source));
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Errors.Concat(result.Validation?.Errors ?? [])));
        Assert.Equal(new[] { "assurance", "backend", "coordination", "delivery", "documentation", "verification" },
            result.WorkItems.Select(item => item.Category).Order(StringComparer.Ordinal));
        Assert.False(result.Source!.PublicEndpoints);
        Assert.Equal(explicitAbsence ? new[] { "VALUE-001", "VALUE-002" } : ["VALUE-001"], result.Source.RequirementIds);
        var verification = Assert.Single(result.WorkItems, item => item.Category == "verification");
        Assert.DoesNotContain(verification.RequiredOutputs!, output => output.StartsWith("test-cases.", StringComparison.Ordinal));
        Assert.DoesNotContain("TC-*", verification.Validation, StringComparison.Ordinal);
        var taskPath = Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!, verification.TaskPath!);
        var task = File.ReadAllText(taskPath);
        var matrix = task.Split("## Structured test-obligation matrix", StringSplitOptions.None)[1]
            .Split("## Constraints and exclusions", StringSplitOptions.None)[0];
        var rows = matrix.Split('\n').Where(line => line.StartsWith("| ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(verification.VerificationObligations.Count + 1, rows.Length);
        var obligation = Assert.Single(verification.VerificationObligations);
        Assert.Contains($"| {obligation.Id} | {obligation.EvidenceRoute} |", matrix, StringComparison.Ordinal);
        Assert.Contains(obligation.Description, matrix, StringComparison.Ordinal);
        Assert.Contains("VALUE-001", matrix, StringComparison.Ordinal);
        Assert.DoesNotContain("| security |", matrix, StringComparison.Ordinal);
        Assert.DoesNotContain("| mutation |", matrix, StringComparison.Ordinal);
        Assert.DoesNotContain("95%", matrix, StringComparison.Ordinal);
        Assert.DoesNotContain("Browser regression", task, StringComparison.Ordinal);
        var repeated = services.Plans.ImportSpec(new(repository.Path, change.Id, source));
        Assert.Equal("unchanged", repeated.Status);
        Assert.Equal(result.WorkItems.Select(item => (item.Id, item.InstanceKey)), repeated.WorkItems.Select(item => (item.Id, item.InstanceKey)));
    }

    [Theory]
    [InlineData("Authenticate authorized requests without persistence or background workers.")]
    [InlineData("Authenticate authorized requests without querying the database and repository that stores them.")]
    [InlineData("Authenticate authorized requests, and the component does not implement persistence or background lifecycle.")]
    public void PlanImportSpec_ProportionateScopeKeepsAuthorizationAndConcurrencyBeforeAbsentCapabilities(string authentication)
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(repository.Path,
            "Authorized value updates", "Only authorized callers update the current value atomically.", [new ChangeRoot("orders-api", "component")])).Change);
        foreach (var finding in services.Impacts.Analyse(new(repository.Path, change.Id, [], 1, 100, false)).Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed authorization and concurrency boundary.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        const string source = "docs/cis/specs/authorized-value.md";
        repository.Write(source, $$"""
            ---
            title: Authorized value updates
            type: feature-specification
            ---

            ## Functional requirements

            | ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- |
            | ACCESS-001 | backend | {{authentication}} Concurrent updates preserve one atomic in-memory value. | Concurrent authorized requests have one accepted outcome. |
            | ACCESS-002 | security | Deny unauthorized callers and never expose secrets. | No unauthorized principal receives another principal's value. |
            | ACCESS-003 | delivery | No endpoint without authentication may be included. | No public endpoint may be introduced. |
            | ACCESS-004 | api | The internal endpoint rejects requests without authentication. | Unauthorized callers are denied. |
            """);
        var result = services.Plans.ImportSpec(new(repository.Path, change.Id, source));
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Errors.Concat(result.Validation?.Errors ?? [])));
        Assert.Equal(new[] { "assurance", "backend", "contract", "coordination", "delivery", "documentation", "security", "verification" },
            result.WorkItems.Select(item => item.Category).Order(StringComparer.Ordinal));
        Assert.False(result.Source!.PublicEndpoints);
        var security = Assert.Single(result.WorkItems, item => item.Category == "security");
        Assert.Equal(new[] { "ACCESS-001", "ACCESS-002", "ACCESS-004" }, security.RequirementIds);
        var backend = Assert.Single(result.WorkItems, item => item.Category == "backend");
        Assert.Contains(security.Id, backend.DependsOn);
        Assert.Contains("concurrency", backend.AcceptanceCriteria, StringComparison.Ordinal);
        var directory = Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!;
        Assert.Contains("Concurrent updates preserve one atomic in-memory value.", File.ReadAllText(Path.Combine(directory, backend.TaskPath!)), StringComparison.Ordinal);
        var verification = Assert.Single(result.WorkItems, item => item.Category == "verification");
        Assert.Contains(verification.RequiredOutputs!, output => output.StartsWith("test-cases.md", StringComparison.Ordinal));
        var matrix = File.ReadAllText(Path.Combine(directory, verification.TaskPath!))
            .Split("## Structured test-obligation matrix", StringSplitOptions.None)[1]
            .Split("## Constraints and exclusions", StringSplitOptions.None)[0];
        Assert.Single(verification.VerificationObligations);
        Assert.Contains("| task-contract | review |", matrix, StringComparison.Ordinal);
        Assert.DoesNotContain("| mutation |", matrix, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FeaturePack_ProportionateMatrixRetainsExplicitReleaseAndNativeObligationsAcrossDefaultRefresh(bool authoredTaskContract)
    {
        using var repository = TemporaryRepository.Create();
        const string source = "docs/cis/specs/direct-value.md";
        repository.Write(source, """
            ---
            title: Direct value component
            type: feature-specification
            ---

            ## Functional requirements

            | ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- |
            | VALUE-001 | backend | Replace the current value directly. | The previous value is returned. |
            """);
        var registry = new TaskTypeRegistry([new CoreTaskTypeProvider()]);
        var first = Assert.IsType<FeatureIssuePack>(FeatureIssuePackBuilder.Build(repository.Path, source, "CIS-0001", [], registry, [], [],
            new Dictionary<string, IReadOnlyList<string>>(), defaultObligations:
            task => FeatureIssuePackBuilder.DefaultVerificationObligations(task, ["unit"])).Pack);
        var verification = Assert.Single(first.WorkItems, item => item.Category == "verification");
        var priorGenerated = Assert.Single(verification.VerificationObligations) with
        {
            Description = "Historical default validation.",
            EvidenceExpectations = ["Historical default validation."],
        };
        var priorContract = priorGenerated with
        {
            SharedGateIds = authoredTaskContract ? ["security"] : ["unit"],
        };
        var release = new CisTaskVerificationObligation("accepted-release", [verification.AcceptanceCriteria],
            "Retain the accepted report at the release boundary.", "artifact", ["release/report.json"])
        {
            SharedGateIds = ["release-smoke"],
            BasisReferences = ["accepted-release-decision"],
        };
        var existing = first.WorkItems.Select(item => item.Id == verification.Id ? item with
        {
            Validation = "Historical default validation.",
            VerificationObligations = [priorContract, release],
        } : item).ToArray();
        var refreshed = Assert.IsType<FeatureIssuePack>(FeatureIssuePackBuilder.Build(repository.Path, source, "CIS-0001", [], registry, [], existing,
            new Dictionary<string, IReadOnlyList<string>>(), first.Source.Sha256,
            task => FeatureIssuePackBuilder.DefaultVerificationObligations(task, ["unit"]),
            task => new(task.RequiredOutputs ?? [], task.NegativeCriteria ?? [])
            {
                VerificationObligations = task.Id == verification.Id ? [priorGenerated]
                    : first.WorkItems.Single(prior => prior.Id == task.Id).VerificationObligations,
            }).Pack);
        var current = Assert.Single(refreshed.WorkItems, item => item.Category == "verification");
        Assert.Equal(verification.Id, current.Id);
        Assert.Equal(verification.InstanceKey, current.InstanceKey);
        Assert.Equal(JsonSerializer.Serialize(release), JsonSerializer.Serialize(Assert.Single(current.VerificationObligations, obligation => obligation.Id == release.Id)));
        var currentContract = Assert.Single(current.VerificationObligations, obligation => obligation.Id == "task-contract");
        Assert.Equal(authoredTaskContract ? new[] { "security" } : ["unit"], currentContract.SharedGateIds);
        if (authoredTaskContract) Assert.Equal(JsonSerializer.Serialize(priorContract), JsonSerializer.Serialize(currentContract));
        else Assert.Equal(current.Validation, currentContract.Description);
        Assert.Equal(current.VerificationObligations.Count, current.VerificationObligations.Select(obligation => obligation.Id).Distinct(StringComparer.Ordinal).Count());
        var matrix = refreshed.TaskDocuments[current.TaskPath!].Split("## Structured test-obligation matrix", StringSplitOptions.None)[1]
            .Split("## Constraints and exclusions", StringSplitOptions.None)[0];
        Assert.Equal(3, matrix.Split('\n').Count(line => line.StartsWith("| ", StringComparison.Ordinal)));
        Assert.Contains("| task-contract | native |", matrix, StringComparison.Ordinal);
        Assert.Contains(authoredTaskContract ? "shared gates: security" : "shared gates: unit", matrix, StringComparison.Ordinal);
        Assert.Contains("| accepted-release | artifact | accepted-release-decision;", matrix, StringComparison.Ordinal);
        Assert.Contains("release/report.json; shared gates: release-smoke", matrix, StringComparison.Ordinal);
        Assert.Contains("release obligation does not itself require a new release campaign for each story", matrix, StringComparison.Ordinal);
        Assert.DoesNotContain("| mutation |", matrix, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("The health endpoint does not require authentication.", true)]
    [InlineData("The web exposes a health endpoint without requiring authentication.", true)]
    [InlineData("No endpoint without authentication may be included.", false)]
    public void PlanImportSpec_ProportionateScopeDistinguishesAnAnonymousEndpointFromItsExclusion(string endpoint, bool isPublic)
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(repository.Path,
            "Bounded health boundary", "The declared health boundary remains explicit.", [new ChangeRoot("orders-api", "component")])).Change);
        foreach (var finding in services.Impacts.Analyse(new(repository.Path, change.Id, [], 1, 100, false)).Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed the declared endpoint boundary.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        const string source = "docs/cis/specs/health-boundary.md";
        repository.Write(source, $$"""
            ---
            title: Bounded health boundary
            type: feature-specification
            ---

            ## Functional requirements

            | ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- |
            | VALUE-001 | backend | Replace the current value directly. | The previous value is returned. |
            | HEALTH-001 | delivery | {{endpoint}} | The declared health boundary is preserved. |
            """);
        var result = services.Plans.ImportSpec(new(repository.Path, change.Id, source));
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Errors.Concat(result.Validation?.Errors ?? [])));
        Assert.Equal(isPublic, result.Source!.PublicEndpoints);
        Assert.Equal(isPublic
                ? new[] { "assurance", "backend", "contract", "coordination", "delivery", "documentation", "observability", "security", "verification" }
                : ["assurance", "backend", "coordination", "delivery", "documentation", "verification"],
            result.WorkItems.Select(item => item.Category).Order(StringComparer.Ordinal));
        var verification = Assert.Single(result.WorkItems, item => item.Category == "verification");
        Assert.Equal(isPublic, verification.AcceptanceCriteria.Contains("PUBLIC-ENDPOINT-CACHE", StringComparison.Ordinal));
        var task = File.ReadAllText(Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!, verification.TaskPath!));
        var matrix = task.Split("## Structured test-obligation matrix", StringSplitOptions.None)[1]
            .Split("## Constraints and exclusions", StringSplitOptions.None)[0];
        Assert.Equal(2, matrix.Split('\n').Count(line => line.StartsWith("| ", StringComparison.Ordinal)));
        Assert.Contains("| task-contract | review |", matrix, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanImportSpec_ProportionateScopeIgnoresNestedNarrativeAndFencedExamples()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(repository.Path,
            "Direct internal value behavior", "The internal value remains directly available.", [new ChangeRoot("orders-api", "component")])).Change);
        foreach (var finding in services.Impacts.Analyse(new(repository.Path, change.Id, [], 1, 100, false)).Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed direct value scope.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        const string source = "docs/cis/specs/narrative-value.md";
        repository.Write(source, """
            ---
            title: Direct internal value behavior
            type: feature-specification
            ---

            ## Goals

            1. An internal service keeps the current value directly.

            ### Excluded examples

            2. Introduce authentication providers, session storage and lifecycle conversion.

            ### Accepted behavior

            3. Replacing a value returns the prior value.

            ```markdown
            ## Lifecycle
            4. Archive sessions and integrate an external provider.
            ```

            ## Lifecycle

            Not applicable.

            ### Excluded examples

            Archive sessions and restore provider state.
            """);
        var result = services.Plans.ImportSpec(new(repository.Path, change.Id, source));
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Errors.Concat(result.Validation?.Errors ?? [])));
        Assert.Equal(new[] { "GOAL-001", "GOAL-002" }, result.Source!.RequirementIds);
        Assert.Equal(new[] { "assurance", "backend", "coordination", "delivery", "documentation", "verification" },
            result.WorkItems.Select(item => item.Category).Order(StringComparer.Ordinal));
        var verification = Assert.Single(result.WorkItems, item => item.Category == "verification");
        var task = File.ReadAllText(Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!, verification.TaskPath!));
        var matrix = task.Split("## Structured test-obligation matrix", StringSplitOptions.None)[1]
            .Split("## Constraints and exclusions", StringSplitOptions.None)[0];
        Assert.Equal(2, matrix.Split('\n').Count(line => line.StartsWith("| ", StringComparison.Ordinal)));
        Assert.Contains("| task-contract | review |", matrix, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Requests without a valid session are rejected.")]
    [InlineData("Users without the editor permission cannot change the value.")]
    [InlineData("Requests are rejected when the session token is not included.")]
    public void PlanImportSpec_ProportionateScopeRetainsConditionalSessionAndPermissionDenials(string denial)
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(repository.Path,
            "Bounded access restriction", "The existing value follows the accepted access restriction.", [new ChangeRoot("orders-api", "component")])).Change);
        foreach (var finding in services.Impacts.Analyse(new(repository.Path, change.Id, [], 1, 100, false)).Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed the declared access restriction.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        const string source = "docs/cis/specs/restricted-value.md";
        repository.Write(source, $$"""
            ---
            title: Bounded access restriction
            type: feature-specification
            ---

            ## Functional requirements

            | ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- |
            | ACCESS-001 | backend | {{denial}} | The denied call leaves the current value unchanged. |
            """);
        var result = services.Plans.ImportSpec(new(repository.Path, change.Id, source));
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Errors.Concat(result.Validation?.Errors ?? [])));
        Assert.Equal(denial.StartsWith("Requests", StringComparison.Ordinal)
                ? new[] { "assurance", "backend", "contract", "coordination", "delivery", "documentation", "security", "verification" }
                : ["assurance", "backend", "coordination", "delivery", "documentation", "security", "verification"],
            result.WorkItems.Select(item => item.Category).Order(StringComparer.Ordinal));
        Assert.False(result.Source!.PublicEndpoints);
        var security = Assert.Single(result.WorkItems, task => task.Category == "security");
        Assert.Equal(new[] { "ACCESS-001" }, security.RequirementIds);
        var directory = Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!;
        Assert.Contains(denial, File.ReadAllText(Path.Combine(directory, security.TaskPath!)), StringComparison.Ordinal);
        var verification = Assert.Single(result.WorkItems, task => task.Category == "verification");
        Assert.Contains(verification.RequiredOutputs!, output => output.StartsWith("test-cases.md", StringComparison.Ordinal));
        var matrix = File.ReadAllText(Path.Combine(directory, verification.TaskPath!))
            .Split("## Structured test-obligation matrix", StringSplitOptions.None)[1]
            .Split("## Constraints and exclusions", StringSplitOptions.None)[0];
        Assert.Equal(2, matrix.Split('\n').Count(line => line.StartsWith("| ", StringComparison.Ordinal)));
        Assert.Contains("| task-contract | review |", matrix, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unit", "security", false)]
    [InlineData(null, "unit", true)]
    [InlineData("unit", null, true)]
    public void FeaturePack_ProportionateObligationBaselineRefreshesPolicyRoutesGatesAndAcceptedCriteria(string? priorGate, string? currentGate, bool reviseAcceptance)
    {
        using var repository = TemporaryRepository.Create();
        const string source = "docs/cis/specs/atomic-value.md";
        const string originalAcceptance = "The previous value is returned exactly.";
        const string revisedAcceptance = "The previous value and its owner field are returned exactly.";
        repository.Write(source, """
            ---
            title: Atomic value behavior
            type: feature-specification
            ---

            ## Functional requirements

            | ID | Story ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- | --- |
            | VALUE-001 | value | backend | Replace the current value directly. | The previous value is returned exactly. |
            """);
        var registry = new TaskTypeRegistry([new CoreTaskTypeProvider()]);
        var first = Assert.IsType<FeatureIssuePack>(FeatureIssuePackBuilder.Build(repository.Path, source, "CIS-0001", [], registry, [], [],
            new Dictionary<string, IReadOnlyList<string>>(), defaultObligations:
            task => FeatureIssuePackBuilder.DefaultVerificationObligations(task, priorGate is null ? [] : [priorGate])).Pack);
        var previous = Assert.Single(first.WorkItems, task => task.Category == "backend");
        if (reviseAcceptance)
            repository.Write(source, File.ReadAllText(Path.Combine(repository.Path, source)).Replace(originalAcceptance, revisedAcceptance, StringComparison.Ordinal));
        var refreshed = FeatureIssuePackBuilder.Build(repository.Path, source, "CIS-0001", [], registry, [], first.WorkItems,
            new Dictionary<string, IReadOnlyList<string>>(), first.Source.Sha256,
            task => FeatureIssuePackBuilder.DefaultVerificationObligations(task, currentGate is null ? [] : [currentGate]),
            task => new(task.RequiredOutputs ?? [], task.NegativeCriteria ?? [])
            { VerificationObligations = first.WorkItems.Single(prior => prior.Id == task.Id).VerificationObligations });
        Assert.Empty(refreshed.Errors);
        var pack = Assert.IsType<FeatureIssuePack>(refreshed.Pack);
        var current = Assert.Single(pack.WorkItems, task => task.Category == "backend");
        Assert.Equal(previous.Id, current.Id);
        Assert.Equal(previous.InstanceKey, current.InstanceKey);
        var obligation = Assert.Single(current.VerificationObligations);
        Assert.Equal(currentGate is null ? "review" : "native", obligation.EvidenceRoute);
        Assert.Equal(currentGate is null ? Array.Empty<string>() : [currentGate], obligation.SharedGateIds);
        Assert.Equal(new[] { current.AcceptanceCriteria }, obligation.Criteria);
        Assert.Equal(current.Validation, obligation.Description);
        if (reviseAcceptance)
        {
            Assert.Contains(revisedAcceptance, current.AcceptanceCriteria, StringComparison.Ordinal);
            Assert.DoesNotContain(originalAcceptance, obligation.Criteria.Single(), StringComparison.Ordinal);
        }
        Assert.Contains("imported_verification_obligations:", pack.TaskDocuments[current.TaskPath!], StringComparison.Ordinal);
        var unproven = FeatureIssuePackBuilder.Build(repository.Path, source, "CIS-0001", [], registry, [], first.WorkItems,
            new Dictionary<string, IReadOnlyList<string>>(), first.Source.Sha256,
            task => FeatureIssuePackBuilder.DefaultVerificationObligations(task, currentGate is null ? [] : [currentGate]),
            task => new(task.RequiredOutputs ?? [], task.NegativeCriteria ?? []));
        Assert.Null(unproven.Pack);
        Assert.Contains(unproven.Errors, error => error.Contains("no retained generated verification baseline", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlanImportSpec_ProportionateAuthoredStaleCriteriaAndMissingBaselineHoldWithoutWrites(bool missingBaseline)
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(repository.Path,
            "Authored value proof", "The accepted value behavior retains its explicit proof.", [new ChangeRoot("orders-api", "component")])).Change);
        foreach (var finding in services.Impacts.Analyse(new(repository.Path, change.Id, [], 1, 100, false)).Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed the explicit proof boundary.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        const string source = "docs/cis/specs/authored-value.md";
        repository.Write(source, """
            ---
            title: Authored value proof
            type: feature-specification
            ---

            ## Functional requirements

            | ID | Story ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- | --- |
            | VALUE-001 | value | backend | Replace the current value directly. | The previous value is returned exactly. |
            """);
        var request = new FeatureSpecImportRequest(repository.Path, change.Id, source);
        var first = services.Plans.ImportSpec(request);
        Assert.Equal(0, first.ExitCode);
        var backend = Assert.Single(first.WorkItems, task => task.Category == "backend");
        var authored = new CisTaskVerificationObligation("accepted-proof", [backend.AcceptanceCriteria],
            "Retain the accepted independent value proof.", "artifact", ["accepted/value-proof.json"]);
        var planPath = services.Changes.DossierFile(change, "plan.md");
        var taskPath = Path.Combine(Path.GetDirectoryName(planPath)!, backend.TaskPath!);
        var document = Regex.Replace(File.ReadAllText(taskPath), @"(?m)^verification_obligations: [^\r\n]*",
            _ => "verification_obligations: " + JsonSerializer.Serialize(backend.VerificationObligations.Append(authored)));
        if (missingBaseline)
            document = Regex.Replace(document, @"(?m)^imported_verification_obligations: [^\r\n]*\r?\n?", "");
        File.WriteAllText(taskPath, document);
        var planBefore = File.ReadAllBytes(planPath);
        var taskBefore = File.ReadAllBytes(taskPath);
        repository.Write(source, File.ReadAllText(Path.Combine(repository.Path, source))
            .Replace("The previous value is returned exactly.", "The previous value and its owner are returned exactly.", StringComparison.Ordinal));
        var held = services.Plans.ImportSpec(request);
        Assert.False(held.Applied);
        Assert.NotEqual(0, held.ExitCode);
        Assert.Contains(held.Errors, error => error.Contains("authored verification obligation citing a replaced acceptance criterion", StringComparison.Ordinal));
        Assert.DoesNotContain(held.Errors, error => error.Contains("no retained generated verification baseline", StringComparison.Ordinal));
        Assert.Equal(planBefore, File.ReadAllBytes(planPath));
        Assert.Equal(taskBefore, File.ReadAllBytes(taskPath));
        Assert.Contains("accepted-proof", File.ReadAllText(taskPath), StringComparison.Ordinal);
    }

    [Fact]
    public void FeaturePack_ProportionateObligationReplacementDoesNotReintroduceRemovedDefaultOnPolicyChange()
    {
        using var repository = TemporaryRepository.Create();
        const string source = "docs/cis/specs/direct-proof.md";
        repository.Write(source, """
            ---
            title: Direct value proof
            type: feature-specification
            ---

            ## Functional requirements

            | ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- |
            | VALUE-001 | backend | Replace the current value directly. | The previous value is returned exactly. |
            """);
        var registry = new TaskTypeRegistry([new CoreTaskTypeProvider()]);
        var first = Assert.IsType<FeatureIssuePack>(FeatureIssuePackBuilder.Build(repository.Path, source, "CIS-0001", [], registry, [], [],
            new Dictionary<string, IReadOnlyList<string>>(), defaultObligations:
            task => FeatureIssuePackBuilder.DefaultVerificationObligations(task, ["unit"])).Pack);
        var verification = Assert.Single(first.WorkItems, task => task.Category == "verification");
        var replacement = new CisTaskVerificationObligation("accepted-direct-proof", [verification.AcceptanceCriteria],
            "Retain the accepted direct value proof.", "artifact", ["accepted/direct-value.json"])
        { BasisReferences = ["accepted-proof-decision"] };
        var existing = first.WorkItems.Select(task => task.Id == verification.Id
            ? task with { VerificationObligations = [replacement] } : task).ToArray();
        var refreshed = FeatureIssuePackBuilder.Build(repository.Path, source, "CIS-0001", [], registry, [], existing,
            new Dictionary<string, IReadOnlyList<string>>(), first.Source.Sha256,
            task => FeatureIssuePackBuilder.DefaultVerificationObligations(task, ["security"]),
            task => new(task.RequiredOutputs ?? [], task.NegativeCriteria ?? [])
            { VerificationObligations = first.WorkItems.Single(prior => prior.Id == task.Id).VerificationObligations });
        Assert.Empty(refreshed.Errors);
        var pack = Assert.IsType<FeatureIssuePack>(refreshed.Pack);
        var current = Assert.Single(pack.WorkItems, task => task.Category == "verification");
        Assert.Equal(verification.Id, current.Id);
        Assert.Equal(JsonSerializer.Serialize(replacement), JsonSerializer.Serialize(Assert.Single(current.VerificationObligations)));
        var matrix = pack.TaskDocuments[current.TaskPath!].Split("## Structured test-obligation matrix", StringSplitOptions.None)[1]
            .Split("## Constraints and exclusions", StringSplitOptions.None)[0];
        Assert.Equal(2, matrix.Split('\n').Count(line => line.StartsWith("| ", StringComparison.Ordinal)));
        Assert.Contains("| accepted-direct-proof | artifact |", matrix, StringComparison.Ordinal);
        Assert.DoesNotContain("| task-contract |", matrix, StringComparison.Ordinal);
        Assert.Contains("Applicable adopted completion policy remains binding at its declared task, feature or release boundary", matrix, StringComparison.Ordinal);
        var legacy = FeatureIssuePackBuilder.Build(repository.Path, source, "CIS-0001", [], registry, [], existing,
            new Dictionary<string, IReadOnlyList<string>>(), first.Source.Sha256,
            task => FeatureIssuePackBuilder.DefaultVerificationObligations(task, ["security"]),
            task => new(task.RequiredOutputs ?? [], task.NegativeCriteria ?? []));
        Assert.Null(legacy.Pack);
        Assert.Contains(legacy.Errors, error => error.Contains("no retained generated verification baseline", StringComparison.Ordinal));
    }

    [Fact]
    public void PlanImportSpec_ProportionateLegacyDefaultWithoutBaselineUpgradesThroughNormalImport()
    {
        using var repository = TemporaryRepository.Create();
        const string priorAcceptance = "Every stable manual case is reflected by recognized automated coverage, and all acceptance and negative criteria have reproducible passing evidence or approved residual risk.";
        const string priorValidation = "Trace TC-* identities into automated tests, refresh the catalogue, then run focused and affected suites, contract/migration checks, browser journeys, and coverage gates.";
        var legacyRegistry = new TaskTypeRegistry([new TestTaskTypeProvider("cis.core", new CoreTaskTypeProvider().GetTaskTypes()
            .Select(definition => definition.Key == "core.verification"
                ? definition with { AcceptanceCriteria = priorAcceptance, Validation = priorValidation } : definition).ToArray())]);
        var legacyServices = CreateServices(taskTypes: legacyRegistry);
        var change = Assert.IsType<ChangeDossier>(legacyServices.Changes.Create(new ChangeCreateRequest(repository.Path,
            "Existing direct value component", "The existing component retains its bounded value behavior.", [new ChangeRoot("orders-api", "component")])).Change);
        foreach (var finding in legacyServices.Impacts.Analyse(new(repository.Path, change.Id, [], 1, 100, false)).Findings)
            legacyServices.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed the existing bounded behavior.");
        DefineAcceptanceCriteria(legacyServices.Changes.DossierFile(change, "proposal.md"));
        const string source = "docs/cis/specs/existing-value.md";
        repository.Write(source, """
            ---
            title: Existing direct value component
            type: feature-specification
            ---

            ## Functional requirements

            | ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- |
            | VALUE-001 | backend | Replace the current value directly. | The previous value is returned exactly. |
            """);
        var request = new FeatureSpecImportRequest(repository.Path, change.Id, source);
        var original = legacyServices.Plans.ImportSpec(request);
        Assert.Equal(0, original.ExitCode);
        var priorVerification = Assert.Single(original.WorkItems, task => task.Category == "verification");
        Assert.Equal(priorAcceptance, priorVerification.AcceptanceCriteria);
        Assert.Contains("Trace TC-* identities", priorVerification.Validation, StringComparison.Ordinal);
        var directory = Path.GetDirectoryName(legacyServices.Changes.DossierFile(change, "plan.md"))!;
        foreach (var task in original.WorkItems)
        {
            var path = Path.Combine(directory, task.TaskPath!);
            File.WriteAllText(path, Regex.Replace(File.ReadAllText(path),
                @"(?m)^imported_verification_obligations: [^\r\n]*\r?\n?", ""));
        }
        var currentServices = CreateServices();
        var refreshed = currentServices.Plans.ImportSpec(request);
        Assert.True(refreshed.ExitCode == 0, string.Join("; ", refreshed.Errors.Concat(refreshed.Validation?.Errors ?? [])));
        Assert.Equal("Draft", refreshed.PlanStatus);
        Assert.Equal(original.Source!.Sha256, refreshed.Source!.Sha256);
        Assert.Equal(original.WorkItems.Select(task => (task.Id, task.InstanceKey)), refreshed.WorkItems.Select(task => (task.Id, task.InstanceKey)));
        var currentVerification = Assert.Single(refreshed.WorkItems, task => task.Category == "verification");
        Assert.NotEqual(priorAcceptance, currentVerification.AcceptanceCriteria);
        Assert.DoesNotContain("Trace TC-* identities", currentVerification.Validation, StringComparison.Ordinal);
        var obligation = Assert.Single(currentVerification.VerificationObligations);
        Assert.Equal(new[] { currentVerification.AcceptanceCriteria }, obligation.Criteria);
        Assert.Equal(currentVerification.Validation, obligation.Description);
        Assert.All(refreshed.WorkItems, task => Assert.Contains("imported_verification_obligations:",
            File.ReadAllText(Path.Combine(directory, task.TaskPath!)), StringComparison.Ordinal));
        Assert.Equal("unchanged", CreateServices().Plans.ImportSpec(request).Status);
    }
}
