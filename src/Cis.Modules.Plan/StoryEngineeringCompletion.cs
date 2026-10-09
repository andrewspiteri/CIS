using Cis.Abstractions;

namespace Cis.Modules.Plan;

/// <summary>Applies the shared completion contract independently to every owned story participant.</summary>
public sealed class StoryEngineeringCompletion(ICisRepositoryContextResolver resolver, ICisEngineeringAssessment assessment,
    ICisGraphSnapshotReader graph, IEnumerable<ICisRepositoryDoctorCheck> checks) : ICisStoryEngineeringCompletion
{
    public IReadOnlyList<CisStoryCompletionContext> Assess(CisStoryTaskWork work, bool prepare)
    {
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
                var required = assessment.Assess(context, "implementation");
                if (!required.Adopted)
                {
                    result.Add(new(repository.Id, "", null, []) { AdoptionStatus = "not-adopted" });
                    continue;
                }
                if (!CisPathSafety.TryResolveUnderRoot(repository.RepositoryPath, relative, out var receipt))
                    throw new InvalidDataException("Story completion evidence path is unsafe.");
                var policy = Path.Combine(repository.RepositoryPath, ".cis/engineering-defaults.json");
                var contract = CisStoryEngineeringContract.Digest(work);
                var task = new PlanWorkItem(work.Task.Id, "implementation", "low", null, null, work.Task.Title,
                    work.Task.RequirementNumbers.Select(number => $"{work.StoryId}:REQ-{number}").ToArray(), [], work.Task.DependsOn,
                    string.Join("\n", work.Task.AcceptanceCriteria), "Native scoped verification", "InProgress");
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
