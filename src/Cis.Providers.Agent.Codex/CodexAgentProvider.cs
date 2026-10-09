using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Providers.Agent.Codex;

public sealed class CodexAgentProvider : ICisAgentProvider, ICisAgentProviderAuthenticator
{
    private const int MaximumRawJsonCharacters = 64 * 1024;
    private const int MaximumProtocolLineCharacters = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _executable;

    public CodexAgentProvider() : this(ResolveExecutable(
        Environment.GetEnvironmentVariable("CIS_CODEX_EXECUTABLE"),
        Environment.GetEnvironmentVariable("PATH"),
        Environment.GetEnvironmentVariable("LOCALAPPDATA"),
        OperatingSystem.IsWindows())) { }
    public CodexAgentProvider(string executable) => _executable = executable;

    public CisAgentProviderDescriptor Descriptor { get; } = new(
        "codex", "Codex", "local-cli", true,
        ["app-server", "exec-json"],
        [CisAgentRunModes.Plan, CisAgentRunModes.Implement, CisAgentRunModes.Review],
        [CisAgentPermissions.ReadOnly, CisAgentPermissions.WorkspaceWrite],
        true, true,
        "Uses Codex App Server for stateful work and codex exec JSONL for explicit bounded fallback execution.");

    public CisAgentProviderDiagnosis Diagnose(string repositoryPath)
    {
        var version = Probe(["--version"], repositoryPath);
        var login = Probe(["login", "status"], repositoryPath);
        return ClassifyDiagnosis(_executable, version, login);
    }

    internal static CisAgentProviderDiagnosis ClassifyDiagnosis(string executable, string? version, string? login)
    {
        if (version is null)
            return new("codex", "executable-missing", false, executable, null, false, [],
                [$"Codex executable '{executable}' is unavailable or did not return a version."]);
        var authenticated = login is not null && !login.Contains("not logged", StringComparison.OrdinalIgnoreCase);
        return new("codex", authenticated ? "ready" : "authentication-unverified", true,
            executable, FirstLine(version), authenticated,
            ["app-server", "exec-json", "resume", "structured-events", "bounded-permissions",
                "authentication:browser", "authentication:device"],
            authenticated ? [] : ["Codex login status did not confirm credentials. An ambient Codex Desktop or App Server session may still execute; authenticate explicitly or retain execution evidence."]);
    }

    public CisAgentProviderAuthenticationDescriptor Authentication { get; } = new(
        ["browser", "device"], "browser",
        "Starts Codex provider-native ChatGPT OAuth. CIS never receives or stores credentials.");

