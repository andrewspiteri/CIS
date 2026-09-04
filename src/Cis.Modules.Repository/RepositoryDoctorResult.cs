using Cis.Abstractions;

namespace Cis.Modules.Repository;

public sealed record RepositoryDoctorResult(
    string Status,
    string? RepositoryPath,
    string? DocumentationRoot,
    OllamaProbeResult Ollama,
    IReadOnlyList<CisRepositoryDoctorFinding> Findings,
    bool RepositoryConfigurationValid,
    bool InitializationStatusCached = false)
{
    public int ErrorCount => Findings.Count(finding => finding.Severity == "error");

    public int WarningCount => Findings.Count(finding => finding.Severity == "warning");

    public int InformationCount => Findings.Count(finding => finding.Severity is "info" or "information");

    public int ExitCode => !RepositoryConfigurationValid
        ? 2
        : ErrorCount > 0
            ? 5
            : 0;
}
