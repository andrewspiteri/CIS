using Cis.Abstractions;

namespace Cis.Modules.Skills;

public sealed class SkillDoctorCheck : ICisRepositoryDoctorCheck
{
    private readonly SkillValidationService _service;

    public SkillDoctorCheck(SkillValidationService service)
    {
        _service = service;
    }

    public string Name => "skills";

    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
        => _service.Validate(context.RepositoryPath, strict: false).Diagnostics
            .Select(diagnostic => new CisRepositoryDoctorFinding(
                diagnostic.Code,
                diagnostic.Severity,
                "agent-skills",
                diagnostic.Message,
                diagnostic.Evidence.Prepend(diagnostic.Path).ToArray(),
                SuggestedFix(diagnostic),
                FixCommand(diagnostic),
                "review-required"))
            .ToArray();

    private static string SuggestedFix(SkillDiagnostic diagnostic)
        => IsSafelyFixable(diagnostic)
            ? "Review, then apply the deterministic missing-metadata or heading repair and rerun strict validation."
            : "Correct the skill metadata, structure, or referenced resource and rerun validation.";

    private static string FixCommand(SkillDiagnostic diagnostic)
        => IsSafelyFixable(diagnostic)
            ? "cis skills validate --fix --strict"
            : "cis skills validate --strict";

    private static bool IsSafelyFixable(SkillDiagnostic diagnostic)
        => diagnostic.Code is "CIS-SKILL-YAML-001"
            or "CIS-SKILL-NAME-002"
            or "CIS-SKILL-DESCRIPTION-001"
            or "CIS-SKILL-BODY-001"
            or "CIS-SKILL-HEADING-001";
}
