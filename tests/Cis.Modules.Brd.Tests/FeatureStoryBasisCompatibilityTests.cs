using System.Security.Cryptography;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;

namespace Cis.Modules.Brd.Tests;

public sealed partial class FeatureIntakeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoryPlanning_UnchangedLegacyBoundariesRetainApprovalAndExactExecutionDigest(bool nullBasis)
    {
        using var f = new Fixture();
        File.WriteAllText(f.Request.SourcePath, PrivacyStory + "\n## Boundaries\n\nRetain the privacy ownership boundary.\n"
            + "\n## Defaults\n\nThe public preference remains opt-in.\n"
            + "\n## Compatibility\n\nExisting consumers retain the versioned representation.\n"
            + "\n## Interfaces\n\nUse the existing archive interface.\n"
            + "\n## API contract\n\nRead-only boundary details.\n");
        AddDeliveryCode(f, "backend", "archive.ts", "export class Archive { getDocumentVersion() {} }");
        AddDeliveryCode(f, "customer-ui", "privacy.ts", "export class Privacy { acknowledgeDocumentVersion() {} }");
        CreateIntake(f);
        var capture = new PlanningContractCapture();
        FeatureIntakeService Fresh() => new(f.Registry, f.Importer, new DocumentationCatalogMerger(), [f.Baseline],
            textGeneration: new StoryTaskGeneration(), storyExecutor: capture);
        var service = Fresh();
        var id = service.Navigation(f.Authority).Features.Single().Stories.Single().Id;
        var prepared = PrepareAssessedStory(service, f, id, service.Story(f.Authority, "referrals", id, false));
        Assert.Empty(prepared.Errors);
        var approved = service.UpdateStoryWorkflow(f.Authority, "referrals", id, "approve", prepared.PlanHash!, prepared.Revision!,
            "Historical reviewer", "Retain the original approved scope and its original boundary identity.");
        Assert.Empty(approved.Errors);
        var request = service.Requests(f.Authority).Single();
        var recordPath = Path.Combine(Path.GetDirectoryName(Path.Combine(f.Authority, request.RequestPath))!, "story-tasks", id + ".json");
        RetainSevenFieldStoryRecord(recordPath);
        if (nullBasis)
        {
            // Current authoring records scoped bases. Encode the older supported shape using
            // its retained hash implementation; exercise currentness and execution via public APIs.
            var state = typeof(FeatureIntakeService).GetMethod("ReadWizard", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(service, [f.Authority, "referrals"]);
            var input = typeof(FeatureIntakeService).GetMethod("ReadDeliveryInput", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(service, [state, false]);
            var legacyScope = (string)typeof(FeatureIntakeService).GetMethod("LegacyStoryScopeHash", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [state, input, id])!;
            var legacyRecord = JsonNode.Parse(File.ReadAllText(recordPath))!;
            legacyRecord.AsObject().Remove("basis");
            legacyRecord["scopeHash"] = legacyScope;
            File.WriteAllText(recordPath, legacyRecord.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
        var retained = File.ReadAllBytes(recordPath);
        service = Fresh();
        var current = service.Story(f.Authority, "referrals", id, false);
        Assert.Empty(current.Errors); Assert.Equal("Approved", current.PlanState);
        var again = Fresh().Story(f.Authority, "referrals", id, false);
        Assert.Equal(current.PlanHash, again.PlanHash); Assert.Equal(current.Revision, again.Revision);
        Assert.Equal(retained, File.ReadAllBytes(recordPath));
        var preview = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", cancellation: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Errors);
        var executed = service.ExecuteStoryTask(f.Authority, "referrals", id, "T1", true, preview.PlanHash, preview.Revision,
            preview.ExecutionPlan!.Hash, "Fixture controller", cancellation: TestContext.Current.CancellationToken);
        Assert.Empty(executed.Errors);
        var work = Assert.IsType<CisStoryTaskWork>(capture.Observed);
        Assert.Equal(1, work.ContractVersion);
        Assert.Contains("Retain the privacy ownership boundary.", work.Constraints, StringComparison.Ordinal);
        Assert.DoesNotContain("public preference remains opt-in", work.Constraints, StringComparison.Ordinal);
        var originalDigest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            work.Slug,
            work.StoryId,
            work.PlanHash,
            work.Definition,
            Task = new
            {
                work.Task.Id,
                work.Task.Title,
                work.Task.Description,
                work.Task.RepositoryIds,
                work.Task.RequirementNumbers,
                work.Task.AcceptanceCriteria,
                work.Task.DependsOn
            },
            work.StoryCriteria,
            work.Direction,
            work.Constraints
        })));
        Assert.Equal(originalDigest, CisStoryEngineeringContract.Digest(work));
        Assert.NotEqual(originalDigest, CisStoryEngineeringContract.Digest(work with { ContractVersion = 2 }));
    }
}
