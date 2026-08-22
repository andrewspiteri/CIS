using Cis.Abstractions;

namespace Cis.Modules.Graph;

public sealed record GraphBuildResult(
    string Status,
    string? RepositoryPath,
    string? DocumentationRoot,
    string? BuildId,
    string? GraphPath,
    string? ManifestPath,
    string? DiagnosticsPath,
    int NodeCount,
    int EdgeCount,
    IReadOnlyList<CisGraphDiagnostic> Diagnostics,
    bool Applied,
    bool RepositoryConfigurationValid)
{
    public int ErrorCount => Diagnostics.Count(diagnostic => diagnostic.Severity == "error");

    public int WarningCount => Diagnostics.Count(diagnostic => diagnostic.Severity == "warning");

    public int InformationCount => Diagnostics.Count(diagnostic =>
        diagnostic.Severity is "info" or "information");

    public int ExitCode => !RepositoryConfigurationValid
        ? 2
        : ErrorCount > 0
            ? 5
            : 0;
}
