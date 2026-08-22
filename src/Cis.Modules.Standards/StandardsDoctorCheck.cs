using Cis.Abstractions;

namespace Cis.Modules.Standards;

public sealed class StandardsDoctorCheck : ICisRepositoryDoctorCheck
{
    private readonly StandardsGovernanceService _service;
    public StandardsDoctorCheck(StandardsGovernanceService service) => _service = service;
    public string Name => "standards-governance";

    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
    {
        var result = _service.Validate(context.RepositoryPath, strict: false);
        return result.Diagnostics.Select(item => new CisRepositoryDoctorFinding(
            item.Code,
            item.Severity,
            "standards-governance",
            item.Message,
            string.IsNullOrWhiteSpace(item.Path) ? [] : [item.Path],
            item.Remediation,
            "cis standards validate --strict",
            "review-required")).ToArray();
    }
}
