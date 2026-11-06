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

/// <summary>Installed gated driver containment; recorded only after actual Windows job assignment.</summary>
public sealed record CisNativeWorkContainment(string Protocol, string JobName, int WindowsSessionId)
{
    public const string WindowsOuterJob = "cis-native-windows-outer-job-v1";
}

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
