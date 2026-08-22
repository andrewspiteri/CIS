namespace Cis.Modules.Plan;

public sealed record PlanWorkItem(
    string Id,
    string Category,
    string Complexity,
    string? ParentId,
    string? TaskPath,
    string Title,
    IReadOnlyList<string> RequirementIds,
    IReadOnlyList<string> ImpactIds,
    IReadOnlyList<string> DependsOn,
    string AcceptanceCriteria,
    string Validation,
    string Status,
    string TaskTypeKey = "core.legacy",
    string TaskTypeVersion = "1.0",
    string ApprovalGate = "none",
    IReadOnlyList<string>? Targets = null,
    IReadOnlyList<string>? DecisionIds = null,
    IReadOnlyList<string>? NegativeCriteria = null,
    IReadOnlyList<string>? RequiredOutputs = null,
    string? FrontendType = null);

public sealed record PlanSource(
    string Path,
    string Sha256,
    string DocumentType,
    int Requirements,
    bool FrontendChanges,
    IReadOnlyList<string> RequirementIds,
    IReadOnlyList<string> Targets,
    IReadOnlyList<string> Stack,
    IReadOnlyList<string> References,
    IReadOnlyList<string>? FrontendTypes = null,
    bool PublicEndpoints = false);

public sealed record FeatureSpecImportRequest(
    string RepositoryPath,
    string ChangeId,
    string FeatureSpecPath);

public sealed record PlanTaskTransitionRequest(
    string RepositoryPath,
    string ChangeId,
    string TaskId,
    string Status,
    string Actor,
    string Reason);

public sealed record PlanValidation(
    bool Valid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    int AcceptedImpacts,
    int CoveredImpacts,
    int OpenDecisions);

public sealed record PlanResult(
    string Status,
    string? ChangeId,
    string? PlanStatus,
    IReadOnlyList<PlanWorkItem> WorkItems,
    PlanValidation? Validation,
    IReadOnlyList<string> Errors,
    bool Applied,
    PlanSource? Source = null)
{
    public int ExitCode => Errors.Count > 0 ? 2 : Validation is { Valid: false } ? 5 : 0;
}
