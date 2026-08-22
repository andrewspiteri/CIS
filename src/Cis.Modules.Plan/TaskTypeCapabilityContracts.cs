namespace Cis.Modules.Plan;

public sealed record TaskTypeCapabilitySelection(
    string CapabilityKey,
    string SelectedTypeKey,
    IReadOnlyList<string> ReplacesTypeKeys,
    string Reviewer,
    DateTimeOffset TimestampUtc,
    string Rationale);

public sealed record TaskTypeCapabilityStatus(
    IReadOnlyList<TaskTypeCapabilitySelection> Selections,
    IReadOnlyList<TaskTypeCapabilityConflict> Conflicts,
    IReadOnlyList<string> Errors);

public sealed record TaskTypeCapabilityConflict(
    string CapabilityKey,
    IReadOnlyList<string> CandidateTypeKeys,
    string Reason);

public sealed record TaskTypeCapabilitySelectRequest(
    string RepositoryPath,
    string CapabilityKey,
    string SelectedTypeKey,
    IReadOnlyList<string> ReplacesTypeKeys,
    string Reviewer,
    string Rationale);

public sealed record TaskTypeCapabilityResult(
    string Status,
    IReadOnlyList<TaskTypeCapabilitySelection> Selections,
    IReadOnlyList<TaskTypeCapabilityConflict> Conflicts,
    IReadOnlyList<string> Errors,
    bool Applied)
{
    public int ExitCode => Errors.Count > 0 ? 2 : Conflicts.Count > 0 ? 5 : 0;
}

public sealed record TaskTypeMigrationRequest(
    string RepositoryPath,
    string ChangeId,
    string TaskId,
    string TargetTypeKey,
    string Reviewer,
    string Rationale);

public sealed record TaskTypeResolution(
    IReadOnlyList<Cis.Abstractions.CisTaskTypeDefinition> Definitions,
    IReadOnlyList<TaskTypeCapabilityConflict> Conflicts,
    IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Conflicts.Count == 0 && Errors.Count == 0;
}
