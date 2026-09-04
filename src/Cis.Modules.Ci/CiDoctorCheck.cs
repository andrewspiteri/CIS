using Cis.Abstractions;

namespace Cis.Modules.Ci;

public sealed class CiDoctorCheck(CiService service) : ICisRepositoryDoctorCheck
{
    public string Name => "ci";
    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
    {
        var workflows = Path.Combine(context.RepositoryPath, ".github", "workflows");
        if (!Directory.Exists(workflows) || !Directory.EnumerateFiles(workflows, "*.y*ml").Any()) return [];
        var result = service.Providers(context.RepositoryPath);
        if (result.ExitCode == 0) return [];
        return [new("CIS-CI-DOCTOR-001", "warning", "ci-investigation",
            "Remote CI investigation is unavailable: " + string.Join("; ", result.Errors),
            [".github/workflows"],
            "Authenticate the configured provider and verify the inferred owner/repository target.",
            "cis ci providers --format agent", "review-required")];
    }
}
