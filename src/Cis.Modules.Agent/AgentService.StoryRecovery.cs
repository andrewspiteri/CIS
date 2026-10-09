using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed partial class AgentService
{
    private StoryCandidate? RecoverStoryCandidate(CisRepositoryContext context, CisStoryTaskWork work,
        CisStoryExecutionPlan selection, CisWorkspaceRepository target, string preparedPath, string baseline,
        List<string> diagnostics)
    {
        var previous = work.PreviousExecution;
        if (previous is null || previous.Applied || previous.Selection.Hash != selection.Hash
            || previous.Status is not ("failed" or "cancelled" or "review-failed" or "blocked" or "changes-requested")) return null;
        var saved = previous.Runs.LastOrDefault(run => run.RepositoryId == target.Id && run.Stage == "Implementation"
            && run.Status == CisAgentRunStates.Succeeded);
        if (saved is null) return null;
        var retained = Show(context.RepositoryPath, saved.RunId);
        var run = retained.Run;
        bool SamePath(string left, string right) => Path.GetFullPath(left).Equals(Path.GetFullPath(right), CisPathSafety.PlatformComparison);
        var expectedPath = Path.Combine(context.RepositoryPath, RootPath, "worktrees", SafeFile(saved.RunId), SafeFile(target.Id));
        if (retained.Diagnostics.Any(message => message.StartsWith("ERROR:", StringComparison.Ordinal)) || run?.Result is null
            || run.Manifest.Status != CisAgentRunStates.Succeeded || run.Manifest.ChangeId != "STORY-" + work.StoryId
            || run.Manifest.TaskId != work.Task.Id + "-IMPLEMENT" || run.Manifest.TaskDigest != work.PlanHash
            || run.Manifest.AcceptedScopeDigest != work.PlanHash || run.Manifest.TargetRepositoryId != target.Id
            || run.Manifest.AuthorityRepositoryId != context.RepositoryId || run.Manifest.Mode != "implement"
            || run.Manifest.Permission != "workspace-write" || !run.Manifest.IsolatedWorktree
            || run.Manifest.Provider != selection.Implementation!.Provider || run.Manifest.Model != selection.Implementation.Model
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
