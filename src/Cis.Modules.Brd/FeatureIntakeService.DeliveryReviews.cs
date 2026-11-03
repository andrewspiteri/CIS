using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    private CisFeatureDeliveryReview CheckDeliveryReview(WizardState state, CisFeatureWizardAnswer answer)
    {
        var request = answer.DeliveryReview!;
        if (answer.Page != "delivery" || answer.Answers.Count > 0 || answer.RepositoryWork is not null || answer.ScreenReview is not null)
            throw new InvalidDataException("Save a story decision separately from page answers and repository work.");
        if (request.Treatment is not ("reuse" or "extend" or "new" or "out-of-scope")
            || !HasAnswer(request.Plan) || request.Plan.Length > 4000 || request.Plan.Contains('\0') || request.Plan.Contains("<!--", StringComparison.Ordinal)
            || request.Owners is null || request.Owners.Count > 8 || request.Owners.Count != request.Owners.Distinct().Count()
            || request.EvidencePaths is null || request.EvidencePaths.Count > 12 || request.EvidencePaths.Any(p => p is null || p.Length > 600))
            throw new InvalidDataException("Choose a delivery treatment, zero or more linked repositories and a bounded description of the planned work.");
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
            var inventoried = input.Files.FirstOrDefault(file => SamePath(file.Absolute, absolute));
            if (inventoried is null)
                throw new InvalidDataException("Select code in the supported implementation inventory. Excluded scripts, tests, migrations and generated paths cannot supply reviewed dependency coverage.");
            evidence[inventoried.RepositoryId + "/" + inventoried.Path] = FileHash(inventoried.Absolute);
        }
        if (request.Treatment is "reuse" or "extend" && evidence.Count == 0)
            throw new InvalidDataException("Select supporting code or enter a repository-relative code path before recording reuse or extension. New work does not require an existing implementation reference.");
        var review = new CisFeatureDeliveryReview(story.Id, story.Story.Title, request.Treatment, request.Owners.Order(StringComparer.Ordinal).ToArray(), request.Plan.Trim(), evidence,
            input.Hash, answer.Actor.Trim(), DateTimeOffset.UtcNow.ToString("O"));
        // Compute the prospective selection with the dependency roots used after
        // persistence. Preserve the original full input token for the existing
        // concurrent-write check rather than rebinding it to this new selection.
        var reviewedState = state with { Review = state.Review with { DeliveryReviews = [.. state.Review.DeliveryReviews ?? [], review] } };
        var reviewedInput = ReadDeliveryInput(reviewedState);
        review = review with { Basis = DeliveryReviewBasis(reviewedInput, story.Id, request.Owners, evidence) };
        var previous = state.Review.DeliveryReviews?.LastOrDefault(item => item.StoryId == story.Id);
        return DeliveryReviewEquivalent(previous, review) ? previous! : review;
    }

    private static IReadOnlyDictionary<string, string> DeliveryReviewBasis(DeliveryInput input, string storyId,
        IReadOnlyList<string> owners, IReadOnlyDictionary<string, string> evidence)
    {
        var basis = new SortedDictionary<string, string>(input.MaterialBasisByStory[storyId].ToDictionary(item => item.Key, item => item.Value), StringComparer.Ordinal);
        foreach (var id in owners) basis["owner/" + id] = DeliveryRepositoryIdentity(input.Repositories.FirstOrDefault(repository => repository.Id == id));
        foreach (var item in evidence) basis["selected-evidence/" + item.Key] = item.Value;
        return basis;
    }

    private static bool DeliveryReviewEquivalent(CisFeatureDeliveryReview? previous, CisFeatureDeliveryReview current)
        => previous is not null && previous.StoryId == current.StoryId && previous.Title == current.Title && previous.Treatment == current.Treatment
            && previous.Owners.Order(StringComparer.Ordinal).SequenceEqual(current.Owners.Order(StringComparer.Ordinal))
            && previous.Plan == current.Plan && previous.Actor == current.Actor
            && previous.Basis is not null && current.Basis is not null && DeliveryBasisHash(previous.Basis) == DeliveryBasisHash(current.Basis);

    private static (CisWorkspaceRepository Repository, string Absolute) DeliveryReference(WizardState state, string reference)
    {
        var separator = reference.IndexOf('/');
        var repository = separator < 1 ? null : state.Workspace.Repositories.FirstOrDefault(r => r.Id == reference[..separator] && r.IsProductOwned && r.Role == "participant");
        var segments = reference[(separator + 1)..].Replace('\\', '/').Split('/');
        var relative = string.Join('/', segments.Where(segment => segment != "."));
        if (repository is null || segments.Any(segment => segment is ".." or "")
            || !CisPathSafety.TryResolveUnderRoot(repository.RepositoryPath, relative, out var path)
            || !SafeAbsolutePath(path) || !File.Exists(path) || !DeliveryExtensions.Contains(Path.GetExtension(path)))
            throw new InvalidDataException("Use an existing code path in the form repository-id/path/to/file. Paths outside owned repositories are not accepted.");
        return (repository, path);
    }

    private static CisFeatureDeliveryResult ProjectDelivery(WizardState state, DeliveryInput input, CisFeatureDeliveryResult result)
    {
        var stories = result.Stories.Select(story =>
        {
            var review = state.Review.DeliveryReviews?.LastOrDefault(r => r.StoryId == story.Id);
            var current = false;
            var reason = "";
            if (review is not null)
            {
                var evidence = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var reference in review.EvidenceHashes)
                {
                    try { evidence[reference.Key] = FileHash(DeliveryReference(state, reference.Key).Absolute); }
                    catch (InvalidDataException) { evidence[reference.Key] = "missing"; }
                }
                var basis = DeliveryReviewBasis(input, story.Id, review.Owners, evidence);
                current = review.Basis is not null ? DeliveryBasisHash(review.Basis) == DeliveryBasisHash(basis)
                    : review.InputHash == input.Hash && review.EvidenceHashes.All(item => evidence.GetValueOrDefault(item.Key) == item.Value);
                reason = current ? "The story, ownership, relevant implementation and dependencies match the saved review basis."
                    : review.Basis is null ? "This retained decision predates scoped review bases; current compatibility cannot be established. Renew this story decision."
                    : DeliveryBasisChange(review.Basis, basis);
                if (input.BasisReasonsByStory.TryGetValue(story.Id, out var unknown))
                    reason = current ? "The saved human decision applies to the unchanged reviewed basis. Implementation coverage remains uncertain: " + unknown
                        : reason + " Implementation coverage remains uncertain: " + unknown;
                if (input.UnboundBasisReasonsByStory.TryGetValue(story.Id, out var unbound)) { current = false; reason = unbound; }
            }
            return story with
            {
                Review = review,
                ReviewCurrent = current,
                ReviewState = review is null ? "not-reviewed" : current ? "current"
                : input.UnboundBasisReasonsByStory.ContainsKey(story.Id) ? "unknown" : "needs-review",
                ReviewReason = reason,
                AssessmentState = input.BasisReasonsByStory.ContainsKey(story.Id) ? "unknown" : story.AssessmentState,
                AssessmentReason = input.BasisReasonsByStory.GetValueOrDefault(story.Id) ?? story.AssessmentReason,
                RequirementChecks = story.Requirements.Select((requirement, index) => new CisFeatureDeliveryRequirementCheck(index + 1, requirement,
                    input.Evidence.Where(e => e.StoryIds.Contains(story.Id) && e.RequirementNumbers.Contains(index + 1)).Select(e => e.Id).ToArray())).ToArray()
            };
        }).ToArray();
        var warnings = result.Warnings.ToList();
        if (stories.Any(story => story.Review is not null && !story.ReviewCurrent && !input.UnboundBasisReasonsByStory.ContainsKey(story.Id)))
            warnings.Add("Some saved story decisions have a changed material basis. Review each named change and renew only the affected choice; bounded coverage uncertainty remains visible.");
        if (stories.Any(story => story.Review is not null && !story.ReviewCurrent && input.UnboundBasisReasonsByStory.ContainsKey(story.Id)))
            warnings.Add("Some saved story decisions have unbounded or unavailable dependency coverage. Resolve the affected story's coverage reason before renewing its decision.");
        var answers = result.Status == "current" || stories.Any(s => s.ReviewCurrent)
            ? DeliveryAnswers(input, stories, warnings) : new Dictionary<string, string>();
        return result with
        {
            Stories = stories,
            SuggestedAnswers = answers,
            Warnings = warnings.Distinct().ToArray(),
            FeatureRepositoryId = input.Repositories.FirstOrDefault(r => SamePath(r.RepositoryPath, state.Record.Plan.RepositoryPath))?.Id,
            RepositoryIds = input.Repositories.Select(r => r.Id).ToArray()
        };
    }

    private static string DeliveryBasisChange(IReadOnlyDictionary<string, string> previous, IReadOnlyDictionary<string, string> current)
    {
        var changed = previous.Keys.Concat(current.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Where(key => previous.GetValueOrDefault(key) != current.GetValueOrDefault(key)).Take(4).ToArray();
        return "The saved decision needs review because its material basis changed: " + string.Join(", ", changed) + ".";
    }

    private static CisFeatureDeliveryResult CarryDeliveryAssessments(DeliveryInput input, CisFeatureDeliveryResult? cached, CisFeatureDeliveryResult fallback,
        ISet<string>? retainedStories = null)
    {
        if (cached is null) return fallback;
        var retained = 0;
        var stories = fallback.Stories.Select(story =>
        {
            var previous = cached.Stories.FirstOrDefault(item => item.Id == story.Id);
            input.MaterialBasisByStory.TryGetValue(story.Id, out var basis);
            if (previous?.AssessmentBasis is null || basis is null
                || DeliveryBasisHash(previous.AssessmentBasis) != DeliveryBasisHash(basis) || input.UnboundBasisReasonsByStory.ContainsKey(story.Id))
                return story with
                {
                    AssessmentReason = previous is null ? story.AssessmentReason : previous.AssessmentBasis is null
                    ? "The earlier assessment has no scoped material basis. Review this story against its current evidence."
                    : input.BasisReasonsByStory.GetValueOrDefault(story.Id) ?? DeliveryBasisChange(previous.AssessmentBasis, basis!)
                };
            var references = previous.EvidenceIds.Select(id => cached.Evidence.FirstOrDefault(item => item.Id == id)).OfType<CisFeatureDeliveryEvidence>().ToArray();
            var ids = references.Select(reference => input.Evidence.FirstOrDefault(item => item.RepositoryId == reference.RepositoryId
                && item.Path == reference.Path && item.ContentHash == reference.ContentHash && item.StoryIds.Contains(story.Id))?.Id).OfType<string>().ToArray();
            if (ids.Length != references.Length) return story;
            retained++;
            retainedStories?.Add(story.Id);
            return previous with
            {
                EvidenceIds = ids,
                AssessmentBasis = basis,
                AssessmentProvider = previous.AssessmentProvider ?? cached.Provider,
                AssessmentModel = previous.AssessmentModel ?? cached.Model
            };
        }).ToArray();
        var warnings = fallback.Warnings.Where(warning => warning != DeliveryUnresolvedWarning).ToList();
        if (stories.Any(story => story.Treatment is "unresolved" or "conflict")) warnings.Add(DeliveryUnresolvedWarning);
        return fallback with
        {
            Stories = stories,
            Warnings = warnings,
            Status = stories.Length > 0 && retained == stories.Length ? "current" : fallback.Status,
            Cached = stories.Length > 0 && retained == stories.Length,
            Provider = cached.Provider,
            Model = cached.Model
        };
    }
}
