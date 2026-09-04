namespace Cis.Modules.Ci;

public sealed record CiFailure(
    long RunId,
    long JobId,
    string Job,
    string Step,
    string Classification,
    string Summary,
    IReadOnlyList<string> ReproductionCommands,
    IReadOnlyList<string> Evidence);

public sealed record CiEvidence(
    string Kind,
    string Path,
    string Sha256,
    long Bytes,
    bool Truncated,
    string Redaction);

public sealed record CiInvestigationResult(
    string Status,
    string? RepositoryPath,
    string Provider,
    string? Target,
    IReadOnlyList<Cis.Abstractions.CisCiProviderAvailability> Availability,
    IReadOnlyList<Cis.Abstractions.CisCiCheck> Checks,
    IReadOnlyList<Cis.Abstractions.CisCiRun> Runs,
    IReadOnlyList<Cis.Abstractions.CisCiJob> Jobs,
    IReadOnlyList<Cis.Abstractions.CisCiArtifact> Artifacts,
    IReadOnlyList<CiFailure> Failures,
    IReadOnlyList<CiEvidence> Evidence,
    IReadOnlyList<string> Errors,
    bool Applied)
{
    public int ExitCode => Status is "invalid-request" or "invalid-repository" ? 2 : Errors.Count > 0 ? 4 : 0;
}
