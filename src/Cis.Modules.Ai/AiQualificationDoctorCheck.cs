using Cis.Abstractions;

namespace Cis.Modules.Ai;

public sealed class AiQualificationDoctorCheck(AiQualificationService service) : ICisRepositoryDoctorCheck
{
    public string Name => "ai-model-qualification";

    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
    {
        var findings = new List<CisRepositoryDoctorFinding>();
        var routing = Path.Combine(context.DocumentationPath, "references", "ai-routing-profile.md");
        if (!File.Exists(routing)) return findings;
        var registry = Path.Combine(context.DocumentationPath, AiQualificationService.RegistryPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(registry))
            findings.Add(new("CIS-AI-DOCTOR-001", "warning", Name,
                "The AI routing profile exists but the task-class model registry is missing.",
                [Path.GetRelativePath(context.RepositoryPath, routing).Replace('\\', '/')],
                "Rerun idempotent repository initialization to seed the model registry and evaluation dataset.",
                "cis repo init", "review-required"));
        var status = service.Status(context.RepositoryPath);
        if (status.Approvals.Count > 0 && status.Evidence.Count == 0)
            findings.Add(new("CIS-AI-DOCTOR-002", "error", Name,
                "Canonical model approvals exist without local runtime qualification evidence.",
                [AiQualificationService.RegistryPath, AiQualificationService.EvidenceRoot],
                "Rerun the probe, benchmark, and prompt-regression suite before relying on the approval.",
                "cis ai model status --format agent", "review-required"));
        return findings;
    }
}
