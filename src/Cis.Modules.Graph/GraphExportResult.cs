using Cis.Abstractions;

namespace Cis.Modules.Graph;

public sealed record GraphExportResult(
    string Status,
    string? RepositoryPath,
    string? GraphPath,
    string? OutputPath,
    string Type,
    string? BuildId,
    string Freshness,
    int NodeCount,
    int EdgeCount,
    IReadOnlyList<CisGraphDiagnostic> Diagnostics,
    bool Applied,
    bool RepositoryConfigurationValid,
    bool GraphAvailable,
    bool RequestValid,
    bool Collision)
{
    public int ExitCode => !RepositoryConfigurationValid || !RequestValid
        ? 2
        : !GraphAvailable || Collision
            ? 4
            : Diagnostics.Any(diagnostic => diagnostic.Severity == "error")
                ? 5
                : 0;
}

public sealed record GraphExportRequest(
    string RepositoryPath,
    string Type,
    string? OutputPath,
    bool Force);
