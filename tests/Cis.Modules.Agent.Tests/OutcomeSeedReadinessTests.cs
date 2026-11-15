using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Plan;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void OutcomeSeed_ReadinessHoldsOnlyTheSeedAndConcreteUpdateContinuesWithinTheSameGrant(bool deferFirst, bool authorizeBeforeBuild)
    {
        using var fixture = new OutcomeParticipantFixture(concreteCriteria: false, authorizeBeforeBuild: authorizeBeforeBuild);
        var provider = new FakeProvider();
        // Readiness must not depend on the optional Plan contract reader.
        var controller = fixture.Service(provider, withContractReader: false);
        var plan = fixture.Planner.Show(fixture.Authority.Path, OutcomeParticipantFixture.Change);
        var seed = Assert.Single(plan.WorkItems);
        Assert.Equal("Draft", seed.Status);
        Assert.StartsWith(CisOutcomeTaskReadiness.SeedInstancePrefix, seed.InstanceKey);
        Assert.Equal(0, plan.ExitCode);
        Assert.Contains(fixture.Planner.Validate(fixture.Authority.Path, OutcomeParticipantFixture.Change).Validation!.Warnings,
            warning => warning.Contains("concrete observable acceptance", StringComparison.Ordinal));
        Assert.Equal(0, controller.Prepare(fixture.Authority.Path, OutcomeParticipantFixture.Change, seed.Id, "fake").ExitCode);
        var originalAuthorization = fixture.ReadChange("authorization.json");
        var taskPath = Path.Combine(fixture.Authority.Path, fixture.Context.DocumentationRoot, "changes",
            OutcomeParticipantFixture.Change, seed.TaskPath!);
        var initialTaskBytes = File.ReadAllBytes(taskPath);
        var ledgerPath = Path.Combine(fixture.Authority.Path, CisAgentWorkPaths.Ledger);
        var initialLedgerBytes = File.ReadAllBytes(ledgerPath);

        foreach (var target in new[] { "ReadyForReview", "Approved", "InProgress", "Complete" })
        {
            var held = Transition(target);
            Assert.False(held.Applied);
            Assert.Contains(held.Errors, message => message.Contains("cis plan task update", StringComparison.Ordinal));
        }
        Assert.Equal(initialTaskBytes, File.ReadAllBytes(taskPath));
        Assert.Contains("concrete observable acceptance", Assert.Throws<InvalidDataException>(() =>
            fixture.Planner.ReadTaskDigest(fixture.Authority.Path, OutcomeParticipantFixture.Change, seed.Id)).Message, StringComparison.Ordinal);
        var linked = new CisFeatureStoryTask("STORY-SEED", seed.Title, seed.Title, [], [], [seed.AcceptanceCriteria], [])
        { ChangeId = OutcomeParticipantFixture.Change, WorkOwnerTaskId = seed.Id, Category = "implementation", ApprovalGate = "none" };
        Assert.Contains(fixture.Planner.ValidateStoryTaskGate(fixture.Authority.Path, linked), message => message.Contains("planning placeholders", StringComparison.Ordinal));

        // Retain a superficially eligible persisted state to prove the actual entry
        // points enforce seed readiness independently of their normal status check.
        var planPath = Path.Combine(fixture.Authority.Path, fixture.Context.DocumentationRoot, "changes",
            OutcomeParticipantFixture.Change, "plan.json");
        var draftPlanBytes = File.ReadAllBytes(planPath);
        var forced = JsonNode.Parse(draftPlanBytes)!;
        forced["workItems"]![0]!["status"] = "InProgress";
        File.WriteAllText(planPath, forced.ToJsonString());
        File.WriteAllText(taskPath, System.Text.Encoding.UTF8.GetString(initialTaskBytes).Replace("task_status: Draft", "task_status: InProgress", StringComparison.Ordinal));
        foreach (var mode in new[] { CisAgentRunModes.Implement, CisAgentRunModes.Review })
        {
            var held = controller.Run(fixture.Authority.Path, OutcomeParticipantFixture.Change, seed.Id, "fake", mode,
                CisAgentPermissions.ReadOnly, "participant", "fake-json", 30, false, OutcomeParticipantFixture.Actor,
                TestContext.Current.CancellationToken);
            Assert.Contains(held.Diagnostics, message => message.Contains("planning placeholders", StringComparison.Ordinal));
        }
        var association = new CisNativeWorkOptions(OutcomeParticipantFixture.Change, seed.Id,
            AuthorityPath: fixture.Authority.Path, Actor: OutcomeParticipantFixture.Actor);
        Assert.Contains(controller.Inspect(fixture.Authority.Path, association).Errors, message => message.Contains("planning placeholders", StringComparison.Ordinal));
        var native = controller.Admit(new(fixture.Authority.Path, "native/unresolved-outcome-seed", "incremental-unit", 30, association));
        Assert.False(native.Allowed);
        Assert.Null(native.Scope);
        Assert.Contains(native.Errors, message => message.Contains("planning placeholders", StringComparison.Ordinal));
        Assert.False(fixture.Planner.Validate(fixture.Authority.Path, OutcomeParticipantFixture.Change).Validation!.Valid);
        Assert.Equal(0, provider.ExecuteCalls);
        File.WriteAllBytes(planPath, draftPlanBytes);
        File.WriteAllBytes(taskPath, initialTaskBytes);

        var siblingPlan = fixture.Planner.OrganizeOutcomeTasks(new(fixture.Authority.Path, OutcomeParticipantFixture.Change,
            [new("Inspect the independent selected behavior", ["main"], "The independent observation remains usable.", "Review the independent observation.")],
            OutcomeParticipantFixture.Actor, "Keep unrelated concrete work usable while the generated seed is unresolved."));
        Assert.Equal(0, siblingPlan.ExitCode);
        var sibling = Assert.Single(siblingPlan.WorkItems, task => task.Id != seed.Id);
        Assert.Equal(0, fixture.Planner.TransitionTask(new(fixture.Authority.Path, OutcomeParticipantFixture.Change, sibling.Id,
            "InProgress", OutcomeParticipantFixture.Actor, "Begin the independent concrete task.")).ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(fixture.Planner.ReadTaskDigest(fixture.Authority.Path, OutcomeParticipantFixture.Change, sibling.Id)));

        if (deferFirst)
        {
            Assert.Equal(0, Transition("Blocked").ExitCode);
            Assert.Equal(0, Transition("Deferred").ExitCode);
            var held = Transition("InProgress");
            Assert.False(held.Applied);
            Assert.Contains(held.Errors, message => message.Contains("planning placeholders", StringComparison.Ordinal));
        }
        const string acceptance = "The participant completes its selected behavior while unrelated approved scope remains usable.";
        const string validation = "Exercise participant completion and inspect original snapshot integrity through the normal controller.";
        var specified = fixture.Planner.OrganizeOutcomeTasks(new(fixture.Authority.Path, OutcomeParticipantFixture.Change,
            [new("Complete the accepted participant behavior", ["main"], acceptance, validation)],
            OutcomeParticipantFixture.Actor, "Replace only the generated placeholders under the original grant.", seed.Id), "update");
        Assert.Equal(0, specified.ExitCode);
        var ready = Assert.Single(specified.WorkItems, task => task.Id == seed.Id);
        Assert.Equal(seed.InstanceKey, ready.InstanceKey);
        Assert.Equal(seed.AuthorizationScopeBasis, ready.AuthorizationScopeBasis);
        Assert.Equal(seed.WorkOwnerTaskId, ready.WorkOwnerTaskId);
        Assert.Equal(acceptance, ready.AcceptanceCriteria);
        Assert.Equal(validation, ready.Validation);
        Assert.Contains(acceptance, File.ReadAllText(taskPath), StringComparison.Ordinal);
        Assert.Equal(initialLedgerBytes, File.ReadAllBytes(ledgerPath));
        Assert.Equal(originalAuthorization, fixture.ReadChange("authorization.json"));
        Assert.Equal(0, Transition("InProgress").ExitCode);
        var run = fixture.Run(controller);
        Assert.True(run.ExitCode == 0, string.Join("; ", run.Diagnostics));
        Assert.Equal(1, provider.ExecuteCalls);
        Assert.Equal(0, Transition("ReadyForReview").ExitCode);
        Assert.Equal(0, Transition("InProgress").ExitCode);
        File.WriteAllText(taskPath, File.ReadAllText(taskPath).Replace("- [ ]", "- [x]", StringComparison.Ordinal)
            + "\n| Accepted participant protocol | Passed | Real controller fixture and retained run evidence |\n");
        var finished = Transition("Complete");
        Assert.True(finished.ExitCode == 0, string.Join("; ", finished.Errors.Concat(finished.Validation?.Errors ?? [])));
        var work = controller.WorkStatus(fixture.Authority.Path, OutcomeParticipantFixture.Change, seed.Id).Work!;
        Assert.Equal(300_000, work.Change.AllocatedMilliseconds);
        Assert.Equal(8, work.Change.AllocatedAttempts);
        Assert.Equal(1, work.Task!.UsedAttempts);
        Assert.Equal(0, work.Change.ReservedMilliseconds);
        Assert.Equal(originalAuthorization, fixture.ReadChange("authorization.json"));
        var events = fixture.ReadChange("events.jsonl").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line)).ToArray();
        try { Assert.Single(events, entry => entry.RootElement.GetProperty("type").GetString() == "outcome-authorized"); }
        finally { foreach (var entry in events) entry.Dispose(); }

        PlanResult Transition(string status) => fixture.Planner.TransitionTask(new(fixture.Authority.Path, OutcomeParticipantFixture.Change,
            seed.Id, status, OutcomeParticipantFixture.Actor, "Exercise only the accepted seed readiness and continuation protocol."));
    }

    [Theory]
    [InlineData("Draft", false)]
    [InlineData("Approved", true)]
    [InlineData("ReadyForReview", true)]
    [InlineData("Complete", true)]
    public void OutcomeSeed_RecognizesUnfinishedPriorSeedWithoutMigratingAcceptedHistoricalContracts(string status, bool usable)
    {
        using var fixture = new OutcomeParticipantFixture(concreteCriteria: false);
        var seed = Assert.Single(fixture.Planner.Show(fixture.Authority.Path, OutcomeParticipantFixture.Change).WorkItems);
        var planPath = Path.Combine(fixture.Authority.Path, fixture.Context.DocumentationRoot, "changes", OutcomeParticipantFixture.Change, "plan.json");
        var taskPath = Path.Combine(fixture.Authority.Path, fixture.Context.DocumentationRoot, "changes", OutcomeParticipantFixture.Change, seed.TaskPath!);
        const string retainedInstance = "outcome:prior-accepted-instance";
        // This is the exact pre-marker record shape retained by earlier versions.
        var retained = JsonNode.Parse(File.ReadAllBytes(planPath))!;
        retained["workItems"]![0]!["instanceKey"] = retainedInstance;
        retained["workItems"]![0]!["status"] = status;
        File.WriteAllText(planPath, retained.ToJsonString());
        File.WriteAllText(taskPath, File.ReadAllText(taskPath).Replace(JsonSerializer.Serialize(seed.InstanceKey), JsonSerializer.Serialize(retainedInstance), StringComparison.Ordinal)
            .Replace("task_status: Draft", "task_status: " + status, StringComparison.Ordinal));
        var taskBytes = File.ReadAllBytes(taskPath);
        var ledgerPath = Path.Combine(fixture.Authority.Path, CisAgentWorkPaths.Ledger);
        var ledgerBytes = File.ReadAllBytes(ledgerPath);
        var authority = fixture.ReadChange("authorization.json");
        Assert.Equal(0, fixture.Planner.Build(fixture.Authority.Path, OutcomeParticipantFixture.Change).ExitCode);
        if (usable) Assert.False(string.IsNullOrWhiteSpace(fixture.Planner.ReadTaskDigest(fixture.Authority.Path, OutcomeParticipantFixture.Change, seed.Id)));
        else
        {
            Assert.Throws<InvalidDataException>(() => fixture.Planner.ReadTaskDigest(fixture.Authority.Path, OutcomeParticipantFixture.Change, seed.Id));
            var partial = fixture.Planner.OrganizeOutcomeTasks(new(fixture.Authority.Path, OutcomeParticipantFixture.Change,
                [new(seed.Title, ["main"], seed.AcceptanceCriteria, "Inspect the participant controller's retained completion evidence.")],
                OutcomeParticipantFixture.Actor, "A partial update must not disguise the remaining known acceptance placeholder.", seed.Id), "update");
            Assert.Equal(0, partial.ExitCode);
            var held = fixture.Planner.TransitionTask(new(fixture.Authority.Path, OutcomeParticipantFixture.Change, seed.Id,
                "InProgress", OutcomeParticipantFixture.Actor, "The remaining known placeholder is still unresolved."));
            Assert.False(held.Applied);
            Assert.Contains(held.Errors, message => message.Contains("planning placeholders", StringComparison.Ordinal));
        }
        if (usable) Assert.Equal(taskBytes, File.ReadAllBytes(taskPath));
        Assert.Equal(ledgerBytes, File.ReadAllBytes(ledgerPath));
        Assert.Equal(authority, fixture.ReadChange("authorization.json"));
    }

    [Fact]
    public void OutcomeSeed_ConcreteSplitChildrenCanFinishTheirRetainedAccountingContainer()
    {
        using var fixture = new OutcomeParticipantFixture(concreteCriteria: false);
        var seed = Assert.Single(fixture.Planner.Show(fixture.Authority.Path, OutcomeParticipantFixture.Change).WorkItems);
        var ledgerPath = Path.Combine(fixture.Authority.Path, CisAgentWorkPaths.Ledger);
        var ledger = File.ReadAllBytes(ledgerPath);
        var split = fixture.Planner.OrganizeOutcomeTasks(new(fixture.Authority.Path, OutcomeParticipantFixture.Change,
            [new("First accepted observation", ["main"], "The first observation retains its selected value.", "Review the first selected observation."),
             new("Second accepted observation", ["main"], "The second observation retains its selected value.", "Review the second selected observation.")],
            OutcomeParticipantFixture.Actor, "Split the proposal seed into its actual bounded accepted behavior.", seed.Id), "split");
        Assert.Equal(0, split.ExitCode);
        var reactivation = fixture.Planner.TransitionTask(new(fixture.Authority.Path, OutcomeParticipantFixture.Change, seed.Id,
            "InProgress", OutcomeParticipantFixture.Actor, "A container cannot become an unresolved executable seed."));
        Assert.False(reactivation.Applied);
        foreach (var child in split.WorkItems.Where(task => task.ParentId == seed.Id))
        {
            Assert.Equal(seed.Id, child.WorkOwnerTaskId);
            Assert.Equal(0, fixture.Planner.TransitionTask(new(fixture.Authority.Path, OutcomeParticipantFixture.Change, child.Id,
                "InProgress", OutcomeParticipantFixture.Actor, "Begin only this concrete child observation.")).ExitCode);
            var path = Path.Combine(fixture.Authority.Path, fixture.Context.DocumentationRoot, "changes", OutcomeParticipantFixture.Change, child.TaskPath!);
            File.WriteAllText(path, File.ReadAllText(path).Replace("- [ ]", "- [x]", StringComparison.Ordinal)
                + "\n| Concrete child observation | Passed | Isolated real-service protocol fixture |\n");
            var finished = fixture.Planner.TransitionTask(new(fixture.Authority.Path, OutcomeParticipantFixture.Change, child.Id,
                "Complete", OutcomeParticipantFixture.Actor, "Complete the accepted concrete child observation."));
            Assert.True(finished.ExitCode == 0, string.Join("; ", finished.Errors.Concat(finished.Validation?.Errors ?? [])));
        }
        var completed = fixture.Planner.TransitionTask(new(fixture.Authority.Path, OutcomeParticipantFixture.Change, seed.Id,
            "Complete", OutcomeParticipantFixture.Actor, "Close only the retained container after every concrete child is complete."));
        // Container checklist/evidence remains explicit rather than inferred from children.
        Assert.False(completed.Applied);
        var parentPath = Path.Combine(fixture.Authority.Path, fixture.Context.DocumentationRoot, "changes", OutcomeParticipantFixture.Change, seed.TaskPath!);
        File.WriteAllText(parentPath, File.ReadAllText(parentPath).Replace("- [ ]", "- [x]", StringComparison.Ordinal)
            + "\n| Accepted child observations | Passed | Both concrete child transitions completed |\n");
        completed = fixture.Planner.TransitionTask(new(fixture.Authority.Path, OutcomeParticipantFixture.Change, seed.Id,
            "Complete", OutcomeParticipantFixture.Actor, "Retain the completed child aggregate and original accounting owner."));
        Assert.True(completed.ExitCode == 0, string.Join("; ", completed.Errors.Concat(completed.Validation?.Errors ?? [])));
        Assert.Equal("Complete", Assert.Single(completed.WorkItems, task => task.Id == seed.Id).Status);
        Assert.False(string.IsNullOrWhiteSpace(fixture.Planner.ReadTaskDigest(fixture.Authority.Path, OutcomeParticipantFixture.Change, seed.Id)));
        Assert.Equal(ledger, File.ReadAllBytes(ledgerPath));
    }

    [Theory]
    [InlineData("Blocked")]
    [InlineData("Deferred")]
    public void OutcomeSeed_OldStartedPlaceholderContractCannotBeRewrittenAndCanResolveThroughRetainedChildren(string disposition)
    {
        using var fixture = new OutcomeParticipantFixture(concreteCriteria: false);
        var seed = Assert.Single(fixture.Planner.Show(fixture.Authority.Path, OutcomeParticipantFixture.Change).WorkItems);
        var dossier = Path.Combine(fixture.Authority.Path, fixture.Context.DocumentationRoot, "changes", OutcomeParticipantFixture.Change);
        var planPath = Path.Combine(dossier, "plan.json");
        var taskPath = Path.Combine(dossier, seed.TaskPath!);
        const string originalInstance = "outcome:started-before-seed-readiness";
        var retained = JsonNode.Parse(File.ReadAllBytes(planPath))!;
        retained["workItems"]![0]!["instanceKey"] = originalInstance;
        retained["workItems"]![0]!["status"] = "InProgress";
        File.WriteAllText(planPath, retained.ToJsonString());
        File.WriteAllText(taskPath, File.ReadAllText(taskPath).Replace(JsonSerializer.Serialize(seed.InstanceKey), JsonSerializer.Serialize(originalInstance), StringComparison.Ordinal)
            .Replace("task_status: Draft", "task_status: InProgress", StringComparison.Ordinal));
        var provider = new FakeProvider();
        var controller = fixture.Service(provider, withContractReader: false);
        var accepted = fixture.Run(controller);
        Assert.True(accepted.ExitCode == 0, string.Join("; ", accepted.Diagnostics));
        Assert.Equal(1, provider.ExecuteCalls);
        var acceptedManifestPath = Path.Combine(fixture.Authority.Path, AgentService.RootPath, "runs", accepted.Run!.Manifest.RunId, "manifest.json");
        var acceptedManifest = File.ReadAllBytes(acceptedManifestPath);
        var envelope = File.ReadAllBytes(fixture.EnvelopePath);
        Assert.Equal(0, fixture.Planner.TransitionTask(new(fixture.Authority.Path, OutcomeParticipantFixture.Change, seed.Id,
            "Blocked", OutcomeParticipantFixture.Actor, "Retain the old started contract while resolving its next action.")).ExitCode);
        if (disposition == "Deferred")
            Assert.Equal(0, fixture.Planner.TransitionTask(new(fixture.Authority.Path, OutcomeParticipantFixture.Change, seed.Id,
                "Deferred", OutcomeParticipantFixture.Actor, "Postpone the old contract without replacing its accepted evidence.")).ExitCode);
        var taskBytes = File.ReadAllBytes(taskPath);
        var planBytes = File.ReadAllBytes(planPath);
        var ledgerPath = Path.Combine(fixture.Authority.Path, CisAgentWorkPaths.Ledger);
        var ledger = File.ReadAllBytes(ledgerPath);
        var authority = fixture.ReadChange("authorization.json");
        OutcomeTaskDefinition Definition(string title) => new(title, ["main"],
            "The selected participant preserves its accepted observation.", "Review the selected observation through its existing controller.");

        var denied = fixture.Planner.OrganizeOutcomeTasks(new(fixture.Authority.Path, OutcomeParticipantFixture.Change,
            [Definition("Replace the already-started contract")], OutcomeParticipantFixture.Actor,
            "Known placeholders in old started work do not authorize rewriting its executable contract.", seed.Id), "update");

        Assert.False(denied.Applied);
        Assert.Contains(denied.Errors, message => message.Contains("started task", StringComparison.Ordinal));
        Assert.Equal(taskBytes, File.ReadAllBytes(taskPath));
        Assert.Equal(planBytes, File.ReadAllBytes(planPath));
        var split = fixture.Planner.OrganizeOutcomeTasks(new(fixture.Authority.Path, OutcomeParticipantFixture.Change,
            [Definition("First current replacement observation"), Definition("Second current replacement observation")],
            OutcomeParticipantFixture.Actor, "Preserve started history and derive concrete children under the same owner.", seed.Id), "split");
        Assert.True(split.ExitCode == 0, string.Join("; ", split.Errors.Concat(split.Validation?.Errors ?? [])));
        var parent = Assert.Single(split.WorkItems, task => task.Id == seed.Id);
        Assert.Equal("Decomposed", parent.Status);
        Assert.Equal(originalInstance, parent.InstanceKey);
        Assert.Equal(seed.AcceptanceCriteria, parent.AcceptanceCriteria);
        Assert.Equal(seed.Validation, parent.Validation);
        var children = split.WorkItems.Where(task => task.ParentId == seed.Id).ToArray();
        Assert.Equal(2, children.Length);
        Assert.All(children, child => Assert.Equal(seed.Id, child.WorkOwnerTaskId));
        Assert.Equal(0, fixture.Planner.TransitionTask(new(fixture.Authority.Path, OutcomeParticipantFixture.Change, children[0].Id,
            "InProgress", OutcomeParticipantFixture.Actor, "Continue only the concrete current replacement child.")).ExitCode);
        Assert.True(fixture.Planner.Validate(fixture.Authority.Path, OutcomeParticipantFixture.Change).Validation!.Valid);
        var inspection = controller.Inspect(fixture.Authority.Path, new(OutcomeParticipantFixture.Change, children[0].Id,
            AuthorityPath: fixture.Authority.Path, Actor: OutcomeParticipantFixture.Actor));
        Assert.Empty(inspection.Errors);
        Assert.Equal(ledger, File.ReadAllBytes(ledgerPath));
        Assert.Equal(acceptedManifest, File.ReadAllBytes(acceptedManifestPath));
        Assert.Equal(envelope, File.ReadAllBytes(fixture.EnvelopePath));
        Assert.Equal(authority, fixture.ReadChange("authorization.json"));
        Assert.Equal(1, provider.ExecuteCalls);
    }

    [Fact]
    public void OutcomeSeed_KnownPlaceholderWordsDoNotMigrateExistingLegacyExecutionContracts()
    {
        using var repository = AgentRepository.Create();
        repository.AddEligibleChange();
        const string path = "docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md";
        repository.Write(path, repository.Read(path) + "\n" + CisOutcomeTaskReadiness.SeedAcceptance + "\n" + CisOutcomeTaskReadiness.SeedValidation + "\n");
        var original = File.ReadAllBytes(Path.Combine(repository.Path, path));
        var provider = new FakeProvider();
        var controller = new AgentService(new CisRepositoryContextResolver(), [provider], clock: Clock);

        var run = controller.Run(repository.Path, "CIS-0001", "WORK-090", "fake", CisAgentRunModes.Review,
            CisAgentPermissions.ReadOnly, null, "fake-json", 30, false, "Legacy controller", TestContext.Current.CancellationToken);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(1, provider.ExecuteCalls);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(repository.Path, path)));
    }
}
