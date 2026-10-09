namespace Cis.Abstractions;

/// <summary>A source-backed compatibility assessment; reconciliation never grants approval.</summary>
public sealed record CisFeatureReconciliationReview(
    int SchemaVersion,
    string ExpectedFeatureSha256,
    string PreviousProductDefinitionHash,
    string ExpectedProductDefinitionHash,
    CisFeatureReconciliationArtifact Comparison,
    IReadOnlyList<CisFeatureReconciliationComparison> Artifacts);

public sealed record CisFeatureReconciliationArtifact(string Path, string Sha256);

public sealed record CisFeatureReconciliationComparison(
    string Path,
    string CurrentSha256,
    CisFeatureReconciliationArtifact Prior,
    string Assessment,
    string Reason);
