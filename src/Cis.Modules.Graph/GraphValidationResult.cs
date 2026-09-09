using Cis.Abstractions;

namespace Cis.Modules.Graph;

public sealed record GraphValidationResult(
    string Status,
    string? RepositoryPath,
    string? GraphPath,
    string? BuildId,
    string Freshness,
    int NodeCount,
    int EdgeCount,
    IReadOnlyList<CisGraphDiagnostic> Diagnostics,
    bool Strict,
    bool RepositoryConfigurationValid,
    bool GraphAvailable,
    bool StructureCached = false)
{
    public int ErrorCount => Diagnostics.Count(diagnostic => diagnostic.Severity == "error");

    public int WarningCount => Diagnostics.Count(diagnostic => diagnostic.Severity == "warning");

    public int InformationCount => Diagnostics.Count(diagnostic =>
        diagnostic.Severity is "info" or "information");

    public int ExitCode => !RepositoryConfigurationValid
        ? 2
        : !GraphAvailable
            ? 4
            : ErrorCount > 0 || Strict && WarningCount > 0
                ? 5
                : 0;
}
