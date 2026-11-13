using Cis.Abstractions;

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
    string? FrontendType = null)
{
    public string? StoryId { get; init; }
    public string? InstanceKey { get; init; }
    public string? WorkOwnerTaskId { get; init; }
    public IReadOnlyList<string> AuthorizationScopeIds { get; init; } = [];
    public string? AuthorizationScopeBasis { get; init; }
    public IReadOnlyList<string> WorkPaths { get; init; } = [];
    public string? Assignee { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AcceptanceCriteriaItems { get; init; }
    public IReadOnlyList<CisTaskVerificationObligation> VerificationObligations { get; init; } = [];
    public IReadOnlyList<string> ContractErrors { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    internal string? UnresolvedManifestRow { get; init; }
}

/// <summary>Task-specific work under a shared outcome; shared constraints and limits stay in its authorization.</summary>
public sealed record OutcomeTaskDefinition(string Title, IReadOnlyList<string> ScopeIds,
    string AcceptanceCriteria, string Validation)
{
    public IReadOnlyList<string> Paths { get; init; } = [];
    public IReadOnlyList<string> DependsOn { get; init; } = [];
    public string Complexity { get; init; } = "medium";
    public string Category { get; init; } = "implementation";
    public string? Assignee { get; init; }
}

public sealed record OutcomeTaskRequest(string RepositoryPath, string ChangeId,
    IReadOnlyList<OutcomeTaskDefinition> Tasks, string Actor, string Reason, string? TaskId = null);

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
    string FeatureSpecPath)
{
    public IReadOnlyList<string> AuthorizationScopeIds { get; init; } = [];
    public string? Actor { get; init; }
    public string? Reason { get; init; }
}

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
