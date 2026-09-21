using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    private static CisFeatureDeliveryReview CheckDeliveryReview(WizardState state, CisFeatureWizardAnswer answer)
    {
        var request = answer.DeliveryReview!;
        if (answer.Page != "delivery" || answer.Answers.Count > 0 || answer.RepositoryWork is not null || answer.ScreenReview is not null)
            throw new InvalidDataException("Save a story decision separately from page answers and repository work.");
        if (request.Treatment is not ("reuse" or "extend" or "new" or "out-of-scope")
            || !HasAnswer(request.Plan) || request.Plan.Length > 4000 || request.Plan.Contains('\0') || request.Plan.Contains("<!--", StringComparison.Ordinal)
            || request.Owners is null || request.Owners.Count > 8 || request.Owners.Count != request.Owners.Distinct().Count()
            || (request.Treatment != "out-of-scope" && request.Owners.Count == 0)
            || request.EvidencePaths is null || request.EvidencePaths.Count > 12 || request.EvidencePaths.Any(p => p is null || p.Length > 600))
            throw new InvalidDataException("Choose a delivery treatment, owning repositories and a bounded description of the planned work.");
        var input = ReadDeliveryInput(state);
        if (request.ExpectedInputHash != input.Hash) throw new InvalidDataException("Code or saved direction changed. Refresh the story assessment before saving your decision.");
        var story = input.Drafts.SingleOrDefault(d => d.Id == request.StoryId)
            ?? throw new InvalidDataException("This story is no longer in the current feature BRD. Refresh before saving.");
        if (request.Owners.Any(id => !input.Repositories.Any(r => r.Id == id)))
            throw new InvalidDataException("Delivery owners must be registered product-owned participant repositories.");
        var evidence = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in request.EvidencePaths.Distinct(StringComparer.Ordinal))
        {
            var (repository, absolute) = DeliveryReference(state, path);
            if (!request.Owners.Contains(repository.Id)) throw new InvalidDataException("An implementation reference must belong to one of the selected owners.");
            evidence[path] = FileHash(absolute);
        }
        if (request.Treatment is "reuse" or "extend" && evidence.Count == 0)
            throw new InvalidDataException("Select supporting code or enter a repository-relative code path before recording reuse or extension. New work does not require an existing implementation reference.");
        return new(story.Id, story.Story.Title, request.Treatment, request.Owners, request.Plan.Trim(), evidence,
            input.Hash, answer.Actor.Trim(), DateTimeOffset.UtcNow.ToString("O"));
    }

    private static (CisWorkspaceRepository Repository, string Absolute) DeliveryReference(WizardState state, string reference)
    {
        var separator = reference.IndexOf('/');
        var repository = separator < 1 ? null : state.Workspace.Repositories.FirstOrDefault(r => r.Id == reference[..separator] && r.IsProductOwned && r.Role == "participant");
        if (repository is null || !CisPathSafety.TryResolveUnderRoot(repository.RepositoryPath, reference[(separator + 1)..], out var path)
            || !SafeAbsolutePath(path) || !File.Exists(path) || !DeliveryExtensions.Contains(Path.GetExtension(path)))
            throw new InvalidDataException("Use an existing code path in the form repository-id/path/to/file. Paths outside owned repositories are not accepted.");
        return (repository, path);
    }

    private static CisFeatureDeliveryResult ProjectDelivery(WizardState state, DeliveryInput input, CisFeatureDeliveryResult result)
    {
        var stories = result.Stories.Select(story =>
        {
            var review = state.Review.DeliveryReviews?.LastOrDefault(r => r.StoryId == story.Id);
            var current = review?.InputHash == input.Hash;
            if (current)
                foreach (var reference in review!.EvidenceHashes)
                {
                    try { if (FileHash(DeliveryReference(state, reference.Key).Absolute) != reference.Value) current = false; }
                    catch (InvalidDataException) { current = false; }
                }
            return story with { Review = review, ReviewCurrent = current,
                RequirementChecks = story.Requirements.Select((requirement, index) => new CisFeatureDeliveryRequirementCheck(index + 1, requirement,
                    input.Evidence.Where(e => e.StoryIds.Contains(story.Id) && e.RequirementNumbers.Contains(index + 1)).Select(e => e.Id).ToArray())).ToArray() };
        }).ToArray();
        var warnings = result.Warnings.ToList();
        if (stories.Any(s => s.Review is not null && !s.ReviewCurrent))
            warnings.Add("Some saved story decisions refer to earlier evidence. Review their retained choices against the current requirements and code, then save again.");
        var answers = result.Status == "current" || stories.Any(s => s.ReviewCurrent)
            ? DeliveryAnswers(input, stories, warnings) : new Dictionary<string, string>();
        return result with { Stories = stories, SuggestedAnswers = answers, Warnings = warnings.Distinct().ToArray(),
            FeatureRepositoryId = input.Repositories.FirstOrDefault(r => SamePath(r.RepositoryPath, state.Record.Plan.RepositoryPath))?.Id,
            RepositoryIds = input.Repositories.Select(r => r.Id).ToArray() };
    }
}
