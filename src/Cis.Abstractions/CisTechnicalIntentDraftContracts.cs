namespace Cis.Abstractions;

public sealed record CisTechnicalIntentDraftPreparation(IReadOnlyList<string> Errors);

/// <summary>Prepares a review-only existing-system draft without granting upstream or technical approval.</summary>
public interface ICisTechnicalIntentDraftPreparer
{
    CisTechnicalIntentDraftPreparation PrepareExistingDraft(string workspacePath);
}
