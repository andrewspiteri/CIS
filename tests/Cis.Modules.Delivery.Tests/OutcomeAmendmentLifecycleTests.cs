using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Plan;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Fact]
    public void OutcomeAmendment_CompletedUiRetainsApplicableHistoricalGrantAfterNoUiAmendment()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new(repository.Path,
            "Historical display work", "Review the accepted bounded display contract.", [])).Change);
        var request = OutcomeRequest(repository.Path) with
        {
            Outcome = change.Outcome, VisualUi = true,
            Scope = [new("display", "Review the accepted bounded display behavior.", ["src"])]
        };
        var authorized = services.Changes.Authorize(new(repository.Path, change.Id, request,
            "Original human", "Authorize the display work and its actual visual applicability."));
        Assert.Equal(0, authorized.ExitCode);
        var authority = Assert.IsType<CisOutcomeAuthorization>(authorized.Authorization);
        AssertApprovedOutcomePlan(services.Plans.Build(repository.Path, change.Id));
        var added = services.Plans.OrganizeOutcomeTasks(new(repository.Path, change.Id,
            [new("Original completed display", ["display"], "The original accepted display contract is reviewed.", "Review the bounded display fixture.")
                { Paths = ["src/original-display.cs"], Category = "frontend" },
             new("Original unfinished display", ["display"], "The remaining accepted display contract is bounded.", "Review the remaining display fixture.")
                { Paths = ["src/unfinished-display.cs"], Category = "frontend" }],
            "Controller", "Organize the accepted visual work under the existing grant."));
        AssertApprovedOutcomePlan(added);
        var task = Assert.Single(added.WorkItems, item => item.Title == "Original completed display");
        var unfinished = Assert.Single(added.WorkItems, item => item.Title == "Original unfinished display");
        // This service fixture models the original actual UI gate; rendering belongs
        // to the existing design lifecycle tests, not this amendment regression.
        var designPath = services.Changes.DossierFile(change, "design.md");
        File.WriteAllText(designPath, "---\napproval_status: Approved\ngate_status: Approved\n---\n# Reviewed display fixture\n");
        AssertApprovedOutcomePlan(services.Plans.TransitionTask(new(repository.Path, change.Id, task.Id,
            "InProgress", "Controller", "Begin the original display after its applicable UI gate.")));
        AssertApprovedOutcomePlan(services.Plans.TransitionTask(new(repository.Path, change.Id, unfinished.Id,
            "InProgress", "Controller", "Begin the remaining display under the original visual authority.")));
        var taskPath = Path.Combine(repository.Path, change.RelativePath, task.TaskPath!);
        File.WriteAllText(taskPath, File.ReadAllText(taskPath).Replace("- [ ]", "- [x]", StringComparison.Ordinal)
            + "\n| Original display review | Reviewed | Original bounded display fixture |\n");
        var finished = services.Plans.TransitionTask(new(repository.Path, change.Id, task.Id,
            "Complete", "Controller", "Complete only the originally authorized display review fixture."));
        AssertApprovedOutcomePlan(finished);
        var completed = Assert.Single(finished.WorkItems, item => item.Id == task.Id);
        var completedContract = JsonSerializer.Serialize(completed);
        var completedBytes = File.ReadAllBytes(taskPath);
        var designBytes = File.ReadAllBytes(designPath);

        var amended = services.Changes.Authorize(new(repository.Path, change.Id, request with
        {
            VisualUi = false, DecisionReference = "fixture:future-no-ui-amendment"
        }, "Original human", "Retain completed display history while excluding further visual work."));
        Assert.Equal(0, amended.ExitCode);
        Assert.Equal(authority.Id, amended.Authorization!.Id);
        Assert.Equal(task.AuthorizationScopeBasis, CisOutcomeAuthority.ScopeBasis(amended.Authorization, task.AuthorizationScopeIds));
        Assert.True(Assert.Single(amended.Authorization.History).Request.VisualUi);
        Assert.False(amended.Authorization.Request.VisualUi);
        Assert.Throws<InvalidDataException>(() => services.Plans.ReadTaskDigest(repository.Path, change.Id, unfinished.Id));
        var held = services.Plans.Validate(repository.Path, change.Id);
        Assert.NotEqual(0, held.ExitCode);
        Assert.Contains(held.Validation!.Errors, error => error.Contains(unfinished.Id, StringComparison.Ordinal));
        Assert.DoesNotContain(held.Validation.Errors, error => error.Contains(task.Id, StringComparison.Ordinal));
        AssertApprovedOutcomePlan(services.Plans.TransitionTask(new(repository.Path, change.Id, unfinished.Id,
            "Cancelled", "Controller", "Preserve unfinished visual work while ending execution excluded by the current no-UI choice.")));
        AssertApprovedOutcomePlan(services.Plans.Validate(repository.Path, change.Id));
        AssertApprovedOutcomePlan(CreateServices().Plans.Build(repository.Path, change.Id));
        var retained = Assert.Single(services.Plans.Show(repository.Path, change.Id).WorkItems, item => item.Id == task.Id);
        Assert.Equal("Complete", retained.Status);
        Assert.Equal(completedContract, JsonSerializer.Serialize(retained));
        Assert.Equal(completedBytes, File.ReadAllBytes(taskPath));
        Assert.Equal(designBytes, File.ReadAllBytes(designPath));

        var dossier = Path.Combine(repository.Path, change.RelativePath);
        var before = SnapshotOutcomeDossier(dossier);
        var newUi = services.Plans.OrganizeOutcomeTasks(new(repository.Path, change.Id,
            [new("New display work", ["display"], "Introduce another display.", "Review another display.")
                { Paths = ["src/new-display.cs"], Category = "frontend" }],
            "Controller", "The historical visual grant cannot authorize further UI under current no-UI scope."));
        Assert.NotEqual(0, newUi.ExitCode);
        Assert.False(newUi.Applied);
        Assert.Contains(newUi.Errors, error => error.Contains("no-UI", StringComparison.OrdinalIgnoreCase));
        AssertOutcomeDossierUnchanged(before, dossier);
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Deferred")]
    public void OutcomeAmendment_CompletedHistoryAndUnfinishedDispositionPermitCurrentReplacement(string disposition)
    {
        using var repository = TemporaryRepository.Create();
        var (services, change, request, original) = PrepareOutcomeAmendmentLifecycle(repository);
        var completed = Assert.Single(original.WorkItems, item => item.Title == "Original completed work");
        var started = Assert.Single(original.WorkItems, item => item.Title == "Original unfinished work");
        var dossier = Path.Combine(repository.Path, change.RelativePath);
        var completedPath = Path.Combine(dossier, completed.TaskPath!);
        var completedBytes = File.ReadAllBytes(completedPath);
        var completedContract = JsonSerializer.Serialize(completed);
        var resultPath = Path.Combine(dossier, "original-reviewed-result.txt");
        var resultBytes = File.ReadAllBytes(resultPath);
        var amended = services.Changes.Authorize(new(repository.Path, change.Id, request with
        {
            Scope = request.Scope.Select(scope => scope.Id == "implementation"
                ? scope with { Description = "Implement the specifically revised accepted behavior." } : scope).ToArray(),
            DecisionReference = "fixture:implementation-amendment"
        }, "Original human", "Authorize only the implementation scope difference."));
        Assert.Equal(0, amended.ExitCode);
        var authorizationPath = services.Changes.DossierFile(change, "authorization.json");
        var amendedBytes = File.ReadAllBytes(authorizationPath);
        var held = services.Plans.Validate(repository.Path, change.Id);
        Assert.NotEqual(0, held.ExitCode);
        Assert.Contains(held.Validation!.Errors, error => error.Contains(started.Id, StringComparison.Ordinal));
        Assert.DoesNotContain(held.Validation.Errors, error => error.Contains(completed.Id, StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => services.Plans.ReadTaskDigest(repository.Path, change.Id, completed.Id));
        var beforeComplete = SnapshotOutcomeDossier(dossier);
        var prematureCompletion = services.Plans.TransitionTask(new(repository.Path, change.Id, started.Id,
            "Complete", "Controller", "An old unfinished contract cannot complete against revised scope."));
        Assert.NotEqual(0, prematureCompletion.ExitCode);
        Assert.False(prematureCompletion.Applied);
        AssertOutcomeDossierUnchanged(beforeComplete, dossier);

        var disposed = services.Plans.TransitionTask(new(repository.Path, change.Id, started.Id, disposition,
            "Controller", "Retain original work and evidence while ending execution under its superseded scope."));
        AssertApprovedOutcomePlan(disposed);
        var retained = Assert.Single(disposed.WorkItems, item => item.Id == started.Id);
        Assert.Equal(disposition, retained.Status);
        Assert.Equal(started.InstanceKey, retained.InstanceKey);
        Assert.Equal(started.WorkOwnerTaskId, retained.WorkOwnerTaskId);
        Assert.Equal(started.AuthorizationScopeBasis, retained.AuthorizationScopeBasis);
        Assert.Equal(started.AcceptanceCriteria, retained.AcceptanceCriteria);
        Assert.Equal(started.Validation, retained.Validation);
        if (disposition == "Deferred")
        {
            var beforeResume = SnapshotOutcomeDossier(dossier);
            var staleResume = services.Plans.TransitionTask(new(repository.Path, change.Id, started.Id,
                "InProgress", "Controller", "Deferral must not grant execution against the changed scope."));
            Assert.NotEqual(0, staleResume.ExitCode);
            Assert.False(staleResume.Applied);
            AssertOutcomeDossierUnchanged(beforeResume, dossier);
        }
        var replacement = services.Plans.OrganizeOutcomeTasks(new(repository.Path, change.Id,
            [new("Current replacement work", ["implementation"], "Deliver the revised accepted behavior.", "Review the current replacement contract.")
                { Paths = ["src/current-replacement.cs"] }],
            "Controller", "Organize replacement work within the amended scope and unchanged shared overall limits."));
        AssertApprovedOutcomePlan(replacement);
        var current = Assert.Single(replacement.WorkItems, item => item.Title == "Current replacement work");
        Assert.NotEqual(started.Id, current.Id);
        Assert.NotEqual(started.AuthorizationScopeBasis, current.AuthorizationScopeBasis);
        AssertApprovedOutcomePlan(services.Plans.TransitionTask(new(repository.Path, change.Id, current.Id,
            "InProgress", "Controller", "Execute only the replacement bound to current scope.")));
        AssertApprovedOutcomePlan(services.Plans.Validate(repository.Path, change.Id));
        AssertApprovedOutcomePlan(CreateServices().Plans.Build(repository.Path, change.Id));
        var historical = Assert.Single(services.Plans.Show(repository.Path, change.Id).WorkItems, item => item.Id == completed.Id);
        Assert.Equal(completedContract, JsonSerializer.Serialize(historical));
        Assert.Equal(completedBytes, File.ReadAllBytes(completedPath));
        Assert.Equal(resultBytes, File.ReadAllBytes(resultPath));
        Assert.Equal(amendedBytes, File.ReadAllBytes(authorizationPath));
        var completedUpdate = services.Plans.OrganizeOutcomeTasks(new(repository.Path, change.Id,
            [new("Replace completed history", ["implementation"], "A different acceptance.", "A different validation.")],
            "Controller", "Completed original contracts remain immutable.", completed.Id), "update");
        Assert.NotEqual(0, completedUpdate.ExitCode);
        Assert.Equal(completedBytes, File.ReadAllBytes(completedPath));
        var events = OutcomeEventTypes(services.Changes.DossierFile(change, "events.jsonl"));
        Assert.Equal(1, events.Count(type => type == "outcome-authorized"));
        Assert.Equal(1, events.Count(type => type == "outcome-authorization-amended"));
        Assert.DoesNotContain("plan-approved", events);
    }

    [Fact]
    public void OutcomeAmendment_RemovedScopeReconcilesBySplittingUnderCurrentGrantAndRetainingOwner()
    {
        using var repository = TemporaryRepository.Create();
        var (services, change, request, original) = PrepareOutcomeAmendmentLifecycle(repository);
        var completed = Assert.Single(original.WorkItems, item => item.Title == "Original completed work");
        var started = Assert.Single(original.WorkItems, item => item.Title == "Original unfinished work");
        var dossier = Path.Combine(repository.Path, change.RelativePath);
        var completedBytes = File.ReadAllBytes(Path.Combine(dossier, completed.TaskPath!));
        var resultPath = Path.Combine(dossier, "original-reviewed-result.txt");
        var resultBytes = File.ReadAllBytes(resultPath);
        var amended = services.Changes.Authorize(new(repository.Path, change.Id, request with
        {
            Scope = [request.Scope.Single(scope => scope.Id == "documentation"),
                new("implementation-v2", "Implement the revised accepted behavior in its revised path.", ["src/revised"])],
            DecisionReference = "fixture:replaced-implementation-scope"
        }, "Original human", "Replace only the specific accepted implementation scope."));
        Assert.Equal(0, amended.ExitCode);
        var authority = Assert.IsType<CisOutcomeAuthorization>(amended.Authorization);
        var authorizationPath = services.Changes.DossierFile(change, "authorization.json");
        var authorizationBytes = File.ReadAllBytes(authorizationPath);
        Assert.Throws<InvalidDataException>(() => services.Plans.ReadTaskDigest(repository.Path, change.Id, started.Id));

        var split = services.Plans.OrganizeOutcomeTasks(new(repository.Path, change.Id,
            [new("Current input replacement", ["implementation-v2"], "Input follows the revised accepted behavior.", "Review the revised input contract.")
                { Paths = ["src/revised/input.cs"] },
             new("Current result replacement", ["implementation-v2"], "Results follow the revised accepted behavior.", "Review the revised result contract.")
                { Paths = ["src/revised/result.cs"] }],
            "Controller", "Reconcile unfinished work into current-grant children without replacing its original accounting owner.", started.Id), "split");
        AssertApprovedOutcomePlan(split);
        var parent = Assert.Single(split.WorkItems, item => item.Id == started.Id);
        Assert.Equal("Decomposed", parent.Status);
        Assert.Equal(started.InstanceKey, parent.InstanceKey);
        Assert.Equal(started.WorkOwnerTaskId, parent.WorkOwnerTaskId);
        Assert.Equal(started.AuthorizationScopeIds, parent.AuthorizationScopeIds);
        Assert.Equal(started.AuthorizationScopeBasis, parent.AuthorizationScopeBasis);
        Assert.Equal(started.AcceptanceCriteria, parent.AcceptanceCriteria);
        Assert.Equal(started.Validation, parent.Validation);
        var children = split.WorkItems.Where(item => item.ParentId == started.Id).ToArray();
        Assert.Equal(2, children.Length);
        Assert.All(children, child =>
        {
            Assert.Equal(started.WorkOwnerTaskId ?? started.Id, child.WorkOwnerTaskId);
            Assert.Equal(new[] { "implementation-v2" }, child.AuthorizationScopeIds);
            Assert.Equal(CisOutcomeAuthority.ScopeBasis(authority, ["implementation-v2"]), child.AuthorizationScopeBasis);
            Assert.False(string.IsNullOrWhiteSpace(services.Plans.ReadTaskDigest(repository.Path, change.Id, child.Id)));
        });
        AssertApprovedOutcomePlan(services.Plans.TransitionTask(new(repository.Path, change.Id, children[0].Id,
            "InProgress", "Controller", "Begin only the current child contract under the retained original owner.")));
        AssertApprovedOutcomePlan(services.Plans.Validate(repository.Path, change.Id));
        Assert.Equal(completedBytes, File.ReadAllBytes(Path.Combine(dossier, completed.TaskPath!)));
        Assert.Equal(resultBytes, File.ReadAllBytes(resultPath));
        Assert.Equal(authorizationBytes, File.ReadAllBytes(authorizationPath));

        // A terminal status cannot invent a historical approval for an unknown basis.
        var path = services.Changes.DossierFile(change, "plan.json");
        var state = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        var row = state["workItems"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == completed.Id)!;
        var unknownBasis = "sha256:" + new string('0', 64);
        row["authorizationScopeBasis"] = unknownBasis;
        File.WriteAllText(path, state.ToJsonString());
        var completedPath = Path.Combine(dossier, completed.TaskPath!);
        File.WriteAllText(completedPath, File.ReadAllText(completedPath).Replace(
            "authorization_scope_basis: " + JsonSerializer.Serialize(completed.AuthorizationScopeBasis),
            "authorization_scope_basis: " + JsonSerializer.Serialize(unknownBasis), StringComparison.Ordinal));
        var unknown = services.Plans.Validate(repository.Path, change.Id);
        Assert.NotEqual(0, unknown.ExitCode);
        Assert.Contains(unknown.Validation!.Errors, error => error.Contains(completed.Id, StringComparison.Ordinal));
        Assert.Equal(resultBytes, File.ReadAllBytes(resultPath));
    }

    private static (Services Services, ChangeDossier Change, CisOutcomeAuthorizationRequest Request, PlanResult Plan)
        PrepareOutcomeAmendmentLifecycle(TemporaryRepository repository)
    {
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new(repository.Path,
            "Bounded amendment lifecycle", "Deliver the accepted behavior and its independent guide.", [])).Change);
        var request = OutcomeRequest(repository.Path) with { Outcome = change.Outcome, VisualUi = false };
        Assert.Equal(0, services.Changes.Authorize(new(repository.Path, change.Id, request,
            "Original human", "Authorize the explicit bounded outcome, scopes and overall allowance.")).ExitCode);
        var initial = services.Plans.Build(repository.Path, change.Id);
        AssertApprovedOutcomePlan(initial);
        var tasks = services.Plans.OrganizeOutcomeTasks(new(repository.Path, change.Id,
            [new("Original completed work", ["implementation"], "The original accepted behavior is reviewed.", "Review the original bounded fixture contract.")
                { Paths = ["src/original-completed.cs"] },
             new("Original unfinished work", ["implementation"], "The original remaining behavior is bounded.", "Review the original remaining fixture contract.")
                { Paths = ["src/original-unfinished.cs"] },
             new("Independent guide", ["documentation"], "The guide describes the unchanged accepted behavior.", "Review the independent guide.")
                { Paths = ["docs/independent-guide.md"], Category = "documentation" }],
            "Controller", "Separate original implementation and independent documentation under one outcome.", Assert.Single(initial.WorkItems).Id), "split");
        AssertApprovedOutcomePlan(tasks);
        var completed = Assert.Single(tasks.WorkItems, item => item.Title == "Original completed work");
        var started = Assert.Single(tasks.WorkItems, item => item.Title == "Original unfinished work");
        AssertApprovedOutcomePlan(services.Plans.TransitionTask(new(repository.Path, change.Id, completed.Id,
            "InProgress", "Controller", "Review the original bounded fixture contract.")));
        AssertApprovedOutcomePlan(services.Plans.TransitionTask(new(repository.Path, change.Id, started.Id,
            "InProgress", "Controller", "Begin original work before the scope amendment.")));
        var dossier = Path.Combine(repository.Path, change.RelativePath);
        File.WriteAllText(Path.Combine(dossier, "original-reviewed-result.txt"), "Reviewed only the original accepted synthetic fixture contract.\n");
        var completedPath = Path.Combine(dossier, completed.TaskPath!);
        File.WriteAllText(completedPath, File.ReadAllText(completedPath).Replace("- [ ]", "- [x]", StringComparison.Ordinal)
            + "\n| Original fixture review | Reviewed | original-reviewed-result.txt |\n");
        var finished = services.Plans.TransitionTask(new(repository.Path, change.Id, completed.Id,
            "Complete", "Controller", "Record completion of the original accepted review fixture."));
        AssertApprovedOutcomePlan(finished);
        return (services, change, request, finished);
    }
}
