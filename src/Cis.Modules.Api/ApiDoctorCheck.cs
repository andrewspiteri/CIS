using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Api;

public sealed class ApiDoctorCheck : ICisRepositoryDoctorCheck
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    public string Name => "api";

    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
    {
        var profile = Path.Combine(context.DocumentationPath, "references", "api-governance-profile.md");
        var dictionary = Path.Combine(context.DocumentationPath, "references", "api-dictionary.md");
        if (!File.Exists(profile))
        {
            if (!File.Exists(dictionary)) return [];
            return [Finding("CIS-API-DOCTOR-001", "warning", "The repository API governance profile is missing.",
                [context.DocumentationRoot + "/references/api-governance-profile.md"],
                "Reconcile repository starters and review the generated API policy decisions.",
                $"cis repo init --root {context.DocumentationRoot} --dry-run")];
        }
        if (!File.Exists(dictionary)) return [];

        var state = Path.Combine(context.RepositoryPath, ApiGovernanceService.StatePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(state))
        {
            return [Finding("CIS-API-DOCTOR-002", "warning", "Local normalized API state is unavailable.",
                [ApiGovernanceService.StatePath], "Run deterministic API discovery, then strict validation.", "cis api discover")];
        }

        var diagnosticsPath = Path.Combine(context.RepositoryPath, ApiGovernanceService.DiagnosticsPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(diagnosticsPath)) return [];
        try
        {
            var diagnostics = JsonSerializer.Deserialize<ApiDiagnostic[]>(File.ReadAllText(diagnosticsPath), JsonOptions) ?? [];
            return diagnostics.Select(item => new CisRepositoryDoctorFinding(
                item.Code, item.Severity, "api-governance", item.Message, item.Evidence,
                item.Remediation, "cis api validate --strict", "review-required")).ToArray();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return [Finding("CIS-API-DOCTOR-003", "warning", $"Local API diagnostics are unreadable: {exception.Message}",
                [ApiGovernanceService.DiagnosticsPath], "Regenerate disposable API state.", "cis api discover")];
        }
    }

    private static CisRepositoryDoctorFinding Finding(string code, string severity, string message,
        IReadOnlyList<string> evidence, string fix, string command)
        => new(code, severity, "api-governance", message, evidence, fix, command, "review-required");
}
