using System.Diagnostics;

namespace Cis.Abstractions;

public static class CisAgentRunModes
{
    public const string Plan = "plan";
    public const string Implement = "implement";
    public const string Review = "review";
    public static readonly IReadOnlySet<string> All = new HashSet<string>([Plan, Implement, Review], StringComparer.Ordinal);
}

public static class CisAgentPermissions
{
    public const string ReadOnly = "read-only";
    public const string WorkspaceWrite = "workspace-write";
    public static readonly IReadOnlySet<string> All = new HashSet<string>([ReadOnly, WorkspaceWrite], StringComparer.Ordinal);
}

public static class CisAgentRunStates
{
    public const string Prepared = "Prepared";
    public const string Starting = "Starting";
    public const string Running = "Running";
    public const string AwaitingPermission = "AwaitingPermission";
    public const string Cancelling = "Cancelling";
    public const string Cancelled = "Cancelled";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string TimedOut = "TimedOut";
    public const string InvalidEvidence = "InvalidEvidence";
    public const string Interrupted = "Interrupted";

    public static bool IsTerminal(string state) => state is Cancelled or Succeeded or Failed or TimedOut or InvalidEvidence or Interrupted;
}

public sealed record CisAgentProviderDescriptor(
    string Id,
    string DisplayName,
    string Kind,
    bool DirectExecution,
    IReadOnlyList<string> Transports,
    IReadOnlyList<string> Modes,
    IReadOnlyList<string> Permissions,
    bool SupportsResume,
    bool SupportsInteractivePermissions,
    string Description);

public sealed record CisAgentProviderDiagnosis(
    string Provider,
    string Status,
    bool Available,
    string? Executable,
    string? Version,
    bool AuthenticationAvailable,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Diagnostics);

public sealed record CisAgentProviderAuthenticationDescriptor(
    IReadOnlyList<string> Methods,
    string DefaultMethod,
    string Description);

public sealed record CisAgentProviderAuthenticationRequest(
    string RepositoryPath,
    string Method,
    TimeSpan Timeout);

public sealed record CisAgentProviderAuthenticationResult(
    string Status,
    int? ExitCode,
    IReadOnlyList<string> Diagnostics);

public sealed record CisAgentExecutionRequest(
    string RunId,
    int Attempt,
    string Provider,
    string Transport,
    string Mode,
    string Permission,
    string WorkingDirectory,
    string Prompt,
    TimeSpan Timeout,
    string? ResumeSessionId,
    bool ApproveWithinCeiling,
    string Actor,
    IReadOnlyDictionary<string, string> Environment,
    TimeSpan? StartupTimeout = null,
    TimeSpan? IdleTimeout = null);

public sealed record CisAgentProviderEvent(
    string Kind,
    string Message,
    string? ProviderEventType = null,
    string? ProviderSessionId = null,
    string? RawJson = null,
    string? RequestedCapability = null,
    string? RequestedTarget = null,
    long? InputTokens = null,
    long? OutputTokens = null,
    decimal? Cost = null,
    int? ProcessId = null,
    string? ProcessStartedAtUtc = null,
    bool? RequestApproved = null);

public sealed record CisAgentProviderExecutionResult(
    string Status,
    int? ExitCode,
    string? SessionId,
    string Summary,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> Validations,
    IReadOnlyList<string> Evidence,
    long? InputTokens,
    long? OutputTokens,
    decimal? Cost,
    string? FailureKind,
    IReadOnlyList<string> Diagnostics);

public interface ICisAgentProvider
{
    CisAgentProviderDescriptor Descriptor { get; }
    CisAgentProviderDiagnosis Diagnose(string repositoryPath);
    CisAgentProviderExecutionResult Execute(
        CisAgentExecutionRequest request,
        Action<CisAgentProviderEvent> onEvent,
        CancellationToken cancellationToken);
}

