using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Modules.Change;
using Cis.Modules.Impact;
using Cis.Modules.Plan;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void AdHocPlanBuild_MaterializesOnlyAcceptedBoundedWorkWithOrdinaryTaskLifecycle()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var accepted = AdHocFinding("IMPACT-ALPHA", "orders-api-alpha");
        var change = CreateAdHocChange(repository, services, accepted,
            AdHocFinding("IMPACT-EXCLUDED", "orders-api-excluded") with { State = "rejected" });

        var built = services.Plans.Build(repository.Path, change.Id);
        AssertAdHocSuccess(built);
        var item = Assert.Single(built.WorkItems);
        Assert.Equal([accepted.Id], item.ImpactIds);
        Assert.Equal("verification", item.Category);
        Assert.Null(built.Source);
        Assert.False(string.IsNullOrWhiteSpace(item.TaskPath));
        var document = File.ReadAllText(AdHocTaskPath(services, change, item));
        foreach (var required in new[]
                 {
                     $"task_id: {item.Id}", $"task_type: {item.TaskTypeKey}",
                     "## Objective", "## Required changes", "## Required outputs",
                     "## Constraints and exclusions", "## Dependencies and approval gates",
                     "## Acceptance criteria", "## Targeted validation", "## Completion evidence",
                     "## Deferrals and residual risk",
                 })
            Assert.Contains(required, document, StringComparison.Ordinal);
        Assert.Contains($"path: {change.RelativePath}/{item.TaskPath}",
            File.ReadAllText(Path.Combine(repository.Path, "docs/cis/catalog.yml")), StringComparison.Ordinal);
        AssertAdHocSuccess(services.Plans.Validate(repository.Path, change.Id));

        var initialPaths = new[]
        {
            AdHocTaskPath(services, change, item), services.Changes.DossierFile(change, "plan.md"),
            Path.Combine(repository.Path, "docs/cis/catalog.yml"),
            services.Changes.DossierFile(change, "events.jsonl"),
        };
        foreach (var path in initialPaths)
            File.SetLastWriteTimeUtc(path, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        var initialBytes = AdHocSnapshot(initialPaths);
        var initialTimes = initialPaths.ToDictionary(path => path, File.GetLastWriteTimeUtc, StringComparer.Ordinal);
        var firstRebuild = CreateServices().Plans.Build(repository.Path, change.Id);
        AssertAdHocSuccess(firstRebuild);
        Assert.Equal("unchanged", firstRebuild.Status);
        Assert.False(firstRebuild.Applied);
        AssertAdHocSnapshot(initialBytes);
        foreach (var path in initialPaths) Assert.Equal(initialTimes[path], File.GetLastWriteTimeUtc(path));

        AssertAdHocSuccess(services.Plans.Approve(repository.Path, change.Id,
            "Synthetic reviewer", "Approve only the bounded synthetic verification task."));
        var started = services.Plans.TransitionTask(new PlanTaskTransitionRequest(
            repository.Path, change.Id, item.Id, "InProgress", "Synthetic controller",
            "Start the approved synthetic task through its ordinary lifecycle."));
        AssertAdHocSuccess(started);
        Assert.Equal("InProgress", Assert.Single(started.WorkItems).Status);
    }

    [Fact]
    public void AdHocPlanBuild_ChangedImpactOrderAndMembershipPreserveOwnersWithoutRecyclingRetiredIds()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var alpha = AdHocFinding("IMPACT-ALPHA", "orders-api-alpha");
        var bravo = AdHocFinding("IMPACT-BRAVO", "orders-api-bravo");
        var change = CreateAdHocChange(repository, services, alpha, bravo);
        var first = services.Plans.Build(repository.Path, change.Id);
        AssertAdHocSuccess(first);
        var alphaTask = AdHocTaskFor(first, alpha.Id);
        var bravoTask = AdHocTaskFor(first, bravo.Id);
        var alphaPath = AdHocTaskPath(services, change, alphaTask);
        var bravoPath = AdHocTaskPath(services, change, bravoTask);
        WriteAdHocAuthoredContract(alphaPath, alphaTask.Id, "alpha-owned");
        WriteAdHocAuthoredContract(bravoPath, bravoTask.Id, "bravo-owned");
        var alphaBytes = File.ReadAllBytes(alphaPath);

        // The new finding sorts before both existing targets. Positional reassignment
        // would transfer WORK-001 and its existing evidence to this different work.
        var charlie = AdHocFinding("IMPACT-CHARLIE", "orders-api-0-charlie");
        WriteAdHocImpact(services, change, bravo, charlie, alpha);
        var added = CreateServices().Plans.Build(repository.Path, change.Id);
        AssertAdHocSuccess(added);
        Assert.Equal(alphaTask.Id, AdHocTaskFor(added, alpha.Id).Id);
        Assert.Equal(bravoTask.Id, AdHocTaskFor(added, bravo.Id).Id);
        var charlieTask = AdHocTaskFor(added, charlie.Id);
        Assert.DoesNotContain(charlieTask.Id, first.WorkItems.Select(item => item.Id));
        Assert.Equal(alphaBytes, File.ReadAllBytes(alphaPath));
        Assert.DoesNotContain("alpha-owned", File.ReadAllText(AdHocTaskPath(services, change, charlieTask)), StringComparison.Ordinal);

        WriteAdHocImpact(services, change, alpha);
        var removed = CreateServices().Plans.Build(repository.Path, change.Id);
        AssertAdHocSuccess(removed);
        Assert.Equal(alphaTask.Id, Assert.Single(removed.WorkItems).Id);
        var retiredBravo = AdHocRetiredTaskPath(services, change, bravoTask);
        var retiredCharlie = AdHocRetiredTaskPath(services, change, charlieTask);
        Assert.False(File.Exists(bravoPath));
        Assert.True(File.Exists(retiredBravo));
        Assert.True(File.Exists(retiredCharlie));
        Assert.Contains("bravo-owned", File.ReadAllText(retiredBravo), StringComparison.Ordinal);
        Assert.Contains($"work_owner_task_id: \"{bravoTask.Id}\"", File.ReadAllText(retiredBravo), StringComparison.Ordinal);
        var catalog = File.ReadAllText(Path.Combine(repository.Path, "docs/cis/catalog.yml"));
        Assert.Contains($"agent-tasks/retired/{Path.GetFileName(bravoTask.TaskPath!)}", catalog, StringComparison.Ordinal);
        Assert.Contains("status: archived", catalog, StringComparison.Ordinal);
        Assert.Contains("plan-task-retired", File.ReadAllText(services.Changes.DossierFile(change, "events.jsonl")), StringComparison.Ordinal);

        // The previously highest identifier is now retired. A fresh service must
        // still reserve both retired IDs rather than reassigning their history.
        var delta = AdHocFinding("IMPACT-DELTA", "orders-api-0-delta");
        WriteAdHocImpact(services, change, delta, alpha);
        var replaced = CreateServices().Plans.Build(repository.Path, change.Id);
        AssertAdHocSuccess(replaced);
        var deltaTask = AdHocTaskFor(replaced, delta.Id);
        Assert.DoesNotContain(deltaTask.Id, added.WorkItems.Select(item => item.Id));
        var deltaDocument = File.ReadAllText(AdHocTaskPath(services, change, deltaTask));
        Assert.DoesNotContain("alpha-owned", deltaDocument, StringComparison.Ordinal);
        Assert.DoesNotContain("bravo-owned", deltaDocument, StringComparison.Ordinal);
        Assert.DoesNotContain($"work_owner_task_id: \"{bravoTask.Id}\"", deltaDocument, StringComparison.Ordinal);

        WriteAdHocImpact(services, change, delta, bravo, alpha);
        var reappeared = CreateServices().Plans.Build(repository.Path, change.Id);
        AssertAdHocSuccess(reappeared);
        var restoredBravo = AdHocTaskFor(reappeared, bravo.Id);
        Assert.Equal(bravoTask.Id, restoredBravo.Id);
        Assert.Equal(bravoTask.TaskPath, restoredBravo.TaskPath);
        Assert.Equal("Draft", restoredBravo.Status);
        Assert.False(File.Exists(retiredBravo));
        Assert.Contains("bravo-owned", File.ReadAllText(bravoPath), StringComparison.Ordinal);
        Assert.Contains($"work_owner_task_id: \"{bravoTask.Id}\"", File.ReadAllText(bravoPath), StringComparison.Ordinal);
        Assert.Contains("plan-task-reactivated", File.ReadAllText(services.Changes.DossierFile(change, "events.jsonl")), StringComparison.Ordinal);
        Assert.Equal(alphaTask.Id, AdHocTaskFor(reappeared, alpha.Id).Id);
        Assert.Equal(alphaBytes, File.ReadAllBytes(alphaPath));
    }

    [Fact]
    public void AdHocPlanBuild_NewPrerequisiteHoldsBeforeChangingRetainedContractOrPlan()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var verification = AdHocFinding("IMPACT-VERIFY", "orders-api-verification");
        var change = CreateAdHocChange(repository, services, verification);
        var first = services.Plans.Build(repository.Path, change.Id);
        AssertAdHocSuccess(first);
        var task = Assert.Single(first.WorkItems);
        var taskPath = AdHocTaskPath(services, change, task);
        WriteAdHocAuthoredContract(taskPath, task.Id, "retained-verification");
        var prerequisite = AdHocFinding("IMPACT-CODE", "orders-api-code") with { Category = "implementation" };
        WriteAdHocImpact(services, change, prerequisite, verification);
        var before = AdHocSnapshot([taskPath, services.Changes.DossierFile(change, "plan.md"),
            Path.Combine(repository.Path, "docs/cis/catalog.yml"), services.Changes.DossierFile(change, "events.jsonl")]);

        var rebuilt = CreateServices().Plans.Build(repository.Path, change.Id);

        Assert.Equal("reconciliation-required", rebuilt.Status);
        Assert.NotEqual(0, rebuilt.ExitCode);
        Assert.False(rebuilt.Applied);
        Assert.Contains(rebuilt.Errors, error => error.Contains("prerequisite", StringComparison.Ordinal));
        Assert.Equal(task.Id, Assert.Single(rebuilt.WorkItems).Id);
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(taskPath)!, "*.md"));
        AssertAdHocSnapshot(before);
    }

    [Fact]
    public void AdHocPlanBuild_UnchangedRebuildPreservesWholeAuthoredContractAndCatalogBytes()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var finding = AdHocFinding("IMPACT-ALPHA", "orders-api-alpha");
        var change = CreateAdHocChange(repository, services, finding);
        var first = services.Plans.Build(repository.Path, change.Id);
        AssertAdHocSuccess(first);
        var task = Assert.Single(first.WorkItems);
        var taskPath = AdHocTaskPath(services, change, task);
        WriteAdHocAuthoredContract(taskPath, task.Id, "authored-contract");
        var authoredBytes = File.ReadAllBytes(taskPath);
        // An authored metadata change may legitimately be reflected in the plan
        // once. It must never replace any authored contract text in the task.
        AssertAdHocSuccess(CreateServices().Plans.Build(repository.Path, change.Id));
        Assert.Equal(authoredBytes, File.ReadAllBytes(taskPath));
        var stablePaths = new[]
        {
            taskPath, services.Changes.DossierFile(change, "plan.md"),
            Path.Combine(repository.Path, "docs/cis/catalog.yml"),
            services.Changes.DossierFile(change, "events.jsonl"),
        };
        foreach (var path in stablePaths)
            File.SetLastWriteTimeUtc(path, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        var bytes = AdHocSnapshot(stablePaths);
        var timestamps = stablePaths.ToDictionary(path => path, File.GetLastWriteTimeUtc, StringComparer.Ordinal);

        var unchanged = CreateServices().Plans.Build(repository.Path, change.Id);

        AssertAdHocSuccess(unchanged);
        Assert.Equal("unchanged", unchanged.Status);
        Assert.False(unchanged.Applied);
        Assert.Equal(task.Id, Assert.Single(unchanged.WorkItems).Id);
        AssertAdHocSnapshot(bytes);
        foreach (var path in stablePaths) Assert.Equal(timestamps[path], File.GetLastWriteTimeUtc(path));
        foreach (var section in new[] { "Objective", "Required changes", "Constraints and exclusions", "Acceptance criteria", "Targeted validation", "Completion evidence" })
            Assert.Contains($"authored-contract: {section}", File.ReadAllText(taskPath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("identity")]
    [InlineData("identity-prefix")]
    [InlineData("type-prefix")]
    [InlineData("version-prefix")]
    [InlineData("contract")]
    [InlineData("path")]
    public void AdHocPlanBuild_InvalidDurableTaskBlocksRebuildValidationAndApproval(string defect)
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = CreateAdHocChange(repository, services, AdHocFinding("IMPACT-ALPHA", "orders-api-alpha"));
        var first = services.Plans.Build(repository.Path, change.Id);
        AssertAdHocSuccess(first);
        var task = Assert.Single(first.WorkItems);
        var taskPath = AdHocTaskPath(services, change, task);
        var planPath = services.Changes.DossierFile(change, "plan.md");
        if (defect == "missing") File.Delete(taskPath);
        else if (defect == "path")
            File.WriteAllText(planPath, File.ReadAllText(planPath).Replace(task.TaskPath!, "../outside-task.md", StringComparison.Ordinal));
        else
        {
            var original = File.ReadAllText(taskPath);
            var invalid = defect switch
            {
                "identity" => original.Replace($"task_id: {task.Id}", "task_id: WORK-UNRELATED", StringComparison.Ordinal),
                "identity-prefix" => original.Replace($"task_id: {task.Id}", $"task_id: {task.Id}-UNRELATED", StringComparison.Ordinal),
                "type-prefix" => original.Replace($"task_type: {task.TaskTypeKey}", $"task_type: {task.TaskTypeKey}-unrelated", StringComparison.Ordinal),
                "version-prefix" => original.Replace($"task_type_version: {task.TaskTypeVersion}", $"task_type_version: {task.TaskTypeVersion}-unrelated", StringComparison.Ordinal),
                _ => original.Replace("## Required changes", "## Removed contract section", StringComparison.Ordinal),
            };
            Assert.NotEqual(original, invalid);
            File.WriteAllText(taskPath, invalid);
        }
        var stablePaths = new List<string> { planPath, Path.Combine(repository.Path, "docs/cis/catalog.yml"), services.Changes.DossierFile(change, "events.jsonl") };
        if (File.Exists(taskPath)) stablePaths.Add(taskPath);
        var before = AdHocSnapshot(stablePaths);

        Assert.NotEqual(0, CreateServices().Plans.Validate(repository.Path, change.Id).ExitCode);
        Assert.NotEqual(0, CreateServices().Plans.Approve(repository.Path, change.Id,
            "Synthetic reviewer", "An invalid durable contract must remain unapproved.").ExitCode);
        var rebuilt = CreateServices().Plans.Build(repository.Path, change.Id);
        Assert.NotEqual(0, rebuilt.ExitCode);
        Assert.False(rebuilt.Applied);
        AssertAdHocSnapshot(before);
        if (defect == "missing") Assert.False(File.Exists(taskPath));
    }

    [Theory]
    [InlineData("label")]
    [InlineData("target")]
    [InlineData("category")]
    [InlineData("evidence")]
    public void AdHocPlanBuild_ChangedBasisRequiresReconciliationWithoutOverwritingAuthoredWork(string changedField)
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var finding = AdHocFinding("IMPACT-ALPHA", "orders-api-alpha");
        var change = CreateAdHocChange(repository, services, finding);
        var first = services.Plans.Build(repository.Path, change.Id);
        AssertAdHocSuccess(first);
        var task = Assert.Single(first.WorkItems);
        var taskPath = AdHocTaskPath(services, change, task);
        WriteAdHocAuthoredContract(taskPath, task.Id, "retained-scope");
        var changed = changedField switch
        {
            "label" => finding with { Label = "Verify a different behavior" },
            "target" => finding with { Target = "orders-api-replacement" },
            "category" => finding with { Category = "contract" },
            _ => finding with { Evidence = "A materially different source contract" },
        };
        WriteAdHocImpact(services, change, changed);
        var before = AdHocSnapshot([taskPath, services.Changes.DossierFile(change, "plan.md"),
            Path.Combine(repository.Path, "docs/cis/catalog.yml"), services.Changes.DossierFile(change, "events.jsonl")]);

        Assert.NotEqual(0, CreateServices().Plans.Validate(repository.Path, change.Id).ExitCode);
        Assert.NotEqual(0, CreateServices().Plans.Approve(repository.Path, change.Id,
            "Synthetic reviewer", "Changed impact scope cannot approve the retained task contract.").ExitCode);
        var result = CreateServices().Plans.Build(repository.Path, change.Id);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("reconciliation-required", result.Status);
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, error => error.Contains(finding.Id, StringComparison.Ordinal));
        AssertAdHocSnapshot(before);
    }

    [Fact]
    public void AdHocPlanBuild_RetainedCatalogIdentityCannotBeRecycledWhenPlanAndDocumentAreMissing()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var original = AdHocFinding("IMPACT-ALPHA", "orders-api-alpha");
        var change = CreateAdHocChange(repository, services, original);
        var built = services.Plans.Build(repository.Path, change.Id);
        AssertAdHocSuccess(built);
        var oldTask = Assert.Single(built.WorkItems);
        var taskPath = AdHocTaskPath(services, change, oldTask);
        var taskDirectory = Path.GetDirectoryName(taskPath)!;
        var planPath = services.Changes.DossierFile(change, "plan.md");
        File.Delete(taskPath);
        File.Delete(planPath);
        WriteAdHocImpact(services, change, AdHocFinding("IMPACT-REPLACEMENT", "orders-api-0-replacement"));
        var before = AdHocSnapshot([Path.Combine(repository.Path, "docs/cis/catalog.yml"),
            services.Changes.DossierFile(change, "events.jsonl")]);

        var result = CreateServices().Plans.Build(repository.Path, change.Id);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(result.Applied);
        Assert.False(File.Exists(planPath));
        Assert.False(File.Exists(taskPath));
        Assert.Empty(Directory.EnumerateFiles(taskDirectory, "*.md", SearchOption.AllDirectories));
        AssertAdHocSnapshot(before);
    }

    [Fact]
    public void AdHocPlanBuild_PartiallyCorruptRetiredBindingCannotCreateANewOwner()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var alpha = AdHocFinding("IMPACT-ALPHA", "orders-api-alpha");
        var bravo = AdHocFinding("IMPACT-BRAVO", "orders-api-bravo");
        var change = CreateAdHocChange(repository, services, alpha, bravo);
        var first = services.Plans.Build(repository.Path, change.Id);
        AssertAdHocSuccess(first);
        var alphaTask = AdHocTaskFor(first, alpha.Id);
        var bravoTask = AdHocTaskFor(first, bravo.Id);
        WriteAdHocImpact(services, change, alpha);
        AssertAdHocSuccess(CreateServices().Plans.Build(repository.Path, change.Id));
        var retiredPath = AdHocRetiredTaskPath(services, change, bravoTask);
        var retained = File.ReadAllText(retiredPath);
        var incomplete = Regex.Replace(retained, @"(?m)^ad_hoc_impact_id:[^\r\n]*(?:\r?\n|$)", "",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        Assert.NotEqual(retained, incomplete);
        Assert.Contains("ad_hoc_impact_basis:", incomplete, StringComparison.Ordinal);
        Assert.Contains("ad-hoc-impact:", incomplete, StringComparison.Ordinal);
        File.WriteAllText(retiredPath, incomplete);
        WriteAdHocImpact(services, change, alpha, bravo);
        var before = AdHocSnapshot([AdHocTaskPath(services, change, alphaTask), retiredPath,
            services.Changes.DossierFile(change, "plan.md"), Path.Combine(repository.Path, "docs/cis/catalog.yml"),
            services.Changes.DossierFile(change, "events.jsonl")]);

        var result = CreateServices().Plans.Build(repository.Path, change.Id);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(result.Applied);
        Assert.False(File.Exists(AdHocTaskPath(services, change, bravoTask)));
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(retiredPath)!, "*.md"));
        AssertAdHocSnapshot(before);
    }

    [Fact]
    public void AdHocPlanBuild_EmptyRetirementSymlinkCannotMoveContractsOutsideAuthority()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "This case exercises Unix directory symlinks; the Windows privilege-dependent link lane is not qualified here.");
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var alpha = AdHocFinding("IMPACT-ALPHA", "orders-api-alpha");
        var bravo = AdHocFinding("IMPACT-BRAVO", "orders-api-bravo");
        var change = CreateAdHocChange(repository, services, alpha, bravo);
        var first = services.Plans.Build(repository.Path, change.Id);
        AssertAdHocSuccess(first);
        var alphaPath = AdHocTaskPath(services, change, AdHocTaskFor(first, alpha.Id));
        var bravoPath = AdHocTaskPath(services, change, AdHocTaskFor(first, bravo.Id));
        var retiredRoot = Path.Combine(Path.GetDirectoryName(alphaPath)!, "retired");
        var outside = Directory.CreateTempSubdirectory("cis-ad-hoc-retirement-");
        try
        {
            Assert.False(Directory.Exists(retiredRoot));
            Assert.Empty(outside.EnumerateFileSystemInfos());
            Directory.CreateSymbolicLink(retiredRoot, outside.FullName);
            WriteAdHocImpact(services, change, alpha);
            var before = AdHocSnapshot([alphaPath, bravoPath, services.Changes.DossierFile(change, "plan.md"),
                Path.Combine(repository.Path, "docs/cis/catalog.yml"), services.Changes.DossierFile(change, "events.jsonl")]);

            var result = CreateServices().Plans.Build(repository.Path, change.Id);

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(result.Applied);
            Assert.True(File.Exists(bravoPath));
            Assert.Empty(outside.EnumerateFileSystemInfos());
            AssertAdHocSnapshot(before);
        }
        finally
        {
            // Remove the link itself before disposing the owned repository, then
            // delete only the exact fresh scratch directory created by this case.
            if (Directory.Exists(retiredRoot)) Directory.Delete(retiredRoot);
            outside.Delete(recursive: true);
        }
    }

    [Fact]
    public void AdHocPlanBuild_LegacyUndocumentedRowHoldsWithoutStampingAnUnprovedInputBasis()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var alpha = AdHocFinding("IMPACT-ALPHA", "orders-api-alpha");
        var change = CreateAdHocChange(repository, services, alpha);
        // Frozen pre-correction shape: one accepted impact, an established identity,
        // no task path, no imported feature source or materialized-task metadata.
        File.WriteAllText(services.Changes.DossierFile(change, "plan.md"), $$"""
            ---
            title: Synthetic ad hoc plan
            type: delivery-plan
            status: Draft
            change_id: {{change.Id}}
            graph_build_id: {{JsonSerializer.Serialize(change.GraphBuildId)}}
            ---

            # Delivery plan

            <!-- cis:execution-manifest
            | ID | Category | Complexity | Parent | Task document | Title | Requirement IDs | Impact IDs | Depends on | Acceptance criteria | Validation | Status | Task type | Type version | Approval gate | Targets | Frontend type |
            | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
            | WORK-017 | verification | medium | | | Address verification impact: Verify orders-api-alpha behavior | | IMPACT-ALPHA | | The retained alpha behavior is verified. | Run the identified native tests. | not-started | core.legacy | 1.0 | none | | not-applicable |
            cis:execution-manifest -->
            """);
        var earlier = AdHocFinding("IMPACT-EARLIER", "orders-api-0-earlier");
        WriteAdHocImpact(services, change, earlier, alpha);
        var before = AdHocSnapshot([services.Changes.DossierFile(change, "plan.md"),
            Path.Combine(repository.Path, "docs/cis/catalog.yml"), services.Changes.DossierFile(change, "events.jsonl")]);

        var upgraded = CreateServices().Plans.Build(repository.Path, change.Id);

        Assert.Equal("reconciliation-required", upgraded.Status);
        Assert.NotEqual(0, upgraded.ExitCode);
        Assert.False(upgraded.Applied);
        var retained = Assert.Single(upgraded.WorkItems);
        Assert.Equal("WORK-017", retained.Id);
        Assert.Equal([alpha.Id], retained.ImpactIds);
        Assert.Null(retained.TaskPath);
        Assert.Equal("The retained alpha behavior is verified.", retained.AcceptanceCriteria);
        Assert.Equal("Run the identified native tests.", retained.Validation);
        AssertAdHocSnapshot(before);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!, "agent-tasks")));
        Assert.Null(upgraded.Source);
    }

    [Fact]
    public void AdHocPlanBuild_ApprovedPlanCannotBeRebuiltOrReassigned()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var alpha = AdHocFinding("IMPACT-ALPHA", "orders-api-alpha");
        var change = CreateAdHocChange(repository, services, alpha);
        var first = services.Plans.Build(repository.Path, change.Id);
        AssertAdHocSuccess(first);
        AssertAdHocSuccess(services.Plans.Approve(repository.Path, change.Id,
            "Synthetic reviewer", "Approve the exact synthetic existing task."));
        var taskPath = AdHocTaskPath(services, change, Assert.Single(first.WorkItems));
        var before = AdHocSnapshot([taskPath, services.Changes.DossierFile(change, "plan.md"),
            Path.Combine(repository.Path, "docs/cis/catalog.yml"), services.Changes.DossierFile(change, "events.jsonl")]);
        WriteAdHocImpact(services, change, alpha, AdHocFinding("IMPACT-NEW", "orders-api-0-new"));

        var rebuilt = CreateServices().Plans.Build(repository.Path, change.Id);

        Assert.NotEqual(0, rebuilt.ExitCode);
        Assert.Contains(rebuilt.Errors, error => error.Contains("approved plan", StringComparison.OrdinalIgnoreCase));
        Assert.False(rebuilt.Applied);
        AssertAdHocSnapshot(before);
    }

    private static ChangeDossier CreateAdHocChange(TemporaryRepository repository, Services services, params ImpactFinding[] findings)
    {
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "Bounded existing verification", "Verify only the accepted existing behavior.",
            [new ChangeRoot("orders-api", "component")])).Change);
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        WriteAdHocImpact(services, change, findings);
        return change;
    }

    private static ImpactFinding AdHocFinding(string id, string target)
        => new(id, "verification", target, $"Verify {target} behavior", "accepted", "high",
            $"Existing deterministic source for {target}", "Bounded existing behavior needs verification.",
            "Synthetic reviewer accepts this exact fixture scope.");

    private static void WriteAdHocImpact(Services services, ChangeDossier change, params ImpactFinding[] findings)
    {
        var text = new StringBuilder($"---\ntitle: Synthetic accepted impact\ntype: impact-analysis\nstatus: Reviewed\nchange_id: {change.Id}\ngraph_build_id: {JsonSerializer.Serialize(change.GraphBuildId)}\nanalysis_truncated: false\n---\n\n## Findings\n\n");
        text.AppendLine("| ID | Category | Target | Label | State | Confidence | Evidence | Rationale | Review reason |");
        text.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var finding in findings)
            text.AppendLine($"| {finding.Id} | {finding.Category} | {finding.Target} | {finding.Label} | {finding.State} | {finding.Confidence} | {finding.Evidence} | {finding.Rationale} | {finding.ReviewReason} |");
        File.WriteAllText(services.Changes.DossierFile(change, "impact.md"), text.ToString());
    }

    private static PlanWorkItem AdHocTaskFor(PlanResult result, string impactId)
        => Assert.Single(result.WorkItems, item => item.ImpactIds.Contains(impactId, StringComparer.Ordinal));

    private static string AdHocTaskPath(Services services, ChangeDossier change, PlanWorkItem item)
        => Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
            Assert.IsType<string>(item.TaskPath).Replace('/', Path.DirectorySeparatorChar));

    private static string AdHocRetiredTaskPath(Services services, ChangeDossier change, PlanWorkItem item)
        => Path.Combine(Path.GetDirectoryName(services.Changes.DossierFile(change, "plan.md"))!,
            "agent-tasks", "retired", Path.GetFileName(Assert.IsType<string>(item.TaskPath)));

    private static void WriteAdHocAuthoredContract(string path, string taskId, string marker)
    {
        var content = File.ReadAllText(path);
        foreach (var section in new[] { "Objective", "Required changes", "Required outputs", "Constraints and exclusions", "Acceptance criteria", "Targeted validation", "Completion evidence" })
        {
            var heading = "## " + section;
            Assert.Contains(heading, content, StringComparison.Ordinal);
            content = content.Replace(heading, heading + $"\n\n{marker}: {section}; preserve this authored contract and original accounting association.", StringComparison.Ordinal);
        }
        var owner = $"work_owner_task_id: \"{taskId}\"";
        content = Regex.IsMatch(content, "(?m)^work_owner_task_id:")
            ? Regex.Replace(content, "(?m)^work_owner_task_id:[^\\r\\n]*", owner)
            : content.Replace($"task_id: {taskId}", $"task_id: {taskId}\n{owner}", StringComparison.Ordinal);
        File.WriteAllText(path, content);
    }

    private static Dictionary<string, byte[]> AdHocSnapshot(IEnumerable<string> paths)
        => paths.ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertAdHocSnapshot(IReadOnlyDictionary<string, byte[]> expected)
    {
        foreach (var (path, bytes) in expected) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    private static void AssertAdHocSuccess(PlanResult result)
        => Assert.True(result.ExitCode == 0,
            string.Join(Environment.NewLine, result.Errors.Concat(result.Validation?.Errors ?? [])));
}
