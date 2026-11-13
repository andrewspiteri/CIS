using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Plan;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Theory]
    [InlineData("path")]
    [InlineData("identity")]
    [InlineData("scope")]
    [InlineData("contract")]
    public void OutcomePlanning_HoldsOnlyTheAffectedMalformedContractBeforeWriting(string defect)
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new(repository.Path,
            "Bounded contract", "Preserve the accepted behavior.", [])).Change);
        Assert.Equal(0, services.Changes.Authorize(new(repository.Path, change.Id,
            OutcomeRequest(repository.Path) with { VisualUi = false }, "Original human", "Authorize this bounded outcome.")).ExitCode);
        var first = Assert.Single(services.Plans.Build(repository.Path, change.Id).WorkItems);
        var supported = services.Plans.OrganizeOutcomeTasks(new(repository.Path, change.Id,
            [new("Independent guide", ["documentation"], "The guide describes the accepted behavior.", "Review the guide.")],
            "Controller", "Add independent supporting documentation."));
        var sibling = Assert.Single(supported.WorkItems, item => item.Id != first.Id);
        var siblingDigest = services.Plans.ReadTaskDigest(repository.Path, change.Id, sibling.Id);
        var taskPath = Path.Combine(repository.Path, change.RelativePath, first.TaskPath!);
        if (defect == "path")
        {
            var path = services.Changes.DossierFile(change, "plan.json");
            var plan = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            plan["workItems"]![0]!["taskPath"] = "../outside.md";
            repository.Write("docs/cis/changes/outside.md", "Preserve unrelated bytes.");
            File.WriteAllText(path, plan.ToJsonString());
        }
        else
        {
            var text = File.ReadAllText(taskPath);
            text = defect switch
            {
                "identity" => text.Replace("task_id: WORK-001", "task_id: WORK-999", StringComparison.Ordinal),
                "scope" => text.Replace("authorization_scope_basis: ", "unbound_scope_basis: ", StringComparison.Ordinal),
                _ => text.Replace(first.AcceptanceCriteria, "An unaccepted incompatible behavior.", StringComparison.Ordinal)
            };
            File.WriteAllText(taskPath, text);
        }
        var dossier = Path.Combine(repository.Path, change.RelativePath);
        var before = SnapshotOutcomeDossier(dossier);
        Assert.Throws<InvalidDataException>(() => services.Plans.ReadTaskDigest(repository.Path, change.Id, first.Id));
        var held = services.Plans.TransitionTask(new(repository.Path, change.Id, first.Id, "InProgress", "Controller", "Inspect the affected contract."));
        Assert.NotEqual(0, held.ExitCode);
        Assert.False(held.Applied);
        AssertOutcomeDossierUnchanged(before, dossier);
        Assert.Equal(siblingDigest, services.Plans.ReadTaskDigest(repository.Path, change.Id, sibling.Id));
        Assert.Equal(0, services.Plans.TransitionTask(new(repository.Path, change.Id, sibling.Id, "InProgress", "Controller", "Continue the independent accepted task.")).ExitCode);
        if (defect == "path") Assert.Equal("Preserve unrelated bytes.", File.ReadAllText(Path.Combine(repository.Path, "docs/cis/changes/outside.md")));
    }

    [Fact]
    public void OutcomePlanning_OrganizesAndRefreshesTasksUnderTheOriginalHumanAuthority()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "Bounded order delivery", "Deliver the accepted order behavior and supporting documentation.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var request = OutcomeRequest(repository.Path) with { Outcome = change.Outcome, VisualUi = false };
        var authorized = services.Changes.Authorize(new(repository.Path, change.Id, request,
            "Original human", "Authorize the outcome, explicit scope and shared finite limits once."));
        Assert.Equal(0, authorized.ExitCode);
        var authority = Assert.IsType<CisOutcomeAuthorization>(authorized.Authorization);
        var authorizationPath = services.Changes.DossierFile(change, "authorization.json");
        var originalAuthorization = File.ReadAllBytes(authorizationPath);
        var dossier = Path.GetDirectoryName(authorizationPath)!;
        var eventsPath = services.Changes.DossierFile(change, "events.jsonl");

        var built = services.Plans.Build(repository.Path, change.Id);
        AssertApprovedOutcomePlan(built);
        var seed = Assert.Single(built.WorkItems);
        var organized = services.Plans.OrganizeOutcomeTasks(new(repository.Path, change.Id,
            [new("Implement order behavior", ["implementation"], "Orders retain the accepted public behavior.", "Run the connected order checks.")
                { Paths = ["src/Orders.Api/Program.cs"], Assignee = "Implementation agent" },
             new("Document order behavior", ["documentation"], "The usage guide describes the accepted behavior.", "Review the guide against the public contract.")
                { Paths = ["docs/order-guide.md"], Category = "documentation" }],
            "Controller", "Separate implementation and supporting documentation within the shared outcome.", seed.Id), "split");
        AssertApprovedOutcomePlan(organized);
        var implementation = Assert.Single(organized.WorkItems, item => item.Title == "Implement order behavior");
        var documentation = Assert.Single(organized.WorkItems, item => item.Title == "Document order behavior");
        Assert.Equal(seed.Id, implementation.WorkOwnerTaskId);
        Assert.Equal(seed.Id, documentation.WorkOwnerTaskId);

        var supporting = services.Plans.OrganizeOutcomeTasks(new(repository.Path, change.Id,
            [new("Check order edge cases", ["implementation"], "The accepted edge cases remain covered.", "Run focused order regression checks.")
                { Paths = ["src/Orders.Api/OrderChecks.cs"], Category = "test" },
             new("Add a usage example", ["documentation"], "The guide includes one usable order example.", "Review the example with the guide.")
                { Paths = ["docs/order-example.md"], Category = "documentation" }],
            "Controller", "Add the supporting checks and example required to deliver the same accepted outcome."));
        AssertApprovedOutcomePlan(supporting);
        Assert.Equal(5, supporting.WorkItems.Count);
        AssertApprovedOutcomePlan(services.Plans.TransitionTask(new(repository.Path, change.Id, implementation.Id,
            "InProgress", "Controller", "Begin the authorized implementation.")));
        AssertApprovedOutcomePlan(services.Plans.TransitionTask(new(repository.Path, change.Id, documentation.Id,
            "InProgress", "Controller", "Begin the independent guide.")));
        var implementationPath = Path.Combine(dossier, implementation.TaskPath!);
        File.AppendAllText(implementationPath, "\n## Work notes\n\nPreserve this authored observation from the unfinished implementation.\n");

        var split = services.Plans.OrganizeOutcomeTasks(new(repository.Path, change.Id,
            [new("Implement order input", ["implementation"], "Order input follows the accepted contract.", "Run the order input checks.")
                { Paths = ["src/Orders.Api/OrderInput.cs"], Complexity = "low" },
             new("Implement order result", ["implementation"], "Order results follow the accepted contract.", "Run the order result checks.")
                { Paths = ["src/Orders.Api/OrderResult.cs"], Complexity = "low" }],
            "Controller", "Split unfinished implementation without creating a new work owner or approval.", implementation.Id), "split");
        AssertApprovedOutcomePlan(split);
        var retainedParent = Assert.Single(split.WorkItems, item => item.Id == implementation.Id);
        Assert.Equal("Decomposed", retainedParent.Status);
        Assert.Equal(implementation.InstanceKey, retainedParent.InstanceKey);
        Assert.Equal(seed.Id, retainedParent.WorkOwnerTaskId);
        var children = split.WorkItems.Where(item => item.ParentId == implementation.Id).ToArray();
        Assert.Equal(2, children.Length);
        Assert.All(children, child =>
        {
            Assert.Equal(seed.Id, child.WorkOwnerTaskId);
            Assert.Equal(new[] { "implementation" }, child.AuthorizationScopeIds);
            Assert.Equal(CisOutcomeAuthority.ScopeBasis(authority, ["implementation"]), child.AuthorizationScopeBasis);
        });
        Assert.Contains("Preserve this authored observation", File.ReadAllText(implementationPath), StringComparison.Ordinal);
        var progressed = services.Plans.TransitionTask(new(repository.Path, change.Id, children[0].Id,
            "InProgress", "Controller", "Continue a bounded child under its retained original owner."));
        AssertApprovedOutcomePlan(progressed);
        var retainedItems = JsonSerializer.Serialize(progressed.WorkItems);
        var retainedTaskBytes = progressed.WorkItems.ToDictionary(item => item.Id,
            item => File.ReadAllBytes(Path.Combine(dossier, item.TaskPath!)));
        var retainedEvents = File.ReadAllBytes(eventsPath);
        var independentDigest = services.Plans.ReadTaskDigest(repository.Path, change.Id, documentation.Id);

        var refreshedServices = CreateServices();
        var refreshed = refreshedServices.Plans.Build(repository.Path, change.Id);
        AssertApprovedOutcomePlan(refreshed);
        Assert.Equal(retainedItems, JsonSerializer.Serialize(refreshed.WorkItems));
        Assert.All(refreshed.WorkItems, item =>
            Assert.Equal(retainedTaskBytes[item.Id], File.ReadAllBytes(Path.Combine(dossier, item.TaskPath!))));
        var refreshedPlanBytes = File.ReadAllBytes(services.Changes.DossierFile(change, "plan.json"));
        var repeated = refreshedServices.Plans.Build(repository.Path, change.Id);
        AssertApprovedOutcomePlan(repeated);
        Assert.False(repeated.Applied);
        Assert.Equal("unchanged", repeated.Status);
        Assert.Equal(refreshedPlanBytes, File.ReadAllBytes(services.Changes.DossierFile(change, "plan.json")));
        Assert.Equal(independentDigest, refreshedServices.Plans.ReadTaskDigest(repository.Path, change.Id, documentation.Id));
        var reused = refreshedServices.Changes.Authorize(new(repository.Path, change.Id, request,
            "Controller", "Reuse the original human decision while refreshing the derived task views."));
        Assert.Equal(0, reused.ExitCode);
        Assert.Equal("authorization-reused", reused.Status);
        Assert.Equal(authority.Reviewer, reused.Authorization!.Reviewer);
        Assert.Equal(originalAuthorization, File.ReadAllBytes(authorizationPath));
        Assert.Equal(retainedEvents, File.ReadAllBytes(eventsPath));
        Assert.Equal(1, OutcomeEventTypes(eventsPath).Count(type => type == "outcome-authorized"));
        Assert.DoesNotContain("plan-approved", OutcomeEventTypes(eventsPath));

        foreach (var expansion in new[]
        {
            new OutcomeTaskDefinition("Unaccepted scope", ["new-product-scope"], "Deliver new behavior.", "Check the new behavior."),
            new OutcomeTaskDefinition("Unaccepted path", ["implementation"], "Change an unrelated component.", "Check the unrelated component.")
                { Paths = ["unrelated/Program.cs"] }
        })
        {
            var before = SnapshotOutcomeDossier(dossier);
            var rejected = refreshedServices.Plans.OrganizeOutcomeTasks(new(repository.Path, change.Id,
                [new("Otherwise valid support", ["documentation"], "Clarify the accepted guide.", "Review the clarification.")
                    { Paths = ["docs/clarification.md"], Category = "documentation" }, expansion],
                "Controller", "Present a material scope difference without rewriting unrelated accepted work."));
            Assert.NotEqual(0, rejected.ExitCode);
            Assert.False(rejected.Applied);
            Assert.Contains(rejected.Errors, error => error.Contains("targeted human decision", StringComparison.Ordinal));
            AssertOutcomeDossierUnchanged(before, dossier);
            Assert.Equal(independentDigest, refreshedServices.Plans.ReadTaskDigest(repository.Path, change.Id, documentation.Id));
        }

        var independentProgress = refreshedServices.Plans.TransitionTask(new(repository.Path, change.Id, documentation.Id,
            "Blocked", "Controller", "Record a guide dependency without reopening the outcome decision."));
        AssertApprovedOutcomePlan(independentProgress);
        Assert.Equal("Blocked", Assert.Single(independentProgress.WorkItems, item => item.Id == documentation.Id).Status);
        Assert.Equal(originalAuthorization, File.ReadAllBytes(authorizationPath));
        Assert.Equal(1, OutcomeEventTypes(eventsPath).Count(type => type == "outcome-authorized"));
        Assert.DoesNotContain("plan-approved", OutcomeEventTypes(eventsPath));
    }

    [Fact]
    public void OutcomePlanning_ChangedSelectedScopeBlocksOnlyAffectedWork()
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new ChangeCreateRequest(
            repository.Path, "Independent order work", "Deliver accepted order behavior with a supporting guide.",
            [new ChangeRoot("orders-api", "component")])).Change);
        var request = OutcomeRequest(repository.Path) with { Outcome = change.Outcome, VisualUi = false };
        var authorized = services.Changes.Authorize(new(repository.Path, change.Id, request,
            "Original human", "Authorize the explicit implementation and documentation scopes."));
        Assert.Equal(0, authorized.ExitCode);
        var authority = Assert.IsType<CisOutcomeAuthorization>(authorized.Authorization);
        var initial = services.Plans.Build(repository.Path, change.Id);
        AssertApprovedOutcomePlan(initial);
        var organized = services.Plans.OrganizeOutcomeTasks(new(repository.Path, change.Id,
            [new("Implement accepted behavior", ["implementation"], "Retain the accepted order contract.", "Run the connected order checks.")
                { Paths = ["src/Orders.Api/Program.cs"] },
             new("Write the independent guide", ["documentation"], "Describe the existing order contract.", "Review the guide against the existing contract.")
                { Paths = ["docs/order-guide.md"], Category = "documentation" }],
            "Controller", "Keep the independently scoped work explicit.", Assert.Single(initial.WorkItems).Id), "split");
        AssertApprovedOutcomePlan(organized);
        var affected = Assert.Single(organized.WorkItems, item => item.Title == "Implement accepted behavior");
        var independent = Assert.Single(organized.WorkItems, item => item.Title == "Write the independent guide");
        AssertApprovedOutcomePlan(services.Plans.TransitionTask(new(repository.Path, change.Id, independent.Id,
            "InProgress", "Controller", "Start the independently authorized guide.")));
        var independentDigest = services.Plans.ReadTaskDigest(repository.Path, change.Id, independent.Id);
        var amendedRequest = request with
        {
            Scope = request.Scope.Select(scope => scope.Id == "implementation"
                ? scope with { Description = "Implement the specifically revised accepted order behavior." } : scope).ToArray(),
            DecisionReference = "fixture:targeted-implementation-amendment"
        };
        var amended = services.Changes.Authorize(new(repository.Path, change.Id, amendedRequest,
            "Original human", "Authorize only the specific implementation scope difference."));
        Assert.Equal(0, amended.ExitCode);
        Assert.Equal(authority.Id, amended.Authorization!.Id);
        Assert.Equal(authority.Digest, Assert.Single(amended.Authorization.History).Digest);
        Assert.Equal(independentDigest, services.Plans.ReadTaskDigest(repository.Path, change.Id, independent.Id));
        var stale = Assert.Throws<InvalidDataException>(() => services.Plans.ReadTaskDigest(repository.Path, change.Id, affected.Id));
        Assert.Contains("targeted", stale.Message, StringComparison.OrdinalIgnoreCase);

        var dossier = Path.GetDirectoryName(services.Changes.DossierFile(change, "authorization.json"))!;
        var before = SnapshotOutcomeDossier(dossier);
        var denied = services.Plans.TransitionTask(new(repository.Path, change.Id, affected.Id,
            "InProgress", "Controller", "The old task contract has not been reconciled to its changed selected scope."));
        Assert.NotEqual(0, denied.ExitCode);
        Assert.False(denied.Applied);
        Assert.Contains(denied.Errors.Concat(denied.Validation?.Errors ?? []),
            error => error.Contains("scope", StringComparison.OrdinalIgnoreCase));
        AssertOutcomeDossierUnchanged(before, dossier);

        var continued = services.Plans.TransitionTask(new(repository.Path, change.Id, independent.Id,
            "Blocked", "Controller", "The unchanged guide remains independently eligible for its normal lifecycle."));
        Assert.Equal(0, continued.ExitCode);
        Assert.True(continued.Applied);
        Assert.Equal("Blocked", Assert.Single(continued.WorkItems, item => item.Id == independent.Id).Status);
        Assert.Equal(independentDigest, services.Plans.ReadTaskDigest(repository.Path, change.Id, independent.Id));
        var events = OutcomeEventTypes(services.Changes.DossierFile(change, "events.jsonl"));
        Assert.Equal(1, events.Count(type => type == "outcome-authorized"));
        Assert.Equal(1, events.Count(type => type == "outcome-authorization-amended"));
        Assert.DoesNotContain("plan-approved", events);
    }

    private static void AssertApprovedOutcomePlan(PlanResult result)
    {
        Assert.True(result.ExitCode == 0, string.Join(Environment.NewLine, result.Errors.Concat(result.Validation?.Errors ?? [])));
        Assert.Equal("Approved", result.PlanStatus);
        Assert.True(result.Validation is { Valid: true });
    }

    private static string[] OutcomeEventTypes(string path)
        => File.ReadLines(path).Select(line =>
        {
            using var item = JsonDocument.Parse(line);
            return item.RootElement.GetProperty("type").GetString()!;
        }).ToArray();

    private static Dictionary<string, byte[]> SnapshotOutcomeDossier(string dossier)
        => Directory.EnumerateFiles(dossier, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(dossier, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertOutcomeDossierUnchanged(IReadOnlyDictionary<string, byte[]> before, string dossier)
    {
        var after = SnapshotOutcomeDossier(dossier);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
    }
}
