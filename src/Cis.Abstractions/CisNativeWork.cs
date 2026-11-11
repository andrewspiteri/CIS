using System.Text.Json.Serialization;

namespace Cis.Abstractions;

/// <summary>Invocation authority, separate from a native profile's material identity.</summary>
public sealed record CisNativeWorkOptions(string? ChangeId = null, string? TaskId = null,
    string? ParentExecutionId = null, string? AuthorityPath = null, string? Actor = null)
{
    public string? RecoveryOfExecutionId { get; init; }
}

public sealed record CisNativeWorkRequest(string RepositoryPath, string ExecutionId, string Operation,
    int MaximumSeconds, CisNativeWorkOptions? Association = null)
{
    // Recovery names the retained native execution. It does not create new authority.
    public string? RecoveryOfExecutionId { get; init; }
    public string? RecoveryBasis { get; init; }
}

/// <summary>Installed gated native containment, verified before the target starts.</summary>
public sealed record CisNativeWorkContainment(string Protocol, string JobName, int WindowsSessionId)
{
    public const string WindowsOuterJob = "cis-native-windows-outer-job-v1";
    public const string LinuxOwnedSession = "cis-native-linux-session-v1";
    // Captured from the created job handle, never reconstructed from a later
    // caller's session/token. Null is retained for historical records and holds inspection.
    public string? WindowsObjectPath { get; init; }
    // The existing Linux supervisor owns this actual setsid worker session.
    // Its leader identity is captured while the worker is still launch-gated.
    public int? LinuxSessionId { get; init; }
    public string? LinuxSessionStartedAtUtc { get; init; }
    // Kernel start ticks identify the leader exactly across runtime processes;
    // converting Linux uptime to UTC is only an observation, not PID identity.
    public long? LinuxSessionStartTicks { get; init; }
}

/// <summary>Read-only qualification of a retained native tree for restart closure proof.
/// A diagnostic tree digest is not a prepared challenge or stopped-work receipt.</summary>
public sealed record CisNativeRestartClosureQualification(string Operation, string State, string ExecutionId,
    string RootExecutionId, string Revision, string AdoptionId, string TreeDigest, int ExecutionCount,
    IReadOnlyList<string> Gaps);

/// <summary>Controlled disposal of interrupted unit work, not a successful native result or historical stop attestation.</summary>
public sealed record CisNativeWorkAbandonment(string Basis, string Actor, string Reason, string RecordedAtUtc,
    IReadOnlyList<string> Evidence);

public sealed record CisNativeWorkAdmission(bool Required, string Status, string Coverage,
    CisAgentWorkReference? Reference, CisAgentWorkResult? Control, IReadOnlyList<string> Errors)
{
    public string? AuthorityPath { get; init; }
    [JsonIgnore]
    public ICisNativeWorkScope? Scope { get; init; }
    public bool Allowed => Errors.Count == 0 && Status is "admitted" or "unaccounted";
}

/// <summary>The existing work authority admits native execution; Testing does not own another ledger.</summary>
public interface ICisNativeWorkController
{
    CisNativeWorkAdmission Inspect(string repositoryPath, CisNativeWorkOptions? association = null);
    CisNativeWorkAdmission InspectExecution(string authorityPath, CisAgentWorkReference reference);
    CisNativeWorkAdmission Admit(CisNativeWorkRequest request);
    CisAgentWorkResult InspectSettlement(string authorityPath, CisAgentWorkReference reference)
        => InspectExecution(authorityPath, reference).Control
            ?? new("held", null, ["The exact native settlement cannot be read from its authority."], false);
}

/// <summary>Settlement of this execution and its descendants, independent of a later owner hold.</summary>
public static class CisNativeWorkSettlement
{
    public static bool Complete(CisAgentWorkResult result, CisAgentWorkReference reference)
    {
        if (result.Errors.Count != 0 || result.Status is not ("settled" or "unchanged")) return false;
        if (result.Work is null) return true; // A composed authority supplies the durable view; protocol fixtures may omit it.
        var executions = result.Work.Executions;
        var ids = new HashSet<string>(StringComparer.Ordinal) { reference.ExecutionId };
        for (var count = -1; count != ids.Count;)
        {
            count = ids.Count;
            foreach (var item in executions)
                if (item.ParentExecutionId is { } parent && ids.Contains(parent)) ids.Add(item.ExecutionId);
        }
        return executions.Any(item => item.ExecutionId == reference.ExecutionId)
            && executions.Where(item => ids.Contains(item.ExecutionId)).All(item => item.CompletedAtUtc is not null
                && item.RecoveryReason is null && item.Status != "waiting-for-owned-children");
    }
}

public interface ICisNativeWorkScope : IDisposable
{
    string AuthorityPath { get; }
    string Actor { get; }
    CisAgentWorkReference Reference { get; }
    int MaximumSeconds { get; }
    TimeSpan Remaining { get; }
    CisNativeWorkAdmission AdmitChild(string executionId, string operation, int maximumSeconds);
    void Launch();
    void Started(int processId, string processStartedAtUtc, CisNativeWorkContainment? containment = null);
    void Returned(bool safelyStopped);
    void MarkUncertain(string reason);
    CisAgentWorkResult Finish(string status, string? summary, IReadOnlyList<string>? evidence);
}

public static class CisNativeWorkEnvironment
{
    public const string Authority = "CIS_WORK_AUTHORITY";
    public const string ParentExecution = "CIS_WORK_PARENT_EXECUTION";
    public const string Change = "CIS_WORK_CHANGE";
    public const string Task = "CIS_WORK_TASK";
    public const string Actor = "CIS_WORK_ACTOR";
    public static IReadOnlyList<string> Keys { get; } = [Authority, ParentExecution, Change, Task, Actor];

    public static CisNativeWorkOptions Read()
    {
        static string? Value(string key) => Environment.GetEnvironmentVariable(key) is { } value && !string.IsNullOrWhiteSpace(value) ? value : null;
        return new(Value(Change), Value(Task), Value(ParentExecution), Value(Authority), Value(Actor));
    }

    /// <summary>Fail closed for direct library calls without the composed authority service.</summary>
    public static bool RequiresController(string repositoryPath, CisNativeWorkOptions? association)
    {
        if (association is not null && (association.ChangeId is not null || association.TaskId is not null
                || association.ParentExecutionId is not null || association.AuthorityPath is not null || association.RecoveryOfExecutionId is not null)
            || Keys.Any(key => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            || CisEngineeringPolicy.Read(repositoryPath)?.RequireTaskCompletion == true) return true;
        for (var directory = new DirectoryInfo(Path.GetFullPath(repositoryPath)); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, CisAgentWorkPaths.Adoption))
                || File.Exists(Path.Combine(directory.FullName, CisAgentWorkPaths.Ledger))
                || File.Exists(Path.Combine(directory.FullName, ".cis", "repository.yml"))
                || File.Exists(Path.Combine(directory.FullName, ".cis", "workspace.yml"))) return true;
        return false;
    }
}
