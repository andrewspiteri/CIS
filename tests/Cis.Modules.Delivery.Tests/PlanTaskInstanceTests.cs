using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Design;
using Cis.Modules.Graph;
using Cis.Modules.Plan;
using Cis.Modules.Repository;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void PlanInstances_DefaultVerificationUsesActualInventoryOnImportAndMigrationWithoutInventingUnitGate()
    {
        using var repository = TemporaryRepository.Create(buildGraph: false);
        var sourceRoot = Path.Combine(repository.Path, "src");
        Assert.True(CisPathSafety.IsUnderRoot(repository.Path, sourceRoot));
        Directory.Delete(sourceRoot, true);
        Assert.Equal(0, new RepositoryInitializer().Initialize(new(repository.Path, "docs/cis", false, true, AdoptEngineeringDefaults: true)).ExitCode);
        var services = CreateServices(taskTypes: CreateSearchRegistry(), engineering: true);
        Assert.Equal(0, services.Graph.Build(repository.Path).ExitCode);
        var context = Assert.IsType<CisRepositoryContext>(new CisRepositoryContextResolver().Resolve(repository.Path).Context);
        var assessment = new EngineeringAssessmentService().Assess(context, "search");
        Assert.True(assessment.Adopted);
        Assert.DoesNotContain(assessment.RequiredGates, gate => gate.Id == "unit");
        var document = Assert.IsType<CisGraphDocument>(new GraphSnapshotReader(new CisRepositoryContextResolver()).Read(repository.Path).Graph)
            .Nodes.First(node => node.Kind == "document");
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(repository.Path,
            "Plan future search behavior", "The accepted search contract remains bounded.", [new ChangeRoot(document.Key, document.Kind)])).Change);
        foreach (var finding in services.Impacts.Analyse(new(repository.Path, change.Id, [], 2, 200, false)).Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed synthetic future behavior scope.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        Assert.Equal(0, services.Plans.SelectCapability(new(repository.Path, "parr.search", "parr.search.legacy", [], "architect", "Select the known provider.")).ExitCode);
        const string source = "docs/cis/specs/future-search-feature.md";
        repository.Write(source, """
            ---
            title: Future search behavior
            type: feature-specification
            ---

            ## Functional requirements

            | ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- |
            | SEARCH-001 | backend | Build a bounded internal search projection. | Search returns the accepted current results. |
            """);
        var imported = services.Plans.ImportSpec(new(repository.Path, change.Id, source));
        Assert.True(imported.ExitCode == 0, string.Join("; ", imported.Errors.Concat(imported.Validation?.Errors ?? [])));
        var task = Assert.Single(imported.WorkItems, item => item.TaskTypeKey == "parr.search.legacy");
        Assert.Equal("review", Assert.Single(task.VerificationObligations).EvidenceRoute);
        var explicitNative = new CisFeatureStoryTask(task.Id, task.Title, task.Title, [], [], [task.AcceptanceCriteria], [])
        {
            Category = task.Category,
            ApprovalGate = "none",
            VerificationObligations = [new("native-search", [task.AcceptanceCriteria], task.Validation, "native", [task.Validation]) { SharedGateIds = ["unit"] }]
        };
        Assert.Contains(services.Plans.ValidateStoryTaskGate(repository.Path, explicitNative), error => error.Contains("absent or non-native gate 'unit'", StringComparison.Ordinal));
        Assert.Equal(0, services.Plans.SelectCapability(new(repository.Path, "parr.search", "parr.search.projection", ["parr.search.legacy"], "architect", "Select the compatible provider.")).ExitCode);
        var migrated = services.Plans.MigrateTaskType(new(repository.Path, change.Id, task.Id, "parr.search.projection", "architect", "Retain the instance while changing provider type."));
        Assert.True(migrated.ExitCode == 0, string.Join("; ", migrated.Errors.Concat(migrated.Validation?.Errors ?? [])));
        Assert.Equal("review", Assert.Single(migrated.WorkItems.Single(item => item.Id == task.Id).VerificationObligations).EvidenceRoute);
    }

    [Fact]
    public void PlanInstances_ExactCanonicalDesignGateKeepsPreparationAvailableAndRequiresActualApproval()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices(new FakeDesignProcessRunner());
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(repository.Path,
            "Bounded customer order view", "Customers inspect their order status.", [new ChangeRoot("orders-api", "component")])).Change);
        foreach (var finding in services.Impacts.Analyse(new(repository.Path, change.Id, [], 2, 200, false)).Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed synthetic view scope.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        const string source = "docs/cis/specs/customer-order-view.md";
        repository.Write(source, """
            ---
            title: Customer order view
            type: feature-specification
            ---

            ## Functional requirements

            | ID | Surface | Frontend type | Requirement | Acceptance criteria |
            | --- | --- | --- | --- | --- |
            | ORDER-001 | frontend | customer | Display the customer's order status page. | The page displays the current order status. |
            """);
        var imported = services.Plans.ImportSpec(new(repository.Path, change.Id, source));
        Assert.True(imported.ExitCode == 0, string.Join("; ", imported.Errors.Concat(imported.Validation?.Errors ?? [])));
        var design = Link(Assert.Single(imported.WorkItems, item => item.Category == "design"));
        var frontend = Link(Assert.Single(imported.WorkItems, item => item.Category == "frontend"));
        var canonicalFrontend = services.Plans.ResolveStoryTask(repository.Path, frontend with
        {
            TaskTypeKey = "CORE.FRONTEND.IMPLEMENTATION",
            Category = "Frontend",
            ApprovalGate = "NONE",
            ChangeId = null,
            WorkOwnerTaskId = null
        });
        Assert.Equal("core.frontend.implementation", canonicalFrontend.TaskTypeKey);
        Assert.Equal("frontend", canonicalFrontend.Category);
        Assert.Equal("none", canonicalFrontend.ApprovalGate);
        Assert.NotEmpty(services.Plans.ValidateStoryTaskGate(repository.Path, canonicalFrontend));
        Assert.Contains(services.Plans.ValidateStoryTaskGate(repository.Path, canonicalFrontend with { FrontendType = null }),
            error => error.Contains("classification", StringComparison.Ordinal));
        var unresolvedSize = services.Plans.ResolveStoryTask(repository.Path, frontend with { Complexity = null });
        Assert.Null(unresolvedSize.Complexity);
        Assert.Contains(CisStoryEngineeringContract.Validate(unresolvedSize), error => error.Contains("complexity", StringComparison.Ordinal));
        Assert.Empty(services.Plans.ValidateStoryTaskGate(repository.Path, design));
        Assert.NotEmpty(services.Plans.ValidateStoryTaskGate(repository.Path, design, completing: true));
        Assert.NotEmpty(services.Plans.ValidateStoryTaskGate(repository.Path, frontend));
        Assert.Contains(services.Plans.ValidateStoryTaskGate(repository.Path, frontend with { WorkOwnerTaskId = design.WorkOwnerTaskId }),
            error => error.Contains("Global design gate", StringComparison.Ordinal));
        Assert.NotEmpty(services.Plans.ValidateStoryTaskGate(repository.Path, frontend with { ChangeId = null, WorkOwnerTaskId = null }));
        Assert.NotEmpty(services.Plans.ValidateStoryTaskGate(repository.Path, design with { WorkOwnerTaskId = frontend.WorkOwnerTaskId }));

        WriteDesignWireframes(services.Changes.DossierFile(change, "wireframes.md"), change.Id, ("CUS-ORDERS", "customer"));
        Assert.Equal(0, services.Designs.Scaffold(new DesignScaffoldRequest(repository.Path, change.Id, "orders", "shell.standard-app", ["component.table"])).ExitCode);
        Assert.Equal(0, services.Designs.Render(repository.Path, change.Id).ExitCode);
        var approved = services.Designs.Approve(new DesignReviewRequest(repository.Path, change.Id, "Synthetic product owner", "Approve the exact bounded order view fixture."));
        Assert.True(approved.ExitCode == 0, string.Join("; ", approved.Diagnostics));
        Assert.Empty(CreateServices().Plans.ValidateStoryTaskGate(repository.Path, frontend));
        Assert.Empty(CreateServices().Plans.ValidateStoryTaskGate(repository.Path, design, completing: true));
        Assert.Empty(CreateServices().Plans.ValidateStoryTaskGate(repository.Path, frontend with
        {
            Id = "DOC-CHILD",
            Category = "documentation",
            TaskTypeKey = "core.documentation.contracts",
            TaskTypeVersion = "1.0"
        }));

        CisFeatureStoryTask Link(PlanWorkItem task) => new(task.Id, task.Title, task.Title, [], [], [task.AcceptanceCriteria], task.DependsOn)
        {
            Category = task.Category,
            Complexity = task.Complexity,
            TaskTypeKey = task.TaskTypeKey,
            TaskTypeVersion = task.TaskTypeVersion,
            ApprovalGate = task.ApprovalGate,
            FrontendType = task.FrontendType,
            ChangeId = change.Id,
            WorkOwnerTaskId = task.Id
        };
    }

    [Fact]
    public void PlanInstances_FreshStoryScopesReloadAndReimportWithoutMergingContractsOrExactDependencies()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(repository.Path,
            "Two bounded order outcomes", "Independent order outcomes retain their exact task contracts.", [new ChangeRoot("orders-api", "component")])).Change);
        foreach (var finding in services.Impacts.Analyse(new(repository.Path, change.Id, [], 2, 200, false)).Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed synthetic bounded scope.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        const string source = "docs/cis/specs/bounded-orders-feature.md";
        repository.Write(source, """
            ---
            title: Bounded order behavior
            type: feature-specification
            ---

            ## Functional requirements

            | ID | Story ID | Surface | Requirement | Acceptance criteria |
            | --- | --- | --- | --- | --- |
            | ORDER-001 | order-status | backend | Implement order status behavior while retaining existing visibility permissions. | Existing order status is returned unchanged. |
            | ORDER-002 | order-history | backend | Implement order history behavior while retaining existing visibility permissions. | Existing order history retains all entries. |

            ## Exclusions

            - Do not change public defaults or introduce a product screen.
            """);
        var request = new FeatureSpecImportRequest(repository.Path, change.Id, source);
        var imported = services.Plans.ImportSpec(request);
        Assert.True(imported.ExitCode == 0, string.Join("; ", imported.Errors.Concat(imported.Validation?.Errors ?? [])));
        var backend = imported.WorkItems.Where(item => item.Category == "backend").ToArray();
        Assert.Equal(2, backend.Length);
        var status = Assert.Single(backend, item => item.StoryId == "order-status");
        var history = Assert.Single(backend, item => item.StoryId == "order-history");
        Assert.NotEqual(status.Id, history.Id);
        Assert.NotEqual(status.InstanceKey, history.InstanceKey);
        Assert.Contains("Existing order status is returned unchanged.", status.AcceptanceCriteria, StringComparison.Ordinal);
        Assert.DoesNotContain("retains all entries", status.AcceptanceCriteria, StringComparison.Ordinal);
        Assert.All(status.RequiredOutputs!, output => Assert.StartsWith("order-status:", output, StringComparison.Ordinal));
        Assert.Equal(status.AcceptanceCriteria, Assert.Single(Assert.Single(status.VerificationObligations).Criteria));
        var security = Assert.Single(imported.WorkItems, item => item.Category == "security");
        Assert.Contains(security.Id, status.DependsOn);
        Assert.Contains(security.Id, history.DependsOn);
        Assert.Contains(imported.WorkItems.Single(item => item.Category == "documentation").Id, security.DependsOn);
        var linked = new CisFeatureStoryTask(status.Id, status.Title, status.Title, [], [], [status.AcceptanceCriteria], status.DependsOn)
        {
            Category = status.Category,
            Complexity = status.Complexity,
            TaskTypeKey = status.TaskTypeKey,
            TaskTypeVersion = status.TaskTypeVersion,
            ApprovalGate = status.ApprovalGate,
            ChangeId = change.Id,
            WorkOwnerTaskId = status.Id
        };
        Assert.Empty(services.Plans.ValidateStoryTaskGate(repository.Path, linked));
        Assert.Empty(services.Plans.ValidateStoryTaskGate(repository.Path, linked with
        {
            Id = "DOC-CHILD",
            Category = "documentation",
            Complexity = "low",
            TaskTypeKey = "core.documentation.contracts"
        }));
        Assert.Contains(services.Plans.ValidateStoryTaskGate(repository.Path, linked with
        {
            Id = "UI-CHILD",
            Category = "frontend",
            TaskTypeKey = "core.frontend.implementation",
            FrontendType = "customer"
        }), error => error.Contains("Global design gate", StringComparison.Ordinal));
        Assert.NotEmpty(services.Plans.ValidateStoryTaskGate(repository.Path, linked with { ApprovalGate = "global-design-approval" }));
        Assert.NotEmpty(services.Plans.ValidateStoryTaskGate(repository.Path, linked with { ChangeId = "CIS-9999" }));

        var planPath = services.Changes.DossierFile(change, "plan.md");
        var dossier = Path.GetDirectoryName(planPath)!;
        var historyPath = Path.Combine(dossier, history.TaskPath!);
        var exact = history.DependsOn.Append(status.Id).ToArray();
        var document = File.ReadAllText(historyPath).Replace("depends_on: " + JsonSerializer.Serialize(history.DependsOn),
            "depends_on: " + JsonSerializer.Serialize(exact), StringComparison.Ordinal)
            .Replace("work_owner_task_id: null", "work_owner_task_id: \"WORK-EXISTING-OWNER\"", StringComparison.Ordinal)
            .Replace("## Required outputs", "## Required outputs\n\n- Retain the reviewed history handoff artifact.", StringComparison.Ordinal)
            .Replace("## Constraints and exclusions", "## Constraints and exclusions\n\n- Preserve the authored compatibility boundary.", StringComparison.Ordinal);
        File.WriteAllText(historyPath, document);
        var lines = File.ReadAllLines(planPath);
        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].StartsWith("| " + history.Id + " |", StringComparison.Ordinal)) continue;
            var cells = lines[index].Split('|');
            cells[9] = " " + string.Join(", ", exact) + " ";
            lines[index] = string.Join('|', cells);
        }
        File.WriteAllLines(planPath, lines);
        var fresh = CreateServices();
        var loaded = fresh.Plans.Show(repository.Path, change.Id);
        var persisted = Assert.Single(loaded.WorkItems, item => item.Id == history.Id);
        Assert.Equal("WORK-EXISTING-OWNER", persisted.WorkOwnerTaskId);
        Assert.Contains("Retain the reviewed history handoff artifact.", persisted.RequiredOutputs!);
        Assert.Contains("Preserve the authored compatibility boundary.", persisted.NegativeCriteria!);
        Assert.All(history.RequiredOutputs!, output => Assert.Contains(output, persisted.RequiredOutputs!));
        Assert.Equal(JsonSerializer.Serialize(history.VerificationObligations), JsonSerializer.Serialize(persisted.VerificationObligations));
        Assert.Contains(status.Id, persisted.DependsOn);

        var reimported = fresh.Plans.ImportSpec(request);
        Assert.True(reimported.ExitCode == 0, string.Join("; ", reimported.Errors.Concat(reimported.Validation?.Errors ?? [])));
        Assert.Equal(backend.Select(item => item.Id).Order(), reimported.WorkItems.Where(item => item.Category == "backend").Select(item => item.Id).Order());
        var retained = Assert.Single(reimported.WorkItems, item => item.Id == history.Id);
        Assert.Contains(status.Id, retained.DependsOn);
        Assert.Equal("WORK-EXISTING-OWNER", retained.WorkOwnerTaskId);
        Assert.Contains("Retain the reviewed history handoff artifact.", retained.RequiredOutputs!);
        var before = File.ReadAllBytes(planPath);
        Assert.Equal("unchanged", CreateServices().Plans.ImportSpec(request).Status);
        Assert.Equal(before, File.ReadAllBytes(planPath));

        var malformed = File.ReadAllLines(planPath);
        var malformedAt = Array.FindIndex(malformed, line => line.StartsWith("| " + history.Id + " |", StringComparison.Ordinal));
        var resolvedRow = malformed[malformedAt];
        var malformedCells = malformed[malformedAt].Split('|');
        malformedCells[24] = " json64:not-valid-base64! ";
        malformed[malformedAt] = string.Join('|', malformedCells);
        File.WriteAllLines(planPath, malformed);
        var unresolvedRow = malformed[malformedAt];
        var shown = CreateServices().Plans.Show(repository.Path, change.Id);
        Assert.Equal(imported.WorkItems.Count, shown.WorkItems.Count);
        Assert.Contains(shown.Errors, error => error.Contains(history.Id, StringComparison.Ordinal));
        Assert.NotEmpty(Assert.Single(shown.WorkItems, item => item.Id == history.Id).ContractErrors);
        Assert.Empty(Assert.Single(shown.WorkItems, item => item.Id == status.Id).ContractErrors);
        Assert.Throws<InvalidDataException>(() => CreateServices().Plans.ReadTaskDigest(repository.Path, change.Id, history.Id));
        Assert.NotEmpty(CreateServices().Plans.ReadTaskDigest(repository.Path, change.Id, status.Id));
        var beforeBlocked = File.ReadAllBytes(planPath);
        var refused = CreateServices().Plans.TransitionTask(new(repository.Path, change.Id, history.Id, "InProgress", "Controller", "Unresolved selected contract cannot execute."));
        Assert.False(refused.Applied);
        Assert.Equal(beforeBlocked, File.ReadAllBytes(planPath));
        Assert.Contains(CreateServices().Plans.CompletionContext(repository.Path, change.Id, history.Id).Errors,
            error => error.Contains(history.Id, StringComparison.Ordinal));
        var untouchedTaskDocument = File.ReadAllBytes(historyPath);
        var unrelated = imported.WorkItems.Single(item => item.Category == "documentation");
        var moved = CreateServices().Plans.TransitionTask(new(repository.Path, change.Id, unrelated.Id, "InProgress", "Controller", "Continue an unrelated fully resolved task."));
        Assert.True(moved.Applied, string.Join("; ", moved.Errors));
        Assert.Contains(unresolvedRow, File.ReadAllLines(planPath));
        Assert.Equal(untouchedTaskDocument, File.ReadAllBytes(historyPath));
        var repairedRows = File.ReadAllLines(planPath);
        repairedRows[Array.FindIndex(repairedRows, line => line == unresolvedRow)] = resolvedRow;
        File.WriteAllLines(planPath, repairedRows);

        // Old same-type instances with neither story nor explicit instance
        // binding retain their IDs/history and require a targeted resolution.
        var ambiguous = File.ReadAllLines(planPath);
        for (var index = 0; index < ambiguous.Length; index++)
        {
            if (!backend.Any(item => ambiguous[index].StartsWith("| " + item.Id + " |", StringComparison.Ordinal))) continue;
            var cells = ambiguous[index].Split('|');
            cells[7] = " ORDER-001, ORDER-002 "; // Both legacy instances claim the same unsplit scope.
            cells[21] = cells[22] = " json64:" + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes<string?>(null)) + " ";
            ambiguous[index] = string.Join('|', cells);
        }
        File.WriteAllLines(planPath, ambiguous);
        foreach (var item in backend)
        {
            var path = Path.Combine(dossier, item.TaskPath!);
            var legacy = File.ReadAllText(path).Replace("story_id: " + JsonSerializer.Serialize(item.StoryId), "story_id: null", StringComparison.Ordinal)
                .Replace("instance_key: " + JsonSerializer.Serialize(item.InstanceKey), "instance_key: null", StringComparison.Ordinal);
            File.WriteAllText(path, legacy);
        }
        var beforeAmbiguous = File.ReadAllBytes(planPath);
        var blocked = CreateServices().Plans.ImportSpec(request);
        Assert.NotEqual(0, blocked.ExitCode);
        Assert.Contains(blocked.Errors, error => error.Contains("ambiguous legacy", StringComparison.Ordinal) && error.Contains(status.Id, StringComparison.Ordinal) && error.Contains(history.Id, StringComparison.Ordinal));
        Assert.Equal(beforeAmbiguous, File.ReadAllBytes(planPath));
        Assert.True(File.Exists(Path.Combine(dossier, status.TaskPath!)));
        Assert.True(File.Exists(historyPath));
    }

    [Fact]
    public void PlanInstances_AddingStoryScopesToSharedLegacyTaskRequiresExplicitOwnerMappingWithoutMutations()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = PrepareSearchChange(repository, services);
        const string source = "docs/cis/specs/search-feature-spec.md";
        WriteSource(false);
        var imported = services.Plans.ImportSpec(new(repository.Path, change.Id, source));
        Assert.Equal(0, imported.ExitCode);
        var shared = Assert.Single(imported.WorkItems, item => item.Category == "backend");
        var planPath = services.Changes.DossierFile(change, "plan.md");
        var taskPath = Path.Combine(Path.GetDirectoryName(planPath)!, shared.TaskPath!);
        File.WriteAllText(taskPath, File.ReadAllText(taskPath)
            .Replace("work_owner_task_id: null", "work_owner_task_id: \"WORK-RETAINED-ALLOCATION\"", StringComparison.Ordinal)
            .Replace("| TODO | TODO | Not run | Record exact reproducible evidence. |", "| Retained result | fixture evidence | Passed | Existing work stays retained. |", StringComparison.Ordinal));
        WriteSource(true, additional: true);
        var added = CreateServices().Plans.ImportSpec(new(repository.Path, change.Id, source));
        Assert.True(added.ExitCode == 0, string.Join("; ", added.Errors.Concat(added.Validation?.Errors ?? [])));
        var stillShared = Assert.Single(added.WorkItems, item => item.Id == shared.Id);
        Assert.Null(stillShared.StoryId);
        Assert.Equal("WORK-RETAINED-ALLOCATION", stillShared.WorkOwnerTaskId);
        var independent = Assert.Single(added.WorkItems, item => item.Category == "backend" && item.StoryId == "additional-search");
        Assert.NotEqual(shared.Id, independent.Id);
        var taskBytes = File.ReadAllBytes(taskPath);
        var planBytes = File.ReadAllBytes(planPath);
        var eventBytes = File.ReadAllBytes(services.Changes.DossierFile(change, "events.jsonl"));
        WriteSource(true, "current-search", "prior-search", true);
        var held = CreateServices().Plans.ImportSpec(new(repository.Path, change.Id, source));
        Assert.NotEqual(0, held.ExitCode);
        Assert.Contains(held.Errors, error => error.Contains(shared.Id, StringComparison.Ordinal) && error.Contains("accounting owner", StringComparison.Ordinal));
        Assert.Equal(planBytes, File.ReadAllBytes(planPath));
        Assert.Equal(taskBytes, File.ReadAllBytes(taskPath));
        Assert.Equal(eventBytes, File.ReadAllBytes(services.Changes.DossierFile(change, "events.jsonl")));
        var retained = Assert.Single(CreateServices().Plans.Show(repository.Path, change.Id).WorkItems, item => item.Id == shared.Id);
        Assert.Equal("WORK-RETAINED-ALLOCATION", retained.WorkOwnerTaskId);
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(taskPath)!, "retired")));
        WriteSource(true, "current-search", "current-search", true, true);
        var bound = CreateServices().Plans.ImportSpec(new(repository.Path, change.Id, source));
        Assert.True(bound.ExitCode == 0, string.Join("; ", bound.Errors.Concat(bound.Validation?.Errors ?? [])));
        var mapped = Assert.Single(bound.WorkItems, item => item.Id == shared.Id);
        Assert.Equal("current-search", mapped.StoryId);
        Assert.Equal(shared.InstanceKey, mapped.InstanceKey);
        Assert.Equal(shared.TaskPath, mapped.TaskPath);
        Assert.Equal("WORK-RETAINED-ALLOCATION", mapped.WorkOwnerTaskId);
        Assert.Contains("Existing work stays retained.", File.ReadAllText(taskPath), StringComparison.Ordinal);
        Assert.Equal(independent.Id, Assert.Single(bound.WorkItems, item => item.Category == "backend" && item.StoryId == "additional-search").Id);
        var newUnscoped = Assert.Single(bound.WorkItems, item => item.Category == "backend" && item.StoryId is null);
        Assert.NotEqual(shared.Id, newUnscoped.Id);
        Assert.NotEqual(mapped.InstanceKey, newUnscoped.InstanceKey);
        Assert.Equal(bound.WorkItems.Count, bound.WorkItems.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(bound.WorkItems.Count, bound.WorkItems.Select(item => item.InstanceKey).Distinct(StringComparer.Ordinal).Count());
        var oldWork = Assert.Single(bound.WorkItems, item => item.Id == shared.Id);
        Assert.Equal(mapped.InstanceKey, oldWork.InstanceKey);
        Assert.Equal("WORK-RETAINED-ALLOCATION", oldWork.WorkOwnerTaskId);
        Assert.Equal(shared.TaskPath, oldWork.TaskPath);
        Assert.Contains("Existing work stays retained.", File.ReadAllText(taskPath), StringComparison.Ordinal);
        var beforeRepeat = File.ReadAllBytes(planPath);
        Assert.Equal("unchanged", CreateServices().Plans.ImportSpec(new(repository.Path, change.Id, source)).Status);
        Assert.Equal(beforeRepeat, File.ReadAllBytes(planPath));

        void WriteSource(bool storyColumn, string first = "", string second = "", bool additional = false, bool newUnscoped = false)
        {
            var header = storyColumn ? "| ID | Story ID | Surface | Requirement | Acceptance criteria |\n| --- | --- | --- | --- | --- |"
                : "| ID | Surface | Requirement | Acceptance criteria |\n| --- | --- | --- | --- |";
            var firstScope = storyColumn ? first + " | " : "";
            var secondScope = storyColumn ? second + " | " : "";
            var extra = additional ? "\n| SEARCH-003 | additional-search | backend | Build additional search behavior. | Search returns the additional current results. |" : "";
            var unscoped = newUnscoped ? "\n| SEARCH-004 | | backend | Build a new independent search behavior. | Search returns the independent result. |" : "";
            repository.Write(source, "---\ntitle: Search projection\ntype: feature-specification\n---\n\n## Functional requirements\n\n" + header
                + $"\n| SEARCH-001 | {firstScope}backend | Build current search behavior with existing visibility permissions. | Search retains current results. |"
                + $"\n| SEARCH-002 | {secondScope}backend | Build prior search behavior with existing visibility permissions. | Search retains prior results. |" + extra + unscoped + "\n");
        }
    }
}
