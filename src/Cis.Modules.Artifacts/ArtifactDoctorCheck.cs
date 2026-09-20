using Cis.Abstractions;

namespace Cis.Modules.Artifacts;

public sealed class ArtifactDoctorCheck(ArtifactRetentionService service) : ICisRepositoryDoctorCheck
{
    public string Name => "local-artifact-retention";

    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
    {
        var result = service.InspectRetention(context.RepositoryPath);
        if (result.Errors.Count > 0)
            return [new("CIS-ARTIFACT-DOCTOR-001", "warning", Name,
                "Local artifact retention policy is unavailable or invalid: " + string.Join("; ", result.Errors),
                [ArtifactRetentionService.ProfilePath],
                "Rerun idempotent initialization or repair the canonical retention profile.",
                "cis artifacts plan --format agent", "review-required")];
        var candidates = result.Candidates.Count;
        if (candidates == 0) return [];
        return [new("CIS-ARTIFACT-DOCTOR-002", "info", Name,
            $"{candidates} derived local artifact entries are eligible for configured retention actions.",
            result.Candidates.Take(10).ToArray(),
            "Review the retention plan, then compact archive-policy families or clean delete-policy families explicitly.",
            "cis artifacts plan --format agent", "review-required")];
    }
}
