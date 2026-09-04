namespace Cis.Abstractions;

public sealed record CisSourceEvidenceRegistrationRequest(
    string RepositoryPath,
    string SourcePath,
    string Assessment,
    string Actor,
    string Rationale);

public sealed record CisSourceEvidenceRegistration(
    string Status,
    string? SourceId,
    string? SourcePath,
    string? RegisteredDigest,
    string? DetectedDigest,
    string? RegistryPath,
    string? ProjectionPath,
    IReadOnlyList<string> Diagnostics)
{
    public int ExitCode => Diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal)) ? 4 : 0;
}

/// <summary>
/// Registers explicitly selected BRD authoring evidence without granting it
/// authority or changing the BRD lifecycle state.
/// </summary>
public interface ICisSourceEvidenceRegistrar
{
    CisSourceEvidenceRegistration Register(CisSourceEvidenceRegistrationRequest request);
}

public sealed record CisBrdSourceEvidence(
    string Id,
    string Kind,
    string RepositoryId,
    string RepositoryPath,
    string SourcePath,
    string RegisteredDigest,
    string? DetectedDigest,
    string Assessment,
    string Rationale,
    bool MaterialToBrd,
    IReadOnlyList<string> Signals);

/// <summary>
/// Allows modules to contribute stable source evidence to BRD discovery while
/// keeping the BRD module independent of the source format implementation.
/// </summary>
public interface ICisBrdSourceEvidenceProvider
{
    IReadOnlyList<CisBrdSourceEvidence> Discover(CisRepositoryContext context);
}

public sealed record CisBrdSourceEvidenceReconciliation(
    string Status,
    bool Applied,
    IReadOnlyList<string> Diagnostics);

public interface ICisBrdSourceEvidenceReconciler
{
    CisBrdSourceEvidenceReconciliation ReconcileSourceEvidence(string repositoryPath);
}
