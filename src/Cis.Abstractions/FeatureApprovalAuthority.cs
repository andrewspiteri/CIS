namespace Cis.Abstractions;

public sealed record CisFeatureApproval(
    bool Applicable,
    bool Ready,
    string? ItemId,
    string? FeaturePath,
    string? Reviewer,
    string? Rationale,
    string? ApprovedContentHash,
    IReadOnlyList<string> Errors);

/// <summary>
/// Resolves whether a canonical feature specification carries current human authority
/// that deterministic downstream transformations may reuse without another approval.
/// </summary>
public interface ICisFeatureApprovalAuthority
{
    CisFeatureApproval Evaluate(string repositoryPath, string featureSpecificationPath);
}
