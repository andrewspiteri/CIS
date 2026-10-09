using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed partial class AgentService
{
    public AgentResult RecoverBrdRevision(string repositoryPath, string runId, string actor, string reason)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return New(null, "invalid-repository", diagnostics: diagnostics);
        if (!SafeRunId(runId)) diagnostics.Add("ERROR: Run ID is unsafe.");
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(reason))
            diagnostics.Add("ERROR: Recovery actor and reason are required.");
        var retained = diagnostics.Count == 0 ? ReadRun(context, runId, diagnostics) : null;
        if (retained is null) return New(context, "invalid", diagnostics: diagnostics);
        var source = retained.Manifest;
        var reviewRunId = retained.Result?.Revision?.ReviewRunId;
        if (source.Status != CisAgentRunStates.InvalidEvidence || source.TaskId != BrdRevisionTask
            || source.Mode != CisAgentRunModes.Implement || source.Permission != CisAgentPermissions.WorkspaceWrite
            || retained.Result?.Status != CisAgentRunStates.InvalidEvidence || reviewRunId is null || !SafeRunId(reviewRunId))
            diagnostics.Add("ERROR: Local recovery requires a retained failed BRD revision with its original review identity.");
        if (retained.Artifacts.Any(item => !item.Valid)
            || !string.Equals(source.ResultDigest, DigestOptional(Path.Combine(RunPath(context, runId), "result.json")), StringComparison.OrdinalIgnoreCase))
            diagnostics.Add("ERROR: Retained revision evidence is missing or changed.");
        var envelopePath = EnvelopePath(context, source.ChangeId, source.TaskId);
        var envelope = Read<AgentTaskEnvelope>(envelopePath, "envelope", diagnostics);
        if (envelope is null || envelope.Id != source.EnvelopeId || DigestOptional(envelopePath) != source.EnvelopeDigest)
            diagnostics.Add("ERROR: The original revision envelope is no longer current.");
        if (envelope is not null)
        {
            var digests = new List<string>();
            foreach (var artifact in envelope.ContextArtifacts)
            {
                if (!CisPathSafety.TryResolveUnderRoot(context.RepositoryPath, artifact, out var path))
                { diagnostics.Add("ERROR: The original revision contains an unsafe context path."); break; }
                digests.Add(artifact + ":" + DigestOptional(path));
            }
            if (Sha(string.Join("\n", digests)) != source.AcceptedScopeDigest)
                diagnostics.Add("ERROR: Approved revision context changed after the retained run.");
        }
        if (diagnostics.Count > 0) return New(context, "invalid", run: retained, diagnostics: diagnostics);

        var target = CisProductDocumentPaths.Resolve(context.DocumentationPath, "specs", "business-requirements.md");
        var relative = Relative(context.RepositoryPath, target);
        var original = File.Exists(target) ? File.ReadAllText(target) : string.Empty;
        var dispositionPath = BrdDispositionPath(context, reviewRunId!);
        CisBrdReviewDispositionDocument? disposition = null;
        if (!File.Exists(dispositionPath)
            || !CisBrdReviewDispositionCodec.TryParse(File.ReadAllText(dispositionPath), out disposition, out _)
            || disposition is null || !CisBrdReviewDispositionCodec.IsApproved(disposition)
            || disposition.AppliedByRunId is not null || disposition.ReviewRunId != reviewRunId)
            diagnostics.Add("ERROR: Recovery requires the original, unapplied, human-approved disposition set.");
        if (disposition is not null)
        {
            if (Sha(original) != disposition.BrdSha256 || Sha(original) != source.TaskDigest
                || envelope!.CanonicalTaskPath != relative || envelope.CanonicalTaskDigest != source.TaskDigest)
                diagnostics.Add("ERROR: The canonical BRD no longer matches the approved revision baseline.");
            if (disposition.ReviewProvider.Equals(source.Provider, StringComparison.OrdinalIgnoreCase))
                diagnostics.Add("ERROR: Revision and review providers must remain independent.");
            ValidateBrdDispositionSource(context, disposition, diagnostics);
        }
        if (diagnostics.Count > 0) return New(context, "invalid", run: retained, diagnostics: diagnostics);
        if (!ApprovedBrdDiffs.TryApply(original, disposition!, out var candidate, out var error))
            return New(context, "blocked", diagnostics: ["ERROR: " + error]);

        var newRunId = NewRunId();
        var lockPath = AcquireLock(context, ProductAuthoringChange, BrdRevisionTask, context.RepositoryId, newRunId, diagnostics);
        if (lockPath is null) return New(context, "locked", diagnostics: diagnostics);
        try
        {
            var workspace = CreateScratchWorkspace(context, newRunId,
                [relative, Relative(context.RepositoryPath, dispositionPath)], "CIS approved BRD diff recovery baseline", diagnostics);
            if (workspace is null) return New(context, "invalid-workspace", diagnostics: diagnostics);
            var candidatePath = Path.Combine(workspace, relative.Replace('/', Path.DirectorySeparatorChar));
            WriteAtomic(candidatePath, candidate);
            if (!TryReadChangedFiles(workspace, out var changes))
                return New(context, "invalid-workspace", diagnostics: ["ERROR: Recovery workspace changed-file inventory could not be verified."]);
            var now = UtcNow();
            // Preserve the original producer for independent closure review; the distinct
            // transport and event explicitly record that this recovery invokes no provider.
            var manifest = source with
            {
                RunId = newRunId, Attempt = 1, WorkingDirectory = workspace, IsolatedWorktree = true,
                Transport = "approved-diff-recovery", ProviderProtocol = "local-exact-diff-v1", ProviderSessionId = null,
                ProcessId = null, ProcessStartedAtUtc = null, ExecutablePath = null, ExecutableDigest = null,
                StartedAtUtc = now, UpdatedAtUtc = now, CompletedAtUtc = now, Status = CisAgentRunStates.Succeeded,
                FailureKind = null, Actor = actor.Trim(), ResultDigest = null,
            };
            InitializeRun(context, manifest);
            var accepted = disposition!.Findings.Where(item => item.Decision == "accepted").Select(item => item.Id).Order(StringComparer.Ordinal).ToArray();
            var result = new AgentResultDocument(2, envelope!.Id, CisAgentRunStates.Succeeded,
                $"Recovered {accepted.Length} approved findings locally from retained run {runId}; no provider was executed.",
                changes,
                ["Every approved diff matched exactly one location in the approved baseline; original context and mixed line endings were preserved.",
                 "Only reversible OEM-437 corruption of UTF-8 punctuation was decoded; approval records were not rewritten."],
                [$"Original run: {runId}; result digest: {source.ResultDigest}.", $"Recovery reason: {reason.Trim()}"],
                Revision: new(reviewRunId!, accepted));
            var resultPath = Path.Combine(RunPath(context, newRunId), "result.json");
            WriteAtomic(resultPath, JsonSerializer.Serialize(result, JsonOptions));
            manifest = manifest with { ResultDigest = ShaFile(resultPath) };
            WriteManifest(context, manifest);
            AppendEvent(context, manifest, new("local-recovery", result.Summary));
            WriteArtifactInventory(context, newRunId);
            var executed = New(context, "succeeded", envelope, run: ReadRun(context, newRunId, diagnostics), diagnostics: diagnostics);
            return ApplyBrdRevisionResult(context, executed, target, relative, original, dispositionPath, disposition);
        }
        finally { ReleaseLock(lockPath); }
    }
}
