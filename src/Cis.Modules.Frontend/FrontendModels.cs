using Cis.Abstractions;

namespace Cis.Modules.Frontend;

public sealed record FrontendContextDiagnostic(
    string Code,
    string Severity,
    string Message,
    IReadOnlyList<string> Evidence);

public sealed record FrontendContextDocument(
    int SchemaVersion,
    string RepositoryId,
    string CreatedUtc,
    string Revision,
    string Digest,
    IReadOnlyList<string> Providers,
    IReadOnlyList<CisFrontendObservation> Observations,
    IReadOnlyList<FrontendContextDiagnostic> Diagnostics);

public sealed record FrontendContextResult(
    string Status,
    string? RepositoryPath,
    string? StatePath,
    string? Digest,
    IReadOnlyList<string> Providers,
    IReadOnlyList<CisFrontendObservation> Observations,
    IReadOnlyList<FrontendContextDiagnostic> Diagnostics,
    bool Strict,
    bool Applied)
{
    public int Errors => Diagnostics.Count(item => item.Severity == "error");
    public int Warnings => Diagnostics.Count(item => item.Severity == "warning");
    public int ExitCode => Status is "invalid-repository" or "missing-state"
        ? 4
        : Errors > 0 || Strict && Warnings > 0
            ? 5
            : 0;
}