    public CisAgentProviderAuthenticationResult Authenticate(
        CisAgentProviderAuthenticationRequest request,
        Action<CisAgentProviderEvent> onEvent,
        CancellationToken cancellationToken)
    {
        if (!Authentication.Methods.Contains(request.Method, StringComparer.OrdinalIgnoreCase))
            return new("unsupported-method", null,
                [$"Codex authentication method '{request.Method}' is unsupported. Use browser or device."]);
        var start = StartInfo(request.RepositoryPath);
        start.ArgumentList.Add("login");
        if (request.Method.Equals("device", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add("--device-auth");
        try
        {
            var result = CisAgentProcessRunner.RunLines(start, string.Empty, request.Timeout,
                line => onEvent(new("authentication-output", Redact(line), "codex/login")),
                line => onEvent(new("authentication-output", Redact(line), "codex/login")),
                cancellationToken,
                onStarted: (id, started) => onEvent(new("process-started", "Codex provider-native authentication started.",
                    "codex/login", ProcessId: id, ProcessStartedAtUtc: started.ToString("O"))));
            var status = result.Cancelled ? "cancelled"
                : result.TimedOut ? "timedout"
                : result.ExitCode == 0 ? "authentication-complete"
                : "authentication-failed";
            var diagnostics = new List<string>();
            if (result.OutputTruncated) diagnostics.Add("Provider authentication output exceeded the retained display bound.");
            if (!string.IsNullOrWhiteSpace(result.StandardError)) diagnostics.Add(Redact(result.StandardError));
            if (status != "authentication-complete" && diagnostics.Count == 0)
                diagnostics.Add(status == "cancelled" ? "Provider authentication was cancelled."
                    : status == "timedout" ? "Provider authentication timed out."
                    : $"Provider-native authentication exited with code {result.ExitCode?.ToString() ?? "unknown"}.");
            return new(status, result.ExitCode, diagnostics);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return new("authentication-failed", null, [Redact(exception.Message)]);
        }
    }

    public CisAgentProviderExecutionResult Execute(CisAgentExecutionRequest request, Action<CisAgentProviderEvent> onEvent, CancellationToken cancellationToken)
        => request.Transport switch
        {
            "app-server" => ExecuteAppServer(request, onEvent, cancellationToken),
            "exec-json" => ExecuteJson(request, onEvent, cancellationToken),
            _ => Failure("unsupported-transport", $"Codex transport '{request.Transport}' is unsupported."),
        };

    private CisAgentProviderExecutionResult ExecuteJson(CisAgentExecutionRequest request, Action<CisAgentProviderEvent> onEvent, CancellationToken cancellationToken)
    {
        var start = StartInfo(request.WorkingDirectory);
        ConfigureExecArguments(start, request);
        ApplyEnvironment(start, request.Environment);
        return ExecuteJson(request, onEvent, cancellationToken, start);
    }

    internal static CisAgentProviderExecutionResult ExecuteJson(CisAgentExecutionRequest request,
        Action<CisAgentProviderEvent> onEvent, CancellationToken cancellationToken, ProcessStartInfo start)
    {
        string? session = null;
        string summary = string.Empty;
        long? input = null, output = null;
        var diagnostics = new List<string>();
        var invalidEvent = false;
        var process = CisAgentProcessRunner.RunLines(start, request.Prompt, request.Timeout, line =>
        {
            var normalized = ParseJsonEvent(line, ref session, ref summary, ref input, ref output);
            invalidEvent |= normalized.Kind == "invalid-provider-event";
            onEvent(normalized);
        }, line => onEvent(new("provider-stderr", Redact(line))), cancellationToken,
            onStarted: (id, started) => onEvent(new("process-started", $"Codex process {id} started.", ProcessId: id, ProcessStartedAtUtc: started.ToString("O"))),
            startupTimeout: request.StartupTimeout, idleTimeout: request.IdleTimeout);

        if (invalidEvent) diagnostics.Add("Provider stream contained an invalid JSONL event.");
        if (process.OutputTruncated) diagnostics.Add("Provider output exceeded the configured bound.");
        if (!string.IsNullOrWhiteSpace(process.StandardError)) diagnostics.Add(Redact(process.StandardError));
        var state = process.Cancelled ? CisAgentRunStates.Cancelled
            : process.TimedOut ? CisAgentRunStates.TimedOut
            : process.OutputTruncated || invalidEvent ? CisAgentRunStates.InvalidEvidence
            : process.ExitCode == 0 ? CisAgentRunStates.Succeeded : CisAgentRunStates.Failed;
        return new(state, process.ExitCode, session, summary, [], [], [], input, output, null,
            state == CisAgentRunStates.InvalidEvidence && invalidEvent ? "invalid-provider-event" : FailureKind(process, process.StandardError), diagnostics);
    }

    internal static void ConfigureExecArguments(ProcessStartInfo start, CisAgentExecutionRequest request)
    {
        start.ArgumentList.Add("exec");
        if (request.ResumeSessionId is not null)
        {
            start.ArgumentList.Add("resume");
            start.ArgumentList.Add(request.ResumeSessionId);
        }
        start.ArgumentList.Add("--json");
        if (request.Model is not null) { start.ArgumentList.Add("--model"); start.ArgumentList.Add(request.Model); }
        start.ArgumentList.Add("--sandbox");
        start.ArgumentList.Add(request.Permission);
        if (request.ApproveWithinCeiling)
            start.ArgumentList.Add("--approve-for-me");
        else
        {
            start.ArgumentList.Add("--config");
            start.ArgumentList.Add("approval_policy=\"never\"");
        }
        start.ArgumentList.Add("-");
    }

    private CisAgentProviderExecutionResult ExecuteAppServer(CisAgentExecutionRequest request, Action<CisAgentProviderEvent> onEvent, CancellationToken cancellationToken)
    {
        var start = StartInfo(request.WorkingDirectory);
        start.ArgumentList.Add("app-server");
        return ExecuteAppServer(request, onEvent, cancellationToken, start);
    }

    internal static CisAgentProviderExecutionResult ExecuteAppServer(CisAgentExecutionRequest request, Action<CisAgentProviderEvent> onEvent,
        CancellationToken cancellationToken, ProcessStartInfo start)
    {
        ApplyEnvironment(start, request.Environment);
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try
        {
            if (!process.Start()) return Failure("runner-infrastructure", "Codex App Server could not be started.");
        }
        catch (Win32Exception exception) { return Failure("missing-prerequisite", Redact(exception.Message)); }
        onEvent(new("process-started", $"Codex App Server process {process.Id} started.", ProcessId: process.Id, ProcessStartedAtUtc: process.StartTime.ToUniversalTime().ToString("O")));

        using var totalSource = new CancellationTokenSource(request.Timeout);
        using var startupSource = new CancellationTokenSource(request.StartupTimeout ?? TimeSpan.FromSeconds(30));
        using var idleSource = new CancellationTokenSource();
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, totalSource.Token, startupSource.Token, idleSource.Token);
        var firstActivity = 0;
        var compacting = 0;
        var activityGate = new object();

        void RecordActivity()
        {
            lock (activityGate)
            {
                if (Interlocked.Exchange(ref firstActivity, 1) == 0)
                    startupSource.CancelAfter(Timeout.InfiniteTimeSpan);
                idleSource.CancelAfter(Volatile.Read(ref compacting) == 1
                    ? Timeout.InfiniteTimeSpan : request.IdleTimeout ?? TimeSpan.FromMinutes(5));
            }
        }

        var session = request.ResumeSessionId;
        string? turnId = null;
        var pendingEvents = new List<(JsonElement Root, string Line)>();
        var pendingCharacters = 0;
        var turnResponded = false;
        var bufferExceeded = false;
        var summary = string.Empty;
        long? input = null, output = null;
        var terminal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var initialized = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var threadReady = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writeLock = new SemaphoreSlim(1, 1);

        void Send(object message)
        {
            SendProtocolMessage(process, message, writeLock, linkedSource.Token);
        }

        void HandleNotification(JsonElement root, string line, bool journal = true)
        {
            if (ContextCompactionActivity(root, session, turnId) is { } consolidating)
            {
                lock (activityGate)
                {
                    Interlocked.Exchange(ref compacting, consolidating ? 1 : 0);
                    RecordActivity();
                }
            }
            var normalized = ParseAppServerEvent(root, line, session, turnId, ref summary, ref input, ref output);
            if (journal) onEvent(normalized);
            if (AppServerTerminalState(root, session, turnId) is { } state) terminal.TrySetResult(state);
        }

        var stderr = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                RecordActivity();
                onEvent(new("provider-stderr", Redact(line)));
            }
        }, CancellationToken.None);
        var reader = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    RecordActivity();
                    if (line.Length > MaximumProtocolLineCharacters)
                    {
                        onEvent(new("invalid-provider-event", "Codex App Server emitted a protocol line above the safe parse limit."));
                        terminal.TrySetResult(CisAgentRunStates.InvalidEvidence);
                        continue;
                    }
                    var rawEventTruncated = line.Length > MaximumRawJsonCharacters;
                    JsonDocument document;
                    try { document = JsonDocument.Parse(line); }
                    catch (JsonException)
                    {
                        onEvent(new("invalid-provider-event", "Codex App Server emitted malformed JSON."));
                        terminal.TrySetResult(CisAgentRunStates.InvalidEvidence);
                        continue;
                    }
                    using (document)
                    {
                        if (rawEventTruncated)
                            onEvent(new("provider-output-truncated", "Codex App Server event was parsed but its retained raw payload was truncated."));
                        var root = document.RootElement;
                        if (root.TryGetProperty("id", out var id))
                        {
                            if (id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var responseId))
                                throw new InvalidOperationException("Codex App Server returned an unsupported protocol request or response identity.");
                            if (root.TryGetProperty("method", out var requestMethod))
                            {
                                var method = requestMethod.GetString() ?? string.Empty;
                                var parameters = root.TryGetProperty("params", out var requestParameters) ? requestParameters : default;
                                var approval = EvaluateApproval(method, parameters, request);
                                onEvent(new(CisAgentRunStates.AwaitingPermission,
                                    approval.Approved ? "Permission accepted within the declared ceiling." : "Permission denied by the declared run policy.",
                                    method, session, Limit(line), approval.Capability, approval.Target, RequestApproved: approval.Approved));
                                if (method == "item/permissions/requestApproval")
                                    Send(new { id = responseId, result = new { permissions = Array.Empty<object>(), scope = "turn" } });
                                else if (method is "item/commandExecution/requestApproval" or "item/fileChange/requestApproval")
                                    Send(new { id = responseId, result = new { decision = approval.Approved ? "accept" : "decline" } });
                                else
                                    Send(new { id = responseId, error = new { code = -32601, message = "CIS does not support this provider interaction." } });
                                continue;
                            }
                            if (responseId == 0)
                            {
                                if (initialized.Task.IsCompleted) throw new InvalidOperationException("Duplicate Codex initialize response.");
                                if (root.TryGetProperty("error", out var error)) throw new InvalidOperationException(ErrorMessage(error));
                                else initialized.TrySetResult(true);
                                continue;
                            }
                            if (responseId == 1)
                            {
                                if (threadReady.Task.IsCompleted) throw new InvalidOperationException("Duplicate Codex thread response.");
                                if (root.TryGetProperty("error", out var error)) threadReady.TrySetException(new InvalidOperationException(ErrorMessage(error)));
                                else if (TryThreadId(root, out var thread)) { session = thread; threadReady.TrySetResult(thread!); }
                                else threadReady.TrySetException(new InvalidOperationException("Codex thread response did not contain a thread identity."));
                                continue;
                            }
                            if (responseId == 2)
                            {
                                if (turnResponded) throw new InvalidOperationException("Duplicate Codex turn/start response.");
                                turnResponded = true;
                                if (TurnResponseError(root, out turnId) is { } error)
                                    terminal.TrySetException(new InvalidOperationException(error));
                                else
                                    foreach (var pending in pendingEvents) HandleNotification(pending.Root, pending.Line, journal: false);
                                pendingEvents.Clear();
                                pendingCharacters = 0;
                                continue;
                            }
                            throw new InvalidOperationException("Unexpected Codex App Server response identity.");
                        }
                        // Notifications can precede the turn/start response. Retain a bounded
                        // batch until the response identifies which turn this invocation owns.
                        if (turnId is null && IsCurrentThreadEvent(root, session))
                        {
                            onEvent(ParseAppServerEvent(root, line, session, null, ref summary, ref input, ref output));
                            if (bufferExceeded) continue;
                            if (pendingEvents.Count >= 128 || pendingCharacters + line.Length > MaximumProtocolLineCharacters)
                            {
                                bufferExceeded = true;
                                onEvent(new("invalid-provider-event", "Codex turn/start did not establish an identity within the event buffer limit."));
                                terminal.TrySetResult(CisAgentRunStates.InvalidEvidence);
                            }
                            else
                            {
                                pendingEvents.Add((root.Clone(), line));
                                pendingCharacters += line.Length;
                            }
                        }
                        else HandleNotification(root, line);
                    }
                }
                if (!terminal.Task.IsCompleted) terminal.TrySetResult(CisAgentRunStates.Interrupted);
            }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
            {
                initialized.TrySetException(error);
                threadReady.TrySetException(error);
                terminal.TrySetException(error);
            }
        }, CancellationToken.None);

        try
        {
            Send(new { method = "initialize", id = 0, @params = new { clientInfo = new { name = "change_impact_studio", title = "Change Impact Studio", version = "0.3.0" } } });
            Wait(initialized.Task, linkedSource.Token);
            Send(new { method = "initialized", @params = new { } });
            var sandbox = request.Permission;
            var method = request.ResumeSessionId is null ? "thread/start" : "thread/resume";
            var parameters = BuildThreadParameters(request, sandbox);
            Send(new { method, id = 1, @params = parameters });
            var threadId = Wait(threadReady.Task, linkedSource.Token);
            Send(new { method = "turn/start", id = 2, @params = new { threadId, input = new[] { new { type = "text", text = request.Prompt } }, cwd = request.WorkingDirectory } });
            var state = Wait(terminal.Task, linkedSource.Token);
            TryKill(process);
            Task.WhenAll(reader, stderr).Wait(TimeSpan.FromSeconds(5));
            return new(state, process.HasExited ? process.ExitCode : null, session, summary, [], [], [], input, output, null,
                state == CisAgentRunStates.Succeeded ? null : state.ToLowerInvariant(), []);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            var cancelled = cancellationToken.IsCancellationRequested;
            var timeoutKind = startupSource.IsCancellationRequested ? "startup-timeout"
                : idleSource.IsCancellationRequested ? "idle-timeout"
                : totalSource.IsCancellationRequested ? "total-timeout"
                : "timeout";
            return new(cancelled ? CisAgentRunStates.Cancelled : CisAgentRunStates.TimedOut,
                process.HasExited ? process.ExitCode : null, session, summary, [], [], [], input, output, null,
                cancelled ? "cancellation" : timeoutKind, []);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            TryKill(process);
            return Failure("provider-protocol", Redact(exception.GetBaseException().Message), session);
        }
        finally
        {
            TryKill(process);
            // Cancellation can also interrupt an approval response on the reader task.
            // Observe its failure and release the gate only after both readers have finished.
            var readers = Task.WhenAll(reader, stderr);
            _ = readers.ContinueWith(task => { _ = task.Exception; writeLock.Dispose(); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            try { readers.Wait(TimeSpan.FromSeconds(5)); }
            catch (AggregateException) { /* The run result above retains the protocol/cancellation outcome. */ }
        }
    }

    internal static void SendProtocolMessage(Process process, object message, SemaphoreSlim writeLock, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(message, JsonOptions) + Environment.NewLine;
        writeLock.Wait(cancellationToken);
        try { CisAgentInputDelivery.Write(process, line, cancellationToken); }
        finally { writeLock.Release(); }
    }

    internal static object BuildThreadParameters(CisAgentExecutionRequest request, string sandbox)
        => request.ResumeSessionId is null
            ? new { cwd = request.WorkingDirectory, approvalPolicy = "on-request", sandbox, serviceName = "change-impact-studio", model = request.Model }
            : new { threadId = request.ResumeSessionId, cwd = request.WorkingDirectory, approvalPolicy = "on-request", sandbox, model = request.Model };

    internal static CisAgentProviderEvent ParseJsonEvent(string line, ref string? session, ref string summary, ref long? input, ref long? output)
    {
        if (line.Length > MaximumProtocolLineCharacters) return new("invalid-provider-event", "Codex JSONL event exceeded the safe parse limit.");
        var rawEventTruncated = line.Length > MaximumRawJsonCharacters;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = Text(root, "type") ?? "unknown";
            session ??= Text(root, "thread_id") ?? Text(root, "threadId");
            if (type == "item.completed" && root.TryGetProperty("item", out var item) && Text(item, "type") == "agent_message")
                summary = Text(item, "text") ?? summary;
            if (root.TryGetProperty("usage", out var usage))
            {
                input = Number(usage, "input_tokens") ?? input;
                output = Number(usage, "output_tokens") ?? output;
            }
            var message = type switch
            {
                "thread.started" => "Codex thread started.",
                "turn.started" => "Codex turn started.",
                "turn.completed" => "Codex turn completed.",
                "turn.failed" => "Codex turn failed.",
                "error" => Text(root, "message") ?? "Codex reported an error.",
                _ => type,
            };
            if (rawEventTruncated) message += " (raw payload truncated)";
            return new("provider-event", Redact(message), type, session, Limit(line), InputTokens: input, OutputTokens: output);
        }
        catch (JsonException) { return new("invalid-provider-event", "Codex emitted malformed JSONL."); }
    }

    internal static CisAgentProviderEvent ParseAppServerEvent(JsonElement root, string line, string? session, string? turnId, ref string summary, ref long? input, ref long? output)
    {
        var method = Text(root, "method") ?? "response";
        if (method == "item/completed" && IsCurrentTurnEvent(root, session, turnId)
            && root.TryGetProperty("params", out var parameters) && parameters.TryGetProperty("item", out var item) && Text(item, "type") == "agentMessage")
            summary = Text(item, "text") ?? summary;
        if (method == "turn/completed" && root.TryGetProperty("params", out var completed) && completed.TryGetProperty("turn", out var turn) && Text(turn, "status") is { } status)
            return new("provider-event", $"Codex turn completed with status {status}.", method, session, Limit(line), InputTokens: input, OutputTokens: output);
        return new("provider-event", method, method, session, Limit(line), InputTokens: input, OutputTokens: output);
    }

    internal static CodexApprovalEvaluation EvaluateApproval(string method, JsonElement parameters, CisAgentExecutionRequest request)
    {
        var hasParameters = parameters.ValueKind == JsonValueKind.Object;
        var cwd = hasParameters ? Text(parameters, "cwd") : null;
        var grantRoot = hasParameters ? Text(parameters, "grantRoot") : null;
        var networkContext = default(JsonElement);
        var network = hasParameters && parameters.TryGetProperty("networkApprovalContext", out networkContext)
            && networkContext.ValueKind != JsonValueKind.Null;
        var additional = hasParameters && parameters.TryGetProperty("additionalPermissions", out var additionalPermissions)
            && additionalPermissions.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;

        if (method == "item/permissions/requestApproval")
            return new(false, "dynamic-permissions", cwd ?? request.WorkingDirectory);
        if (network)
        {
            var host = networkContext.ValueKind == JsonValueKind.Object ? Text(networkContext, "host") : null;
            var protocol = networkContext.ValueKind == JsonValueKind.Object ? Text(networkContext, "protocol") : null;
            return new(false, "network", string.Join(":", new[] { protocol, host }.Where(value => !string.IsNullOrWhiteSpace(value))));
        }
        if (additional)
            return new(false, "additional-permissions", cwd ?? request.WorkingDirectory);

        var target = method == "item/fileChange/requestApproval" ? grantRoot ?? request.WorkingDirectory : cwd ?? request.WorkingDirectory;
        var supported = method is "item/commandExecution/requestApproval" or "item/fileChange/requestApproval";
        var approved = supported
            && request.ApproveWithinCeiling
            && request.Permission == CisAgentPermissions.WorkspaceWrite
            && IsContained(request.WorkingDirectory, target);
        return new(approved, method == "item/fileChange/requestApproval" ? "filesystem-write" : "command-execution", target);
    }

    private static string? EventThreadId(JsonElement root)
        => root.TryGetProperty("params", out var parameters) && parameters.ValueKind == JsonValueKind.Object
            ? Text(parameters, "threadId") : null;

    private static bool IsCurrentThreadEvent(JsonElement root, string? session)
        => !string.IsNullOrWhiteSpace(session) && string.Equals(EventThreadId(root), session, StringComparison.Ordinal);

    private static string? EventTurnId(JsonElement root)
    {
        if (!root.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object) return null;
        return Text(parameters, "turnId") ?? (parameters.TryGetProperty("turn", out var turn) && turn.ValueKind == JsonValueKind.Object
            ? Text(turn, "id") : null);
    }

    private static bool IsCurrentTurnEvent(JsonElement root, string? session, string? turnId)
        => IsCurrentThreadEvent(root, session) && !string.IsNullOrWhiteSpace(turnId)
            && string.Equals(EventTurnId(root), turnId, StringComparison.Ordinal);

    internal static string? TurnResponseError(JsonElement root, out string? turnId)
    {
        turnId = null;
        if (root.TryGetProperty("error", out var error))
            return error.ValueKind == JsonValueKind.Object ? ErrorMessage(error) : "Codex turn/start returned an invalid error.";
        if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty("turn", out var turn) && turn.ValueKind == JsonValueKind.Object)
            turnId = Text(turn, "id");
        return string.IsNullOrWhiteSpace(turnId) ? "Codex turn/start response did not contain a turn identity." : null;
    }

    internal static bool? ContextCompactionActivity(JsonElement root, string? session, string? turnId)
    {
        if (!IsCurrentTurnEvent(root, session, turnId)) return null;
        var method = Text(root, "method");
        if (method is not ("item/started" or "item/completed")) return null;
        if (!root.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object
            || Text(item, "type") != "contextCompaction") return null;
        // Context consolidation is a known active provider operation that may emit
        // no tokens. The total run deadline still bounds it; ordinary idle checks resume afterward.
        return method == "item/started";
    }

    internal static string? AppServerTerminalState(JsonElement root, string? session, string? turnId)
    {
        var method = Text(root, "method");
        // App Server also streams delegated threads. Only the selected thread can
        // supply this run's result; child completion must not terminate its parent.
        if (method == "turn/completed") return IsCurrentTurnEvent(root, session, turnId) ? TerminalState(root) : null;
        if (method != "error") return null;
        if (EventThreadId(root) is not null && !IsCurrentThreadEvent(root, session)) return null;
        if (EventTurnId(root) is not null && !IsCurrentTurnEvent(root, session, turnId)) return null;
        // Reconnection notices are streamed as errors while the provider is still
        // recovering. Keep journaling them and let its terminal event or our timeout decide.
        return root.TryGetProperty("params", out var parameters)
            && parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("willRetry", out var retry)
            && retry.ValueKind == JsonValueKind.True ? null : CisAgentRunStates.Failed;
    }

    private static string TerminalState(JsonElement root)
    {
        if (!root.TryGetProperty("params", out var parameters)
            || !parameters.TryGetProperty("turn", out var turn)) return CisAgentRunStates.InvalidEvidence;
        return Text(turn, "status") switch
        {
            "completed" => CisAgentRunStates.Succeeded,
            "failed" => CisAgentRunStates.Failed,
            "interrupted" => CisAgentRunStates.Interrupted,
            _ => CisAgentRunStates.InvalidEvidence,
        };
    }

    private static bool IsContained(string root, string candidate)
    {
        try
        {
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var normalizedCandidate = Path.GetFullPath(candidate);
            return normalizedCandidate.Equals(normalizedRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                || normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private string? Probe(string[] arguments, string repositoryPath)
    {
        try
        {
            var start = StartInfo(repositoryPath);
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            var result = CisProcessSafety.Run(start, TimeSpan.FromSeconds(8), 16_384);
            if (result.TimedOut || result.OutputTruncated || result.ExitCode != 0) return null;
            return string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardError.Trim() : result.StandardOutput.Trim();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException) { return null; }
    }

    internal static string ResolveExecutable(string? configured, string? environmentPath,
        string? localAppData, bool windows)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim().Trim('"');
        var executableName = windows ? "codex.exe" : "codex";
        foreach (var segment in (environmentPath ?? string.Empty).Split(Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = segment.Trim('"');
            if (!Path.IsPathRooted(directory)) continue;
            try
            {
                var candidate = Path.GetFullPath(Path.Combine(directory, executableName));
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Ignore malformed PATH entries and continue deterministic discovery.
            }
        }

        if (windows && !string.IsNullOrWhiteSpace(localAppData))
        {
            try
            {
                var desktopRoot = Path.GetFullPath(Path.Combine(localAppData, "OpenAI", "Codex", "bin"));
                if (Directory.Exists(desktopRoot))
                {
                    var desktopExecutable = Directory.EnumerateDirectories(desktopRoot)
                        .Select(directory => Path.Combine(directory, executableName))
                        .Where(File.Exists)
                        .Select(path => new FileInfo(path))
                        .OrderByDescending(file => file.LastWriteTimeUtc)
                        .ThenByDescending(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                        .Select(file => file.FullName)
                        .FirstOrDefault();
                    if (desktopExecutable is not null) return desktopExecutable;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                               or ArgumentException or NotSupportedException or PathTooLongException)
            {
                // The subsequent executable probe reports a stable missing-prerequisite result.
            }
        }
        return executableName;
    }

    private ProcessStartInfo StartInfo(string workingDirectory) => CreateStartInfo(_executable, workingDirectory);
    internal static ProcessStartInfo CreateStartInfo(string executable, string workingDirectory) => new(executable)
    {
        WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false),
        StandardErrorEncoding = new UTF8Encoding(false),
    };
    private static void ApplyEnvironment(ProcessStartInfo start, IReadOnlyDictionary<string, string> environment)
    { start.Environment.Clear(); foreach (var pair in environment) start.Environment[pair.Key] = pair.Value; }
    private static T Wait<T>(Task<T> task, CancellationToken cancellationToken)
        => task.WaitAsync(cancellationToken).GetAwaiter().GetResult();
    private static bool TryThreadId(JsonElement root, out string? id)
    { id = null; if (!root.TryGetProperty("result", out var result) || !result.TryGetProperty("thread", out var thread)) return false; id = Text(thread, "id"); return id is not null; }
    private static string ErrorMessage(JsonElement error) => Text(error, "message") ?? "Codex App Server returned an error.";
    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long? Number(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : null;
    private static string FirstLine(string value) => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? value.Trim();
    private static string Limit(string value) => value.Length <= MaximumRawJsonCharacters ? value : value[..MaximumRawJsonCharacters];
    internal static string Redact(string value)
    {
        var result = System.Text.RegularExpressions.Regex.Replace(value,
            "(?i)([\\\"']?(?:api[-_ ]?key|token|authorization|password)[\\\"']?\\s*[:=]\\s*)[^\\r\\n,;}]+",
            "$1[REDACTED]");
        result = System.Text.RegularExpressions.Regex.Replace(result, "(?i)(bearer\\s+)[A-Za-z0-9._~+/-]+=*", "$1[REDACTED]");
        return Limit(result);
    }
    private static string? FailureKind(CisAgentProcessResult result, string standardError)
        => result.Cancelled ? "cancellation"
            : result.TimedOut ? result.TimeoutKind ?? "timeout"
            : result.OutputTruncated ? "invalid-evidence"
            : result.ExitCode == 0 ? null
            : standardError.Contains("permission", StringComparison.OrdinalIgnoreCase)
                || standardError.Contains("approval", StringComparison.OrdinalIgnoreCase)
                || standardError.Contains("sandbox", StringComparison.OrdinalIgnoreCase)
                ? "permission-required" : "provider-failure";
    private static CisAgentProviderExecutionResult Failure(string kind, string message, string? session = null)
        => new(CisAgentRunStates.Failed, null, session, string.Empty, [], [], [], null, null, null, kind, [message]);
    private static void TryKill(Process process)
    { try { if (!process.HasExited) process.Kill(true); process.WaitForExit(5_000); } catch (Exception exception) when (exception is InvalidOperationException or Win32Exception) { } }
}

internal sealed record CodexApprovalEvaluation(bool Approved, string Capability, string Target);
