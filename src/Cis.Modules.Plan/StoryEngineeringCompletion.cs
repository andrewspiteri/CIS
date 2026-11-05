using Cis.Abstractions;

namespace Cis.Modules.Plan;

/// <summary>Applies the shared completion contract independently to every owned story participant.</summary>
public sealed class StoryEngineeringCompletion(ICisRepositoryContextResolver resolver, ICisEngineeringAssessment assessment,
    ICisGraphSnapshotReader graph, IEnumerable<ICisRepositoryDoctorCheck> checks,
    ICisEngineeringTaskContractReader? taskContracts = null) : ICisStoryEngineeringCompletion
{
    public IReadOnlyList<CisStoryCompletionContext> Assess(CisStoryTaskWork work, bool prepare)
    {
        try { if (taskContracts is not null) work = work with { Task = taskContracts.ResolveStoryTask(work.WorkspacePath, work.Task) }; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        { return work.Repositories.Select(repository => new CisStoryCompletionContext(repository.Id, "", null, ["Story task contract resolution failed: " + error.Message])).ToArray(); }
        var result = new List<CisStoryCompletionContext>();
        var identities = new List<(CisRepositoryContext Context, string Digest, int ResultIndex)>();
        foreach (var repository in work.Repositories)
        {
            var relative = $".cis/local/engineering/story-completion/{work.Slug}/{work.StoryId}/{work.Task.Id}.json";
            try
            {
                if (!repository.IsProductOwned)
                    throw new InvalidDataException("Story completion participant is not product-owned.");
                var context = resolver.Resolve(repository.RepositoryPath).Context
                    ?? throw new InvalidDataException("Repository context is unavailable for story completion.");
                var gateErrors = taskContracts?.ValidateStoryTaskGate(work.WorkspacePath, work.ForCanonicalGate(), completing: true)
                    ?? (string.IsNullOrWhiteSpace(work.Task.ApprovalGate) || work.Task.ApprovalGate.Equals("none", StringComparison.OrdinalIgnoreCase)
                        ? [] : new[] { $"Task {work.Task.Id} requires approval gate '{work.Task.ApprovalGate}'; its existing approval authority is unavailable." });
                if (gateErrors.Count > 0)
                {
                    result.Add(new(repository.Id, relative, null, gateErrors));
                    continue;
                }
                var required = assessment.Assess(context, work.Task.Category ?? "unresolved");
                if (!required.Adopted)
                {
                    result.Add(new(repository.Id, "", null, []) { AdoptionStatus = "not-adopted" });
                    continue;
                }
                var contractErrors = CisStoryEngineeringContract.Validate(work.Task).ToList();
                if (contractErrors.Count == 0)
                    contractErrors.AddRange(taskContracts?.ValidateTaskType(work.WorkspacePath, work.Task.TaskTypeKey!, work.Task.TaskTypeVersion!, work.Task.Category!)
                        ?? ["Story task type validation is unavailable; load the existing planning task-type provider."]);
                if (contractErrors.Count > 0)
                {
                    result.Add(new(repository.Id, relative, null, contractErrors));
                    continue;
                }
                if (!CisPathSafety.TryResolveUnderRoot(repository.RepositoryPath, relative, out var receipt))
                    throw new InvalidDataException("Story completion evidence path is unsafe.");
                var policy = Path.Combine(repository.RepositoryPath, ".cis/engineering-defaults.json");
                var contract = CisStoryEngineeringContract.Digest(work);
                var task = new PlanWorkItem(work.Task.Id, work.Task.Category!, work.Task.Complexity!, null, null, work.Task.Title,
                    work.Task.RequirementNumbers.Select(number => $"{work.StoryId}:REQ-{number}").ToArray(), [], work.Task.DependsOn,
                    string.Join("\n", work.Task.AcceptanceCriteria), work.Task.Validation!, "InProgress",
                    work.Task.TaskTypeKey!, work.Task.TaskTypeVersion!, work.Task.ApprovalGate ?? "none", work.Task.Targets,
                    work.Task.DecisionIds, work.Task.NegativeCriteria, work.Task.RequiredOutputs, work.Task.FrontendType)
                {
                    StoryId = work.Task.StoryId ?? work.StoryId,
                    InstanceKey = work.Task.InstanceKey,
                    WorkOwnerTaskId = work.Task.WorkOwnerTaskId,
                    AcceptanceCriteriaItems = work.Task.AcceptanceCriteria.ToArray(),
                    VerificationObligations = work.Task.VerificationObligations
                };
                var identity = CisExecutionIdentity.Capture(context);
                identities.Add((context, identity, result.Count));
                var metadata = graph.ReadMetadata(repository.RepositoryPath);
                var errors = required.Diagnostics.ToList();
                string? template = null;
                if (prepare && errors.Count == 0)
                    template = EngineeringCompletionReview.Serialize(EngineeringCompletionReview.Template("STORY-" + work.StoryId,
                        task, policy, metadata, required, identity, taskContractDigest: contract));
                if (!prepare)
                {
                    var alignment = checks.Where(check => check.Name == "engineering-alignment").ToArray();
                    if (alignment.Length == 0) errors.Add("Story completion requires the engineering alignment checker.");
                    errors.AddRange(alignment.SelectMany(check => check.Inspect(context)).Where(finding => finding.Severity == "error").Select(finding => finding.Message));
                    errors.AddRange(EngineeringCompletionReview.Review(repository.RepositoryPath, receipt, "STORY-" + work.StoryId,
                        task, policy, metadata, required, identity, taskContractDigest: contract, reviewTaskId: work.Task.Id + "-REVIEW",
                        documentationRoot: context.DocumentationRoot, implementationTaskId: work.Task.Id + "-IMPLEMENT",
                        implementationRepository: resolver.Resolve(work.WorkspacePath).Context?.RepositoryPath
                            ?? throw new InvalidDataException("Story authority context is unavailable for implementation provenance.")));
                    if (CisExecutionIdentity.Capture(context) != identity) errors.Add("Repository inputs changed during story completion review.");
                }
                result.Add(new(repository.Id, relative, template, errors));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
            { result.Add(new(repository.Id, relative, null, [error.Message])); }
        }
        if (!prepare)
            foreach (var (context, digest, index) in identities)
            {
                try
                {
                    if (CisExecutionIdentity.Capture(context) != digest)
                        result[index] = result[index] with { Errors = [.. result[index].Errors, "A participant changed while other repositories were being reviewed. Refresh the complete closing evidence."] };
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
                { result[index] = result[index] with { Errors = [.. result[index].Errors, error.Message] }; }
            }
        return result;
    }
}
