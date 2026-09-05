using Cis.Abstractions;

namespace Cis.Modules.Repository;

public sealed class WorkspaceBoundaryDoctorCheck(ICisWorkspaceRegistry registry) : ICisRepositoryDoctorCheck
{
    public string Name => "workspace-boundary";

    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
    {
        var relativePath = ".cis/workspace.yml";
        var path = Path.Combine(context.RepositoryPath, ".cis", "workspace.yml");
        if (!File.Exists(path)) return [];

        var resolution = registry.Resolve(context.RepositoryPath);
        if (resolution.IsSuccess) return [];
        return [new CisRepositoryDoctorFinding(
            "CIS-WORKSPACE-001",
            "error",
            Name,
            "The workspace registry does not define a valid schema-2 product and ecosystem boundary.",
            [relativePath, .. resolution.Errors.Take(20)],
            "Reinitialize the workspace with explicit --ecosystem and --product identities, then explicitly re-import every owned or dependency repository with its relationship.",
            null,
            "manual")];
    }
}
