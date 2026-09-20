using Cis.Modules.TechnicalIntent;

namespace Cis.Modules.SolutionDesign;

public sealed record SolutionComponent(
    string Id,
    string Name,
    string Classification,
    string Responsibility,
    string Owns,
    string Excludes,
    IReadOnlyList<string> RequirementIds);

public sealed record SolutionDesignValidation(
    bool Valid,
    bool Current,
    string EffectiveStatus,
    string DesignStatus,
    string ComponentSheetStatus,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool InferenceReconciliationRequired { get; init; }
}

public sealed record SolutionDesignResult(
    string Status,
    string? WorkspacePath,
    string? AuthorityRepositoryId,
    string? DesignPath,
    string? ComponentSheetPath,
    string? TechnicalIntentVersion,
    IReadOnlyList<SolutionComponent> Components,
    SolutionDesignValidation? Validation,
    IReadOnlyList<string> Errors,
    bool Applied)
{
    public int ExitCode => Status == "blocked" ? 5
        : Errors.Count > 0 ? 2
        : Status == "missing" ? 4
        : Validation is { Valid: false } ? 5
        : 0;
}

public interface ISolutionDesignTechnicalIntentSource
{
    TechnicalIntentResult Status(string workspacePath);
}

internal sealed class SolutionDesignTechnicalIntentSource(TechnicalIntentService service)
    : ISolutionDesignTechnicalIntentSource
{
    public TechnicalIntentResult Status(string workspacePath) => service.Status(workspacePath);
}
