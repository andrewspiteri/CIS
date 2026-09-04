using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Providers.Agent.Claude;

public sealed class ClaudeAgentProvider : ICisAgentProvider, ICisAgentProviderAuthenticator
{
    private const int MaximumRawJsonCharacters = 64 * 1024;
    private const int MaximumProtocolLineCharacters = 4 * 1024 * 1024;
    private const string BrdReviewJsonSchema = """
        {"type":"object","additionalProperties":false,"required":["summary","changedFiles","validations","evidence","review"],"properties":{"summary":{"type":"string","minLength":1},"changedFiles":{"type":"array","maxItems":0,"items":{"type":"string"}},"validations":{"type":"array","items":{"type":"string"}},"evidence":{"type":"array","items":{"type":"string"}},"review":{"type":"object","additionalProperties":false,"required":["recommendation","strengths","findings"],"properties":{"recommendation":{"type":"string","enum":["ready","revise","blocked"]},"strengths":{"type":"array","items":{"type":"string","minLength":1}},"findings":{"type":"array","maxItems":100,"items":{"type":"object","additionalProperties":false,"required":["id","severity","category","location","observation","recommendation"],"properties":{"id":{"type":"string","pattern":"^BRD-REV-[0-9]{3}$"},"severity":{"type":"string","enum":["blocking","major","minor","observation"]},"category":{"type":"string","minLength":1},"location":{"type":"string","minLength":1},"observation":{"type":"string","minLength":1},"recommendation":{"type":"string","minLength":1}}}}}}}}
        """;
    private readonly string _executable;

    public ClaudeAgentProvider() : this(ResolveExecutable(
        Environment.GetEnvironmentVariable("CIS_CLAUDE_EXECUTABLE"),
        Environment.GetEnvironmentVariable("PATH"),
        Environment.GetEnvironmentVariable("USERPROFILE"),
        OperatingSystem.IsWindows())) { }
    public ClaudeAgentProvider(string executable) => _executable = executable;

    public CisAgentProviderDescriptor Descriptor { get; } = new(
        "claude", "Claude Code", "local-cli", true,
        ["stream-json"],
        [CisAgentRunModes.Plan, CisAgentRunModes.Implement, CisAgentRunModes.Review],
        [CisAgentPermissions.ReadOnly, CisAgentPermissions.WorkspaceWrite],
        true, false,
        "Uses Claude Code print mode with structured streaming JSON and an explicit predeclared permission mode.");

    public CisAgentProviderDiagnosis Diagnose(string repositoryPath)
    {
        var version = Probe(["--version"], repositoryPath);
        if (version is null)
            return new(Descriptor.Id, "executable-missing", false, _executable, null, false, [],
                [$"Claude executable '{_executable}' is unavailable or did not return a version."]);
        var auth = Probe(["auth", "status", "--json"], repositoryPath);
        var authenticated = AuthenticationConfirmed(auth);
        return new(Descriptor.Id, authenticated ? "ready" : "authentication-unavailable", authenticated,
            _executable, FirstLine(version), authenticated,
            ["stream-json", "resume", "predeclared-permissions", "authentication:browser",
                "authentication:console", "authentication:sso"],
            authenticated ? [] : ["Claude Code is installed but no provider-native authenticated session was confirmed."]);
    }

    public CisAgentProviderAuthenticationDescriptor Authentication { get; } = new(
        ["browser", "console", "sso"], "browser",
        "Starts Claude provider-native browser authentication. CIS never receives or stores credentials.");

