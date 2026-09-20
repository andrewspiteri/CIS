using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    public CisFeatureScreensResult Screens(string workspacePath, string slug, bool prepare, string? expectedRevision = null, string? screenKey = null)
    {
        try
        {
            var state = ReadWizard(workspacePath, slug);
            if (prepare && expectedRevision != ProjectWizard(state).Revision)
                throw new InvalidDataException("The feature changed. Refresh before generating screens.");
            var generator = screenGenerators?.SingleOrDefault()
                ?? throw new InvalidDataException("Load the CIS design module to generate feature screens.");
            var input = ScreenInput(state) with { TargetScreenKey = screenKey };
            return generator.Run(input, prepare, () => {
                var current = ScreenInput(ReadWizard(workspacePath, slug));
                return current.InputHash == input.InputHash && JsonSerializer.Serialize(current.Reviews, Json) == JsonSerializer.Serialize(input.Reviews, Json);
            });
        }
        catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        { return new("invalid", null, [], [], [e.Message]); }
    }

    private static CisFeatureScreenInput ScreenInput(WizardState state)
    {
        var answers = state.Review.Pages.GetValueOrDefault("experience")?.Answers ?? [];
        var direction = string.Join("\n\n", answers.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + ":\n" + pair.Value));
        var uiPath = Path.Combine(state.Authority.RepositoryPath, state.Authority.DocumentationRoot, "design/ui-direction.md");
        var baseline = SafeAbsolutePath(uiPath) && File.Exists(uiPath) ? File.ReadAllText(uiPath) : "";
        // Only substantive inputs affect currency: saving another page or the same answer does not stale images.
        var hash = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            state.Record.Plan.SourceHash, state.Record.Plan.Title, direction, baseline,
            Boundary = state.Workspace.Repositories.Select(repo => new { repo.Id, repo.RepositoryPath, repo.Participation }).OrderBy(repo => repo.Id),
        }, Json)));
        return new(state.Authority.RepositoryPath, state.Record.Plan.Slug, state.Record.Plan.Title,
            hash, state.Source, direction) { ExperienceAnswers = answers, Reviews = state.Review.ScreenReviews ?? [] };
    }

    private CisFeatureScreenReview CheckScreenReview(WizardState state, CisFeatureWizardAnswer answer)
    {
        var request = answer.ScreenReview!;
        if (answer.Page != "experience" || answer.Answers.Count != 0 || answer.RepositoryWork is not null
            || request.Decision is not ("not-needed" or "amend" or "restore") || request.Feedback is null
            || request.Feedback.Length > 2000 || request.Feedback.Contains('\0') || request.Feedback.Contains("<!--", StringComparison.Ordinal)
            || request.Decision == "amend" && string.IsNullOrWhiteSpace(request.Feedback))
            throw new InvalidDataException("Choose Not needed, Restore or describe the requested screen changes (up to 2,000 characters).");
        if (state.Review.ScreenReviews?.Count >= 200) throw new InvalidDataException("This feature has reached the screen review history limit.");
        var input = ScreenInput(state);
        var result = screenGenerators?.SingleOrDefault()?.Run(input, false, () => true)
            ?? throw new InvalidDataException("Load the design module to review screens.");
        var screen = result.Screens.SingleOrDefault(screen => screen.Plan.Id == request.ScreenId);
        var excluded = input.Reviews.LastOrDefault(review => review.Key == request.ScreenId);
        if (screen is null && excluded?.Decision == "not-needed" && request.Decision == "restore" && request.ScreenRevision == excluded.ScreenRevision)
            return excluded with { Id = Guid.NewGuid().ToString("N"), Decision = "restore", Feedback = request.Feedback.Trim(),
                Actor = answer.Actor.Trim(), SavedAt = DateTimeOffset.UtcNow.ToString("O"), SourceHash = state.Record.Plan.SourceHash };
        if (result.Errors.Count > 0 || result.Status is "stale" or "missing" || result.InputHash != request.PreviewHash
            || screen is null || screen.Revision != request.ScreenRevision)
            throw new InvalidDataException("This screen changed or its context is stale. Refresh or regenerate the previews before reviewing; your feedback has been kept.");
        if (!SingleLine(screen.Plan.Title, 200) || screen.Plan.Title.Contains("<!--", StringComparison.Ordinal))
            throw new InvalidDataException("The screen title is invalid. Regenerate this preview before reviewing it.");
        return new(Guid.NewGuid().ToString("N"), screen.Key, screen.Plan.Title, screen.Plan.Evidence, screen.Plan.FrontendType,
            request.Decision, request.Feedback.Trim(), answer.Actor.Trim(), DateTimeOffset.UtcNow.ToString("O"), state.Record.Plan.SourceHash, screen.Revision);
    }
}
