using Cis.Abstractions;
using Cis.Modules.Repository;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    private IReadOnlyList<CisStoryCompletionContext> StoryCompletion(WizardState state, DeliveryInput input,
        CisFeatureStoryResult result, string taskId, bool prepare)
    {
        var task = result.Tasks.Single(item => item.Id == taskId);
        var participantIds = task.RepositoryIds.Count == 0 ? [state.Authority.Id] : task.RepositoryIds;
        var repositories = new List<CisWorkspaceRepository>();
        var missing = new List<CisStoryCompletionContext>();
        foreach (var id in participantIds)
        {
            var matches = state.Workspace.Repositories.Where(item => item.Id == id && item.IsProductOwned).ToArray();
            if (matches.Length != 1)
                missing.Add(new(id, "", null, ["Story completion requires exactly one registered product-owned participant: " + id]));
            else repositories.Add(matches[0]);
        }
        if (missing.Count > 0) return missing;
        if (storyCompletion is null)
        {
            return repositories.Select(repository => new EngineeringAssessmentService().Assess(
                new(repository.RepositoryPath, repository.Id, repository.DocumentationRoot,
                    Path.Combine(repository.RepositoryPath, repository.DocumentationRoot),
                    Path.Combine(repository.RepositoryPath, repository.DocumentationRoot, "catalog.yml")), "implementation").Adopted
                ? new CisStoryCompletionContext(repository.Id, "", null,
                    ["The engineering completion service is unavailable; adopted story completion cannot bypass verification."])
                : new CisStoryCompletionContext(repository.Id, "", null, []) { AdoptionStatus = "not-adopted" }).ToArray();
        }
        var work = new CisStoryTaskWork(state.Workspace.WorkspacePath, result.Slug, result.StoryId, result.PlanHash!, result.Definition,
            task, task.RequirementNumbers.Select(number => result.Story!.Requirements[number - 1]).ToArray(), repositories, "")
        { Direction = input.Direction, Constraints = input.Constraints };
        var contexts = storyCompletion.Assess(work, prepare);
        if (!contexts.Select(context => context.RepositoryId).Order(StringComparer.Ordinal)
            .SequenceEqual(participantIds.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            return [.. contexts, new(state.Authority.Id, "", null,
                ["Engineering completion must return exactly one result for every declared participant."])];
        return contexts;
    }
}