/// <summary>
/// Optional provider capability for starting a provider-native authentication flow.
/// CIS never accepts, stores, or transports provider credentials.
/// </summary>
public interface ICisAgentProviderAuthenticator
{
    CisAgentProviderAuthenticationDescriptor Authentication { get; }
    CisAgentProviderAuthenticationResult Authenticate(
        CisAgentProviderAuthenticationRequest request,
        Action<CisAgentProviderEvent> onEvent,
        CancellationToken cancellationToken);
}

public sealed record CisAgentProcessResult(
    int? ExitCode,
    bool TimedOut,
    bool Cancelled,
    bool OutputTruncated,
    int ProcessId,
    string StandardError,
    string? TimeoutKind = null);

public static class CisAgentProcessRunner
{
    public const int DefaultMaximumCharacters = 4 * 1024 * 1024;

    public static CisAgentProcessResult RunLines(
        ProcessStartInfo startInfo,
        string standardInput,
        TimeSpan timeout,
        Action<string> onOutputLine,
        Action<string>? onErrorLine,
        CancellationToken cancellationToken,
        int maximumCharacters = DefaultMaximumCharacters,
        Action<int, DateTimeOffset>? onStarted = null,
        TimeSpan? startupTimeout = null,
        TimeSpan? idleTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(onOutputLine);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (startupTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(startupTimeout));
        if (idleTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleTimeout));
        if (maximumCharacters < 1) throw new ArgumentOutOfRangeException(nameof(maximumCharacters));

        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start()) throw new InvalidOperationException($"Process '{startInfo.FileName}' could not be started.");
        var processId = process.Id;
        onStarted?.Invoke(processId, process.StartTime.ToUniversalTime());
        var retainedError = new System.Text.StringBuilder();
        var totalCharacters = 0L;
        var truncated = 0;
        var firstActivity = 0;
        using var totalSource = new CancellationTokenSource(timeout);
        using var startupSource = new CancellationTokenSource();
        using var idleSource = new CancellationTokenSource();
        if (startupTimeout.HasValue) startupSource.CancelAfter(startupTimeout.Value);

        void RecordActivity()
        {
            if (Interlocked.Exchange(ref firstActivity, 1) == 0)
                startupSource.CancelAfter(Timeout.InfiniteTimeSpan);
            if (idleTimeout.HasValue) idleSource.CancelAfter(idleTimeout.Value);
        }

        async Task ReadLines(StreamReader reader, Action<string> sink, bool retain)
        {
            string? line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                RecordActivity();
                var next = Interlocked.Add(ref totalCharacters, line.Length + 1);
                if (next > maximumCharacters)
                {
                    Interlocked.Exchange(ref truncated, 1);
                    continue;
                }
                sink(line);
                if (retain && retainedError.Length < 65_536)
                    retainedError.AppendLine(line[..Math.Min(line.Length, 4_096)]);
            }
        }

        var stdout = ReadLines(process.StandardOutput, onOutputLine, false);
        var stderr = ReadLines(process.StandardError, onErrorLine ?? (_ => { }), true);
        if (!string.IsNullOrEmpty(standardInput)) process.StandardInput.Write(standardInput);
        process.StandardInput.Close();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            totalSource.Token, startupSource.Token, idleSource.Token, cancellationToken);
        var cancelled = false;
        try
        {
            process.WaitForExitAsync(linked.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            cancelled = cancellationToken.IsCancellationRequested;
            TryKill(process);
        }
        Task.WhenAll(stdout, stderr).Wait(TimeSpan.FromSeconds(5));

        return new CisAgentProcessResult(
            process.HasExited ? process.ExitCode : null,
            !cancelled && (totalSource.IsCancellationRequested || startupSource.IsCancellationRequested || idleSource.IsCancellationRequested),
            cancelled,
            Volatile.Read(ref truncated) != 0,
            processId,
            retainedError.ToString().Trim(),
            startupSource.IsCancellationRequested ? "startup-timeout"
                : idleSource.IsCancellationRequested ? "idle-timeout"
                : totalSource.IsCancellationRequested ? "total-timeout"
                : null);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process may have exited between the state check and kill.
        }
    }
}
