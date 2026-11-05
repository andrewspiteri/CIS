using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed partial class AgentService
{
    private string? RetainedStoryReportTarget(CisRepositoryContext context, CisStoryTaskWork work, StoryWorkBinding binding)
    {
        var contract = CisStoryEngineeringContract.Digest(work);
        var manifest = ReadRunManifests(context).Where(run => run.Mode == "implement"
            && run.Status == CisAgentRunStates.Succeeded && run.TargetRepositoryId == "task-report"
            && run.ChangeId == "STORY-" + work.StoryId && run.TaskId == work.Task.Id + "-IMPLEMENT"
            && run.TaskContractDigest == contract && (run.StoryRelationshipId == binding.RelationshipId || run.StoryRelationshipId is null))
            .OrderByDescending(run => run.StartedAtUtc, StringComparer.Ordinal).FirstOrDefault();
        if (manifest is null) return null;
        var retained = Show(context.RepositoryPath, manifest.RunId);
        var workspaceRoot = Path.Combine(context.RepositoryPath, RootPath, "workspaces");
        if (manifest.AuthorityRepositoryId != context.RepositoryId || retained.Run?.Result is null
            || retained.Diagnostics.Any(message => message.StartsWith("ERROR:", StringComparison.Ordinal))
            || retained.Run.Artifacts.Count == 0 || retained.Run.Artifacts.Any(artifact => !artifact.Valid)
            || !CisPathSafety.IsUnderRoot(workspaceRoot, manifest.TargetRepositoryPath)
            || CisPathSafety.ContainsReparsePoint(context.RepositoryPath, manifest.TargetRepositoryPath)
            || !Directory.Exists(manifest.TargetRepositoryPath) || !IsGitRepository(manifest.TargetRepositoryPath))
            throw new InvalidDataException("The retained repository-free report target cannot be reused safely. Inspect native run " + manifest.RunId + ".");
        return manifest.TargetRepositoryPath;
    }

    private StoryCandidate? RecoverStoryCandidate(CisRepositoryContext context, CisStoryTaskWork work,
        CisStoryExecutionPlan selection, CisWorkspaceRepository target, string preparedPath, string baseline,
        List<string> diagnostics, StoryWorkBinding binding)
    {
        var previous = work.PreviousExecution;
        if (previous?.Applied == true) return null;
        var compatiblePrevious = previous is not null
            && previous.Status is "failed" or "cancelled" or "review-failed" or "blocked" or "changes-requested" or "held" or "running";
        var saved = compatiblePrevious ? previous!.Runs.LastOrDefault(run => run.RepositoryId == target.Id && run.Stage == "Implementation"
            && run.Status == CisAgentRunStates.Succeeded) : null;
        // A stopped controller may have persisted a completed stage before the story result was
        // saved. Recover its original evidence by stable relationship, never a new stage allowance.
        var durable = ReadRunManifests(context).Where(run => (run.ExecutionRoute == "story-task" && run.StoryRelationshipId == binding.RelationshipId
                || run.StoryRelationshipId is null && run.ChangeId == "STORY-" + work.StoryId && run.TaskId == work.Task.Id + "-IMPLEMENT")
            && run.TargetRepositoryId == target.Id
            && run.TaskContractDigest == CisStoryEngineeringContract.Digest(work) && run.Mode == "implement"
            && run.Status == CisAgentRunStates.Succeeded).OrderByDescending(run => run.StartedAtUtc, StringComparer.Ordinal).FirstOrDefault();
        if (durable is not null && (saved is null || StringComparer.Ordinal.Compare(durable.StartedAtUtc,
            ReadManifest(context, saved.RunId, diagnostics)?.StartedAtUtc) > 0))
            saved = new(durable.RunId, target.Id, "Implementation", durable.Provider, durable.Model!, durable.Status,
                durable.WorkingDirectory, "Retained story implementation", [])
            { CorrectionRoundId = durable.StoryCorrectionRoundId };
        if (saved is null) return null;
        var retained = Show(context.RepositoryPath, saved.RunId);
        var run = retained.Run;
        if (run is not null && !string.IsNullOrWhiteSpace(run.Manifest.TaskContractDigest)
            && run.Manifest.TaskContractDigest != CisStoryEngineeringContract.Digest(work)
            && run.Manifest.ChangeId == "STORY-" + work.StoryId && run.Manifest.TaskId == work.Task.Id + "-IMPLEMENT"
            && run.Manifest.AuthorityRepositoryId == context.RepositoryId && run.Manifest.TargetRepositoryId == target.Id
            && run.Manifest.Status == CisAgentRunStates.Succeeded && run.Manifest.Mode == "implement"
            && run.Artifacts.Count > 0 && run.Artifacts.All(artifact => artifact.Valid)
            && !retained.Diagnostics.Any(message => message.StartsWith("ERROR:", StringComparison.Ordinal)))
            return null; // A newly approved contract may start work without refunding this old run or its rounds.
        var sameContract = run is not null && (!string.IsNullOrWhiteSpace(run.Manifest.TaskContractDigest)
            ? run.Manifest.TaskContractDigest == CisStoryEngineeringContract.Digest(work)
            : run.Manifest.TaskDigest == work.PlanHash && run.Manifest.AcceptedScopeDigest == work.PlanHash);
        bool SamePath(string left, string right) => Path.GetFullPath(left).Equals(Path.GetFullPath(right), CisPathSafety.PlatformComparison);
        var expectedPath = Path.Combine(context.RepositoryPath, RootPath, "worktrees", SafeFile(saved.RunId), SafeFile(target.Id));
        if (retained.Diagnostics.Any(message => message.StartsWith("ERROR:", StringComparison.Ordinal)) || run?.Result is null
            || run.Manifest.Status != CisAgentRunStates.Succeeded || run.Manifest.ChangeId != "STORY-" + work.StoryId
            || run.Manifest.TaskId != work.Task.Id + "-IMPLEMENT" || !sameContract || run.Manifest.TargetRepositoryId != target.Id
            || run.Manifest.AuthorityRepositoryId != context.RepositoryId || run.Manifest.Mode != "implement"
            || run.Manifest.Permission != "workspace-write" || !run.Manifest.IsolatedWorktree
            || !SamePath(run.Manifest.TargetRepositoryPath, target.RepositoryPath)
            || !SamePath(run.Manifest.WorkingDirectory, expectedPath) || !Directory.Exists(expectedPath)
            || CisPathSafety.ContainsReparsePoint(context.RepositoryPath, expectedPath)
            || run.Artifacts.Count == 0 || run.Artifacts.Any(artifact => !artifact.Valid))
            throw new InvalidDataException("Retained implementation evidence cannot be reused safely for " + target.Id + ". Inspect run " + saved.RunId + ".");
        if (StoryGit(expectedPath, ["rev-parse", "HEAD"]).Trim() != run.Manifest.RepositoryRevision
            || !SamePath(StoryGit(expectedPath, ["rev-parse", "--path-format=absolute", "--git-common-dir"]).Trim(),
                StoryGit(preparedPath, ["rev-parse", "--path-format=absolute", "--git-common-dir"]).Trim())
            || StoryGit(expectedPath, ["rev-parse", run.Manifest.RepositoryRevision + "^{tree}"]).Trim()
                != StoryGit(preparedPath, ["rev-parse", baseline + "^{tree}"]).Trim())
            throw new InvalidDataException("The repository baseline changed since the retained implementation for " + target.Id + ". Its work remains available in run " + saved.RunId + ".");
        if (!TryReadChangedFiles(expectedPath, out var changes)
            || !changes.Order(StringComparer.Ordinal).SequenceEqual(run.Result.ChangedFiles.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("The retained implementation's file set changed for " + target.Id + ". Inspect run " + saved.RunId + " before continuing.");
        // Freeze the retained files and subject them to a fresh independent review.
        // Prior completion claims never grant approval or bypass copy-back checks.
        var candidate = CreateIsolatedSnapshot(context, target with { RepositoryPath = expectedPath }, NewRunId(), diagnostics);
        return candidate is null ? null : new(target, expectedPath, run.Manifest.RepositoryRevision, candidate, retained);
    }
}
