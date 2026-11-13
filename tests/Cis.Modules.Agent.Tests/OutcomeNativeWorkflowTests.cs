using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Decision;
using Cis.Modules.Graph;
using Cis.Modules.Impact;
using Cis.Modules.Plan;
using Cis.Modules.Repository;
using Cis.Modules.Testing;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Fact]
    public void OutcomeWorkflow_NativeReportsSurviveSupportingWorkSplitRefreshAndNormalCompletionUnderOneGrant()
    {
        using var repository = AgentRepository.Create();
        const string actor = "Outcome controller";
        repository.Write("src/X.cs", "public static class X { public static bool Value => true; }");
        repository.Write("src/Y.cs", "public static class Y { public static bool Value => true; }");
        repository.Write("tests/XTests.cs", "class XTests { }");
        repository.Write("tests/YTests.cs", "class YTests { }");
        repository.Write("config.json", "{}");
        var graph = new ProtectedEvidenceGraph(repository.Path);
        repository.Write(".cis/local/graph/manifest.json", JsonSerializer.Serialize(graph.Read(repository.Path).Manifest, JsonOptions));
        var resolver = new CisRepositoryContextResolver();
        var changes = new ChangeDossierStore(resolver);
        var storage = new CisOwnedStoragePolicy(256 * 1024 * 1024, [new(repository.Path, ".cis/local")])
        { RestorationBytes = 16 * 1024 * 1024 };
        var grant = new CisOutcomeAuthorizationRequest("Deliver the neutral native fixture outcome.",
            [new("native", "The neutral X/Y behaviors and their supporting verification.", ["src", "tests", "config.json"])],
            ["No publication or unrelated product scope."], 600, 12, 60, storage, [actor], true, true, false, false,
            "fixture:one-human-outcome-decision");
        var created = changes.Create(new(repository.Path, "Neutral native outcome", grant.Outcome, [], "CIS-0001")
        { OutcomeAuthorization = grant });
        Assert.True(created.ExitCode == 0, string.Join("; ", created.Errors));
        var authorized = changes.Authorize(new(repository.Path, "CIS-0001", grant, "Maintainer", "One explicit synthetic decision covers this complete bounded outcome."));
        Assert.True(authorized.ExitCode == 0, string.Join("; ", authorized.Errors));
        var context = resolver.Resolve(repository.Path).Context!;
        var authorization = CisOutcomeAuthority.Read(context, "CIS-0001")!;
        var authorityBytes = File.ReadAllBytes(CisOutcomeAuthority.Path(context, "CIS-0001"));
        var planner = new PlanningService(changes, new ImpactAnalysisService(changes, new GraphQueryService(resolver)),
            new DecisionService(changes, resolver), resolver);
        var built = planner.Build(repository.Path, "CIS-0001");
        Assert.True(built.ExitCode == 0, string.Join("; ", built.Errors));
        var first = Assert.Single(built.WorkItems);
        var exampleDirectory = Path.Combine(context.DocumentationPath, "changes/CIS-0001");
        var exampleFiles = Directory.GetFiles(exampleDirectory, "*", SearchOption.AllDirectories);
        var humanFiles = exampleFiles.Where(path => path.EndsWith(".md", StringComparison.Ordinal)).ToArray();
        TestContext.Current.TestOutputHelper?.WriteLine($"One-task example: human decisions=1; human-facing Markdown files={humanFiles.Length}; human-facing bytes={humanFiles.Sum(path => new FileInfo(path).Length)}; machine files={exampleFiles.Length - humanFiles.Length}; machine bytes={exampleFiles.Except(humanFiles).Sum(path => new FileInfo(path).Length)}.");
        Start(first.Id);
        var controller = new AgentService(resolver, [], taskContracts: planner);
        var request = new CisAgentWorkAllocationRequest(600, 12, 60, [new(first.Id, 300, 4, 30)],
            grant.DecisionReference, "fixture:native-shared-storage", AcknowledgeHistoricalUnknown: true)
        { Storage = storage, Controllers = [actor] };
        var initial = controller.AllocateWork(repository.Path, "CIS-0001", WriteWorkInput(repository, "outcome-initial.json", request),
            actor, "Controller distribution under the existing outcome; no further human approval.");
        Assert.True(initial.ExitCode == 0, string.Join("; ", initial.Errors));
        var testing = new IncrementalTestService(graph, controller, controller);
        var xProfile = WriteProfile("outcome-x", "X");
        var firstResult = testing.Run(Options(first.Id, xProfile), 0, TestContext.Current.CancellationToken);
        Assert.True(firstResult.ExitCode == 0, JsonSerializer.Serialize(firstResult, JsonOptions));
        Assert.Equal(1, firstResult.ExecutedUnitBatches);
        Assert.True(firstResult.ScopeComplete);
        var firstStatePath = Path.Combine(repository.Path, firstResult.StatePath!);
        var state = JsonSerializer.Deserialize<IncrementalTestState>(File.ReadAllBytes(firstStatePath), JsonOptions)!;
        var accepted = Assert.Single(state.Batches.Values).Unit!;
        Assert.Equal(1, accepted.Passed);
        var reportPath = Path.Combine(Path.GetDirectoryName(firstStatePath)!, accepted.ReportPath);
        var report = File.ReadAllBytes(reportPath);
        var acceptedFingerprint = accepted.Fingerprint;

        var support = planner.OrganizeOutcomeTasks(new(repository.Path, "CIS-0001",
            [Definition("Verify the separate Y behavior", "Y")], actor, "Necessary supporting work stays inside the existing outcome."));
        Assert.True(support.ExitCode == 0, string.Join("; ", support.Errors));
        var supporting = Assert.Single(support.WorkItems, item => item.Id != first.Id);
        Start(supporting.Id);
        var beforeSupport = controller.WorkStatus(repository.Path, "CIS-0001", first.Id).Work!;
        var added = controller.AllocateWork(repository.Path, "CIS-0001", WriteWorkInput(repository, "outcome-support.json", request with
        {
            Tasks = [new(supporting.Id, 180, 3, 30)], ExpectedRevision = beforeSupport.Revision
        }), actor, "Distribute unused overall capacity without another decision.");
        Assert.True(added.ExitCode == 0, string.Join("; ", added.Errors));
        var supportingResult = testing.Run(Options(supporting.Id, WriteProfile("outcome-y", "Y")), 0, TestContext.Current.CancellationToken);
        Assert.True(supportingResult.ExitCode == 0, JsonSerializer.Serialize(supportingResult, JsonOptions));
        Assert.Equal(1, supportingResult.ExecutedUnitBatches);

        var split = planner.OrganizeOutcomeTasks(new(repository.Path, "CIS-0001",
            [Definition("Finish the first bounded X check", "X"), Definition("Retain the second bounded X obligation", "X")],
            actor, "Split unfinished work without replacing its owner or prior result.", first.Id), "split");
        Assert.True(split.ExitCode == 0, string.Join("; ", split.Errors));
        var children = split.WorkItems.Where(item => item.ParentId == first.Id).ToArray();
        Assert.Equal(2, children.Length);
        Assert.All(children, child => Assert.Equal(first.Id, child.WorkOwnerTaskId));
        Start(children[0].Id);
        var refreshed = planner.Build(repository.Path, "CIS-0001");
        Assert.True(refreshed.ExitCode == 0, string.Join("; ", refreshed.Errors));
        var callsPath = Path.Combine(repository.Path, ".cis/native-calls.log");
        var calls = File.ReadAllLines(callsPath);
        Assert.Equal(2, calls.Length);
        var ledgerPath = Path.Combine(repository.Path, CisAgentWorkPaths.Ledger);
        var beforeProjection = File.ReadAllBytes(ledgerPath);
        var beforeReuse = controller.WorkStatus(repository.Path, "CIS-0001", children[0].Id).Work!;
        Assert.Equal(first.Id, beforeReuse.OwnerTaskId);
        var inspection = controller.Inspect(repository.Path, Options(children[0].Id, xProfile).Work);
        Assert.Empty(inspection.Errors);
        Assert.Equal(first.Id, inspection.Control!.Work!.OwnerTaskId);
        Assert.Equal(beforeProjection, File.ReadAllBytes(ledgerPath));
        var reused = testing.Run(Options(children[0].Id, xProfile), 0, TestContext.Current.CancellationToken);
        Assert.True(reused.ExitCode == 0, JsonSerializer.Serialize(reused, JsonOptions));
        Assert.Equal(0, reused.ExecutedUnitBatches);
        Assert.Equal(1, reused.ReusedUnitBatches);
        Assert.Equal(calls, File.ReadAllLines(callsPath));
        Assert.Equal(report, File.ReadAllBytes(reportPath));

        Assert.Equal(0, planner.TransitionTask(new(repository.Path, "CIS-0001", children[0].Id, "ReadyForReview", actor,
            "Review the same authorized work without invalidating its passing result.")).ExitCode);
        var duringReview = testing.Run(Options(children[0].Id, xProfile), 0, TestContext.Current.CancellationToken);
        Assert.True(duringReview.ExitCode == 0, JsonSerializer.Serialize(duringReview, JsonOptions));
        Assert.Equal(0, duringReview.ExecutedUnitBatches);
        Assert.Equal(calls, File.ReadAllLines(callsPath));
        Start(children[0].Id);
        var childPath = Path.Combine(context.DocumentationPath, "changes/CIS-0001", children[0].TaskPath!);
        File.WriteAllText(childPath, File.ReadAllText(childPath).Replace("- [ ]", "- [x]", StringComparison.Ordinal)
            + "\n| Native fixture | Passed: one current X case | " + Path.GetRelativePath(repository.Path, reportPath).Replace('\\', '/') + " |\n");
        var completed = planner.TransitionTask(new(repository.Path, "CIS-0001", children[0].Id, "Complete", actor,
            "Complete through normal checks with retained native evidence; no human completion approval."));
        Assert.True(completed.ExitCode == 0, string.Join("; ", completed.Errors));
        var afterCompletion = testing.Run(Options(children[0].Id, xProfile), 0, TestContext.Current.CancellationToken);
        Assert.True(afterCompletion.ExitCode == 0, JsonSerializer.Serialize(afterCompletion, JsonOptions));
        Assert.Equal(0, afterCompletion.ExecutedUnitBatches);
        Assert.Equal(1, afterCompletion.ReusedUnitBatches);
        Assert.Equal(calls, File.ReadAllLines(callsPath));
        state = JsonSerializer.Deserialize<IncrementalTestState>(File.ReadAllBytes(firstStatePath), JsonOptions)!;
        Assert.Equal(acceptedFingerprint, Assert.Single(state.Batches.Values).Unit!.Fingerprint);
        Assert.Equal(SHA256.HashData(report), SHA256.HashData(File.ReadAllBytes(reportPath)));
        var final = controller.WorkStatus(repository.Path, "CIS-0001", children[0].Id).Work!;
        Assert.Equal(first.Id, final.OwnerTaskId);
        Assert.Equal(600_000, final.Change.AllocatedMilliseconds);
        Assert.Equal(beforeReuse.Task!.UsedAttempts, final.Task!.UsedAttempts);
        Assert.True(final.Task.ConsumedMilliseconds >= beforeReuse.Task.ConsumedMilliseconds);
        Assert.Equal(0, final.Change.ReservedMilliseconds);
        var ledger = JsonNode.Parse(File.ReadAllBytes(Path.Combine(repository.Path, CisAgentWorkPaths.Ledger)))!;
        var owners = ledger["changes"]!["CIS-0001"]!["owners"]!.AsObject();
        Assert.Equal(2, owners.Count);
        Assert.True(owners.Sum(owner => owner.Value!["milliseconds"]!.GetValue<long>()) <= 600_000);
        Assert.Equal(authorityBytes, File.ReadAllBytes(CisOutcomeAuthority.Path(context, "CIS-0001")));
        Assert.Empty(CisOutcomeAuthority.Read(context, "CIS-0001")!.History);
        var completedDocument = File.ReadAllText(childPath);
        File.WriteAllText(childPath, completedDocument.Replace(children[0].AcceptanceCriteria,
            "A different unaccepted behavior.", StringComparison.Ordinal));
        var heldContract = controller.Inspect(repository.Path,
            new("CIS-0001", children[0].Id, AuthorityPath: repository.Path, Actor: actor));
        Assert.Contains(heldContract.Errors, error => error.Contains("canonical contract", StringComparison.Ordinal));
        Assert.Equal(calls, File.ReadAllLines(callsPath));
        Assert.Equal(report, File.ReadAllBytes(reportPath));
        File.WriteAllText(childPath, completedDocument);

        void Start(string task)
        {
            var result = planner.TransitionTask(new(repository.Path, "CIS-0001", task, "InProgress", actor, "Start this in-scope task under its existing outcome."));
            Assert.True(result.ExitCode == 0, string.Join("; ", result.Errors));
        }

        static OutcomeTaskDefinition Definition(string title, string source) => new(title, ["native"],
            "The bounded native case passes and its current result is retained.", "Use the existing native unit fixture.")
        { Paths = ["src/" + source + ".cs", "tests/" + source + "Tests.cs"] };

        IncrementalTestOptions Options(string task, string profile) => new(repository.Path, profile, "unit", 1, 60)
        { Work = new("CIS-0001", task, AuthorityPath: repository.Path, Actor: actor) };

        string WriteProfile(string id, string source)
        {
            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (dotnet is null || !File.Exists(dotnet))
                dotnet = Path.GetFullPath("../../../dotnet" + (OperatingSystem.IsWindows() ? ".exe" : ""), RuntimeEnvironment.GetRuntimeDirectory());
            var fixture = Path.Combine(AppContext.BaseDirectory, "native-fixture", "Cis.Testing.NativeFixture.dll");
            var profile = new IncrementalTestProfile
            {
                SchemaVersion = 1, Id = id, SourceFiles = ["src/" + source + ".cs"], TestFiles = ["tests/" + source + "Tests.cs"],
                ConfigFiles = ["config.json"], ToolFiles = [dotnet, fixture], FullTestFilter = "FullyQualifiedName~Fixture." + source + "Tests",
                UnitCommand = [dotnet, fixture, "unit", "{output}", "{sourceFile}", "{testFilter}"], UnitReportPath = "unit.trx",
                TimeoutSeconds = 10, MaxRunBytes = 8 * 1024 * 1024, MaxRetainedBytes = 32 * 1024 * 1024, MinFreeBytes = 0
            };
            var relative = id + ".json";
            repository.Write(relative, JsonSerializer.Serialize(profile, JsonOptions));
            return relative;
        }
    }
}