    public CisAgentProviderAuthenticationResult Authenticate(
        CisAgentProviderAuthenticationRequest request,
        Action<CisAgentProviderEvent> onEvent,
        CancellationToken cancellationToken)
    {
        if (!Authentication.Methods.Contains(request.Method, StringComparer.OrdinalIgnoreCase))
            return new("unsupported-method", null,
                [$"Claude authentication method '{request.Method}' is unsupported. Use browser, console, or sso."]);
        var start = new ProcessStartInfo(_executable)
        {
            WorkingDirectory = request.RepositoryPath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        ConfigureAuthenticationArguments(start, request.Method);
        try
        {
            var result = CisAgentProcessRunner.RunLines(start, string.Empty, request.Timeout,
                line => onEvent(new("authentication-output", Redact(line), "claude/auth/login")),
                line => onEvent(new("authentication-output", Redact(line), "claude/auth/login")),
                cancellationToken,
                onStarted: (id, started) => onEvent(new("process-started", "Claude provider-native authentication started.",
                    "claude/auth/login", ProcessId: id, ProcessStartedAtUtc: started.ToString("O"))));
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

    internal static void ConfigureAuthenticationArguments(ProcessStartInfo start, string method)
    {
        start.ArgumentList.Add("auth");
        start.ArgumentList.Add("login");
        if (method.Equals("browser", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add("--claudeai");
        else if (method.Equals("console", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add("--console");
        else if (method.Equals("sso", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add("--sso");
    }

    internal static bool AuthenticationConfirmed(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        try
        {
            using var document = JsonDocument.Parse(status);
            var root = document.RootElement;
            return Boolean(root, "loggedIn") || Boolean(root, "authenticated");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public CisAgentProviderExecutionResult Execute(CisAgentExecutionRequest request, Action<CisAgentProviderEvent> onEvent, CancellationToken cancellationToken)
    {
        if (request.Transport != "stream-json") return Failure("unsupported-transport", $"Claude transport '{request.Transport}' is unsupported.");
        var start = new ProcessStartInfo(_executable) { WorkingDirectory = request.WorkingDirectory, UseShellExecute = false, CreateNoWindow = true };
        ConfigureExecutionArguments(start, request);
        if (request.ResumeSessionId is not null) { start.ArgumentList.Add("--resume"); start.ArgumentList.Add(request.ResumeSessionId); }
        start.Environment.Clear();
        foreach (var pair in request.Environment) start.Environment[pair.Key] = pair.Value;

        string? session = null;
        var summary = new StringBuilder();
        long? input = null, output = null;
        decimal? cost = null;
        var diagnostics = new List<string>();
        var invalid = false;
        CisAgentProcessResult process;
        try
        {
            process = CisAgentProcessRunner.RunLines(start, request.Prompt, request.Timeout, line =>
            {
                var parsed = ParseEvent(line, ref session, summary, ref input, ref output, ref cost);
                if (parsed.Kind == "invalid-provider-event") invalid = true;
                onEvent(parsed);
            }, line => onEvent(new("provider-stderr", Redact(line))), cancellationToken,
                onStarted: (id, started) => onEvent(new("process-started", $"Claude process {id} started.", ProcessId: id, ProcessStartedAtUtc: started.ToString("O"))),
                startupTimeout: request.StartupTimeout, idleTimeout: request.IdleTimeout);
        }
        catch (Win32Exception exception) { return Failure("missing-prerequisite", Redact(exception.Message)); }

        if (process.OutputTruncated) diagnostics.Add("Provider output exceeded the configured bound.");
        if (!string.IsNullOrWhiteSpace(process.StandardError)) diagnostics.Add(Redact(process.StandardError));
        var status = process.Cancelled ? CisAgentRunStates.Cancelled
            : process.TimedOut ? CisAgentRunStates.TimedOut
            : process.OutputTruncated || invalid ? CisAgentRunStates.InvalidEvidence
            : process.ExitCode == 0 ? CisAgentRunStates.Succeeded : CisAgentRunStates.Failed;
        return new(status, process.ExitCode, session, summary.ToString().Trim(), [], [], [], input, output, cost,
            process.Cancelled ? "cancellation" : process.TimedOut ? process.TimeoutKind ?? "timeout" : process.OutputTruncated || invalid ? "invalid-evidence" : process.ExitCode == 0 ? null
                : process.StandardError.Contains("permission", StringComparison.OrdinalIgnoreCase) || process.StandardError.Contains("approval", StringComparison.OrdinalIgnoreCase)
                    ? "permission-required" : "provider-failure",
            diagnostics);
    }

    internal static CisAgentProviderEvent ParseEvent(string line, ref string? session, StringBuilder summary, ref long? input, ref long? output, ref decimal? cost)
    {
        if (line.Length > MaximumProtocolLineCharacters)
            return new("invalid-provider-event", "Claude streaming event exceeded the safe parse limit.");
        var rawEventTruncated = line.Length > MaximumRawJsonCharacters;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = Text(root, "type") ?? "unknown";
            var subtype = Text(root, "subtype");
            session ??= Text(root, "session_id") ?? Text(root, "sessionId");
            if (root.TryGetProperty("usage", out var usage))
            {
                input = Number(usage, "input_tokens") ?? input;
                output = Number(usage, "output_tokens") ?? output;
            }
            if (root.TryGetProperty("total_cost_usd", out var total) && total.TryGetDecimal(out var amount)) cost = amount;
            if (type == "result")
            {
                var result = root.TryGetProperty("structured_output", out var structured)
                    && structured.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                    ? structured.GetRawText()
                    : Text(root, "result");
                if (!string.IsNullOrWhiteSpace(result)) { summary.Clear(); summary.Append(result); }
            }
            else if (type == "assistant" && root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in content.EnumerateArray())
                    if (Text(item, "type") == "text" && Text(item, "text") is { Length: > 0 } text)
                    { if (summary.Length > 0) summary.AppendLine(); summary.Append(text); }
            }
            var eventKind = type == "system" && subtype == "thinking_tokens" ? "provider-heartbeat" : "provider-event";
            var eventMessage = type switch
            {
                "rate_limit_event" => DescribeRateLimit(root),
                "system" when subtype == "thinking_tokens" => DescribeThinking(root),
                "assistant" => "Claude produced assistant output.",
                "user" => "Claude tool result received.",
                "result" => "Claude completed the request.",
                _ => subtype is null ? type : $"{type}/{subtype}",
            };
            if (rawEventTruncated) eventMessage += " (raw payload truncated)";
            return new(eventKind, eventMessage, type, session, Limit(line), InputTokens: input, OutputTokens: output, Cost: cost);
        }
        catch (JsonException) { return new("invalid-provider-event", "Claude emitted malformed streaming JSON."); }
    }

    internal static void ConfigureExecutionArguments(ProcessStartInfo start, CisAgentExecutionRequest request)
    {
        start.ArgumentList.Add("-p");
        start.ArgumentList.Add("--input-format"); start.ArgumentList.Add("text");
        start.ArgumentList.Add("--output-format"); start.ArgumentList.Add("stream-json");
        start.ArgumentList.Add("--verbose");
        start.ArgumentList.Add("--permission-mode");
        start.ArgumentList.Add(request.Permission == CisAgentPermissions.WorkspaceWrite ? "acceptEdits" : "dontAsk");
        if (request.Permission == CisAgentPermissions.ReadOnly)
        {
            start.ArgumentList.Add("--safe-mode");
            start.ArgumentList.Add("--restricted");
            start.ArgumentList.Add("--disable-slash-commands");
            start.ArgumentList.Add("--tools"); start.ArgumentList.Add("Read,Glob,Grep");
        }
        if (request.Mode == CisAgentRunModes.Review)
        {
            start.ArgumentList.Add("--json-schema"); start.ArgumentList.Add(BrdReviewJsonSchema);
            start.ArgumentList.Add("--no-session-persistence");
        }
    }

    private string? Probe(string[] arguments, string repositoryPath)
    {
        try
        {
            var start = new ProcessStartInfo(_executable) { WorkingDirectory = repositoryPath, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            var result = CisProcessSafety.Run(start, TimeSpan.FromSeconds(8), 16_384);
            if (result.TimedOut || result.OutputTruncated || result.ExitCode != 0) return null;
            return string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardError.Trim() : result.StandardOutput.Trim();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException) { return null; }
    }

    internal static string ResolveExecutable(string? configured, string? environmentPath,
        string? userProfile, bool windows)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim().Trim('"');
        var executableName = windows ? "claude.exe" : "claude";
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

        if (windows && !string.IsNullOrWhiteSpace(userProfile))
        {
            try
            {
                var extensionRoots = new[]
                {
                    Path.Combine(userProfile, ".vscode", "extensions"),
                    Path.Combine(userProfile, ".vscode-insiders", "extensions"),
                    Path.Combine(userProfile, ".cursor", "extensions"),
                    Path.Combine(userProfile, ".windsurf", "extensions"),
                };
                var extensionExecutable = extensionRoots
                    .Where(Directory.Exists)
                    .SelectMany(root => Directory.EnumerateDirectories(root, "anthropic.claude-code-*",
                        SearchOption.TopDirectoryOnly))
                    .Select(directory => Path.Combine(directory, "resources", "native-binary", executableName))
                    .Where(File.Exists)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ThenByDescending(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                    .Select(file => file.FullName)
                    .FirstOrDefault();
                if (extensionExecutable is not null) return extensionExecutable;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                               or ArgumentException or NotSupportedException or PathTooLongException)
            {
                // The subsequent executable probe reports a stable missing-prerequisite result.
            }
        }
        return executableName;
    }

    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long? Number(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : null;
    private static bool Boolean(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static string DescribeRateLimit(JsonElement root)
    {
        if (!root.TryGetProperty("rate_limit_info", out var info) || info.ValueKind != JsonValueKind.Object)
            return "Claude quota status reported.";
        var status = Text(info, "status");
        return status?.ToLowerInvariant() switch
        {
            "allowed" => "Claude quota check allowed.",
            "rejected" or "denied" or "blocked" or "exceeded" or "rate_limited" => "Claude rate limit reached.",
            _ => $"Claude quota status: {status ?? "unknown"}.",
        };
    }
    private static string DescribeThinking(JsonElement root)
        => Number(root, "estimated_tokens") is { } estimated
            ? $"Claude is working ({estimated:N0} estimated thinking tokens)."
            : "Claude is working.";
    private static string FirstLine(string value) => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? value.Trim();
    private static string Limit(string value) => value.Length <= MaximumRawJsonCharacters ? value : value[..MaximumRawJsonCharacters];
    private static string Redact(string value)
    {
        var redacted = Regex.Replace(value,
            "(?i)([\\\"']?(?:api[-_ ]?key|token|authorization|password)[\\\"']?\\s*[:=]\\s*)[^\\r\\n,;}]+",
            "$1[REDACTED]");
        return Limit(Regex.Replace(redacted, "(?i)(bearer\\s+)[A-Za-z0-9._~+/-]+=*", "$1[REDACTED]"));
    }
    private static CisAgentProviderExecutionResult Failure(string kind, string message)
        => new(CisAgentRunStates.Failed, null, null, string.Empty, [], [], [], null, null, null, kind, [message]);
}
