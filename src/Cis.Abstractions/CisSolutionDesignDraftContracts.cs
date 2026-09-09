namespace Cis.Abstractions;

public sealed record CisSolutionDesignDraftResult(IReadOnlyList<string> Errors, bool Applied = false);

/// <summary>Owns preparation and atomic application of review-only architecture inference.</summary>
public interface ICisSolutionDesignDrafts
{
    CisSolutionDesignDraftResult PrepareExistingDraft(string workspacePath);
    CisSolutionDesignDraftResult ApplyExistingDraft(string workspacePath, string originalDesign,
        string originalComponents, string proposedDesign, string proposedComponents);
}
