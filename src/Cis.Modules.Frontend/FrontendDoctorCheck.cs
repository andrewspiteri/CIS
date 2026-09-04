using Cis.Abstractions;

namespace Cis.Modules.Frontend;

public sealed class FrontendDoctorCheck(FrontendContextService service) : ICisRepositoryDoctorCheck
{
    public string Name => "frontend-context";
    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
    {
        var routeMap = Path.Combine(context.DocumentationPath, "references", "screen-route-map.md");
        if (!File.Exists(routeMap)) return [];
        var result = service.Inventory(context.RepositoryPath);
        if (result.Status == "missing-state") return [new("CIS-FRONTEND-DOCTOR-001", "warning", Name,
            "Frontend reference governance is present but normalized frontend context state is missing.",
            ["references/screen-route-map.md", FrontendContextService.StatePath],
            "Run frontend discovery, validate it, then rebuild the graph.", "cis frontend discover --format agent", "review-required")];
        if (result.Errors == 0 && result.Warnings == 0) return [];
        return [new("CIS-FRONTEND-DOCTOR-002", result.Errors > 0 ? "error" : "warning", Name,
            $"Frontend context has {result.Errors} errors and {result.Warnings} warnings.",
            result.Diagnostics.Take(10).SelectMany(item => item.Evidence).Distinct().ToArray(),
            "Review provider conflicts, route collisions, source evidence, and canonical screen-route drift.",
            "cis frontend validate --strict --format agent", "review-required")];
    }
}
