using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Workflow;

public sealed record WorkflowStep(string Id, string Executable, IReadOnlyList<string> Arguments, IReadOnlyList<string> DependsOn,
    bool ContinueOnFailure, int TimeoutSeconds, string WorkingDirectory = ".", IReadOnlyList<string>? TestSuites = null)
{
    public IReadOnlyList<string> Suites => TestSuites ?? [];
}
public sealed record WorkflowDefinition(string Id, string Path, string Digest, IReadOnlyList<WorkflowStep> Steps);
public sealed record WorkflowStepState(string Id, string Status, int? ExitCode, string StartedAtUtc, string? CompletedAtUtc,
    long DurationMilliseconds, string OutputPath, string? Error, int Attempt = 1, string FailureKind = "none");
public sealed record WorkflowRunState(int SchemaVersion, string RunId, string WorkflowId, string WorkflowDigest, string Status,
    string CreatedAtUtc, string UpdatedAtUtc, IReadOnlyList<WorkflowStepState> Steps);
public sealed record WorkflowResult(string Status, string? RepositoryPath, string? RunId, WorkflowDefinition? Workflow,
    WorkflowRunState? Run, IReadOnlyList<WorkflowDefinition> Workflows, IReadOnlyList<string> Diagnostics, bool Applied)
{
    public int ExitCode => Diagnostics.Any(x => x.StartsWith("ERROR:", StringComparison.Ordinal)) || Run?.Status == "failed" ? 4 : 0;
}

public sealed class WorkflowService
{
    public const string RunsPath = ".cis/local/workflows";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ICisRepositoryContextResolver _resolver; private readonly Func<DateTimeOffset> _clock;
    public WorkflowService(ICisRepositoryContextResolver resolver, Func<DateTimeOffset>? clock = null) { _resolver = resolver; _clock = clock ?? (() => DateTimeOffset.UtcNow); }

    public WorkflowResult List(string repositoryPath)
    { var context = Resolve(repositoryPath, out var d); var all = context is null ? [] : Discover(context, d); return New(context, d.Count == 0 ? "listed" : "invalid", null, null, null, all, d, false); }
    public WorkflowResult Describe(string repositoryPath, string id)
    { var context = Resolve(repositoryPath, out var d); var all = context is null ? [] : Discover(context, d); var workflow = all.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); if (context is not null && workflow is null) d.Add($"ERROR: Unknown workflow '{id}'."); return New(context, d.Count == 0 ? "described" : "invalid", null, workflow, null, all, d, false); }

    public WorkflowResult Run(string repositoryPath, string id, string? requestedRunId)
    {
        var described = Describe(repositoryPath, id); if (described.RepositoryPath is null || described.Workflow is null || described.Diagnostics.Count > 0) return described;
        var context = _resolver.Resolve(described.RepositoryPath).Context!; var workflow = described.Workflow; var d = new List<string>();
        Validate(workflow, d); if (d.Count > 0) return New(context, "invalid", null, workflow, null, described.Workflows, d, false);
        var runId = string.IsNullOrWhiteSpace(requestedRunId) ? $"{workflow.Id}-{_clock():yyyyMMddHHmmss}" : SafeId(requestedRunId, d);
        if (d.Count > 0) return New(context, "invalid", runId, workflow, null, described.Workflows, d, false);
        var runDirectory = Path.Combine(context.RepositoryPath, RunsPath.Replace('/', Path.DirectorySeparatorChar), runId!); Directory.CreateDirectory(runDirectory);
        var statePath = Path.Combine(runDirectory, "state.json"); var now = _clock().ToUniversalTime().ToString("O");
        var state = File.Exists(statePath) ? ReadState(statePath, d) : new WorkflowRunState(2, runId!, workflow.Id, workflow.Digest, "running", now, now, []);
        if (state is null || d.Count > 0) return New(context, "invalid-state", runId, workflow, state, described.Workflows, d, false);
        if (state.WorkflowDigest != workflow.Digest) return New(context, "definition-changed", runId, workflow, state, described.Workflows,
            ["ERROR: Workflow changed after this run began; start a new run ID."], false);
        var states = state.Steps.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase); var applied = false;
        foreach (var step in workflow.Steps)
        {
            if (states.TryGetValue(step.Id, out var existing) && existing.Status == "succeeded") continue;
            if (step.DependsOn.Any(dep => !states.TryGetValue(dep, out var dependency) || dependency.Status != "succeeded"))
            { states[step.Id] = new(step.Id, "blocked", null, now, now, 0, $"{step.Id}.log", "A dependency did not succeed.", existing?.Attempt ?? 1, "product"); continue; }
            var attempt = states.TryGetValue(step.Id, out existing) ? existing.Attempt + 1 : 1;
            var stepState = Execute(context.RepositoryPath, runDirectory, step, attempt); states[step.Id] = stepState; applied = true;
            state = state with { Status = "running", UpdatedAtUtc = _clock().ToUniversalTime().ToString("O"), Steps = workflow.Steps.Where(x => states.ContainsKey(x.Id)).Select(x => states[x.Id]).ToArray() };
            WriteState(statePath, state);
            if (stepState.Status == "failed" && !step.ContinueOnFailure) break;
        }
        var finalSteps = workflow.Steps.Where(x => states.ContainsKey(x.Id)).Select(x => states[x.Id]).ToArray();
        var finalStatus = finalSteps.Any(x => x.Status == "failed" || x.Status == "blocked") ? "failed" : finalSteps.Length == workflow.Steps.Count ? "succeeded" : "running";
        state = state with { Status = finalStatus, UpdatedAtUtc = _clock().ToUniversalTime().ToString("O"), Steps = finalSteps }; WriteState(statePath, state);
        return New(context, finalStatus, runId, workflow, state, described.Workflows, d, applied);
    }

    public WorkflowResult Status(string repositoryPath, string runId) => ReadRun(repositoryPath, runId, "reported");
    public WorkflowResult Log(string repositoryPath, string runId) => ReadRun(repositoryPath, runId, "logged");
    public WorkflowResult Summarise(string repositoryPath, string runId)
    {
        var result = ReadRun(repositoryPath, runId, "summarised"); if (result.RepositoryPath is null || result.Run is null || result.Diagnostics.Count > 0) return result;
        var directory = Path.Combine(result.RepositoryPath, RunsPath.Replace('/', Path.DirectorySeparatorChar), result.Run.RunId);
        var markdown = $"# Workflow run {result.Run.RunId}\n\n- Workflow: `{result.Run.WorkflowId}`\n- Status: **{result.Run.Status}**\n- Updated: {result.Run.UpdatedAtUtc}\n\n| Step | Status | Exit code | Duration ms | Log |\n|---|---|---:|---:|---|\n" +
            string.Join("\n", result.Run.Steps.Select(x => $"| {x.Id} | {x.Status} | {x.ExitCode?.ToString() ?? "-"} | {x.DurationMilliseconds} | {x.OutputPath} |")) + "\n";
        File.WriteAllText(Path.Combine(directory, "summary.md"), markdown); return result with { Applied = true };
    }

    private WorkflowResult ReadRun(string repositoryPath, string runId, string status)
    {
        var context = Resolve(repositoryPath, out var d); if (context is null) return New(null, "invalid-repository", runId, null, null, [], d, false);
        var safe = SafeId(runId, d); var path = safe is null ? "" : Path.Combine(context.RepositoryPath, RunsPath.Replace('/', Path.DirectorySeparatorChar), safe, "state.json");
        WorkflowRunState? state = null; if (d.Count == 0 && !File.Exists(path)) d.Add($"ERROR: Unknown workflow run '{runId}'."); else if (d.Count == 0) state = ReadState(path, d);
        return New(context, d.Count == 0 ? status : "invalid", runId, null, state, Discover(context, d), d, false);
    }

    private WorkflowStepState Execute(string repository, string runDirectory, WorkflowStep step, int attempt)
    {
        var started = _clock().ToUniversalTime(); var logName = attempt == 1 ? step.Id + ".log" : $"{step.Id}.attempt-{attempt}.log"; var logPath = Path.Combine(runDirectory, logName);
        try
        {
            var workingDirectory = Path.GetFullPath(Path.Combine(repository, step.WorkingDirectory.Replace('/', Path.DirectorySeparatorChar)));
            var repositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository));
            if (!workingDirectory.Equals(repositoryRoot, StringComparison.OrdinalIgnoreCase)
                && !workingDirectory.StartsWith(repositoryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return State("failed", null, "Working directory escapes the repository.", "missing-prerequisite");
            if (!Directory.Exists(workingDirectory))
                return State("failed", null, "Working directory does not exist.", "missing-prerequisite");
            using var process = new Process { StartInfo = new ProcessStartInfo(step.Executable) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
            foreach (var arg in step.Arguments) process.StartInfo.ArgumentList.Add(arg); process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(step.TimeoutSeconds * 1000)) { process.Kill(true); File.WriteAllText(logPath, "Workflow step timed out."); return State("failed", null, "Timed out.", "timeout"); }
            Task.WaitAll(stdout, stderr); var output = stdout.Result + stderr.Result; File.WriteAllText(logPath, output);
            return State(process.ExitCode == 0 ? "succeeded" : "failed", process.ExitCode,
                process.ExitCode == 0 ? null : $"Exited with {process.ExitCode}.",
                process.ExitCode == 0 ? "none" : ClassifyFailure(output));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception) { File.WriteAllText(logPath, ex.Message); return State("failed", null, ex.Message, ex is System.ComponentModel.Win32Exception ? "missing-prerequisite" : "infrastructure"); }
        WorkflowStepState State(string status, int? exit, string? error, string failureKind) { var completed = _clock().ToUniversalTime(); return new(step.Id, status, exit, started.ToString("O"), completed.ToString("O"), (long)(completed - started).TotalMilliseconds, logName, error, attempt, failureKind); }
    }

    private static IReadOnlyList<WorkflowDefinition> Discover(CisRepositoryContext context, List<string> diagnostics)
    {
        var root = Path.Combine(context.DocumentationPath, "workflows"); if (!Directory.Exists(root)) return [];
        var output = new List<WorkflowDefinition>(); foreach (var path in Directory.EnumerateFiles(root, "*.md"))
        { try { output.Add(Parse(context, path)); } catch (InvalidOperationException ex) { diagnostics.Add($"ERROR: {Path.GetFileName(path)}: {ex.Message}"); } }
        return output.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
    }
    private static WorkflowDefinition Parse(CisRepositoryContext context, string path)
    {
        var text = File.ReadAllText(path); var steps = new List<WorkflowStep>(); Dictionary<string, int>? columns = null;
        foreach (var line in text.Split('\n'))
        { if (!line.TrimStart().StartsWith('|')) continue; var c = line.Trim().Trim('|').Split('|').Select(x => x.Trim()).ToArray();
          if (c.Length < 5) continue;
          if (c[0].Equals("Step", StringComparison.OrdinalIgnoreCase)) { columns = c.Select((value,index)=>(value,index)).ToDictionary(item=>item.value,item=>item.index,StringComparer.OrdinalIgnoreCase); continue; }
          if (c.All(x => x.All(ch => ch is '-' or ':' or ' '))) continue;
          columns ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Step"]=0,["Command"]=1,["Depends on"]=2,["Continue on failure"]=3,["Timeout seconds"]=4 };
          string Value(string name, string fallback="") => columns.TryGetValue(name, out var index) && index < c.Length ? c[index] : fallback;
          var id=Value("Step"); var tokens = Tokenize(Value("Command")); if (tokens.Count == 0) throw new InvalidOperationException($"Step '{id}' has no command.");
          steps.Add(new(id, tokens[0], tokens.Skip(1).ToArray(), Split(Value("Depends on")), Value("Continue on failure").Equals("yes", StringComparison.OrdinalIgnoreCase), int.TryParse(Value("Timeout seconds"), out var timeout) ? Math.Clamp(timeout, 1, 3600) : 600, Value("Working directory", "."), Split(Value("Test suites")))); }
        return new(Path.GetFileNameWithoutExtension(path), Path.GetRelativePath(context.RepositoryPath, path).Replace(Path.DirectorySeparatorChar, '/'), Sha(text), steps);
    }
    private static IReadOnlyList<string> Tokenize(string value)
    { var result = new List<string>(); var current = new StringBuilder(); var quoted = false; foreach (var ch in value) { if (ch == '"') { quoted = !quoted; continue; } if (char.IsWhiteSpace(ch) && !quoted) { if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); } } else current.Append(ch); } if (quoted) throw new InvalidOperationException("Command contains an unclosed quote."); if (current.Length > 0) result.Add(current.ToString()); return result; }
    private static string[] Split(string value) => value is "" or "-" ? [] : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static void Validate(WorkflowDefinition workflow, List<string> d)
    { if (workflow.Steps.Count == 0) d.Add("ERROR: Workflow has no steps."); var ids = workflow.Steps.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase); if (ids.Count != workflow.Steps.Count) d.Add("ERROR: Workflow step IDs must be unique."); foreach (var step in workflow.Steps) { foreach (var dep in step.DependsOn) if (!ids.Contains(dep)) d.Add($"ERROR: Step '{step.Id}' has unknown dependency '{dep}'."); if (string.IsNullOrWhiteSpace(step.WorkingDirectory)) d.Add($"ERROR: Step '{step.Id}' has no working directory."); } }
    private static string ClassifyFailure(string output)
    {
        if (new[] { "ENOSPC", "out of memory", "ENOMEM", "worker process", "process exited unexpectedly", "docker daemon", "cannot connect to the Docker", "resource temporarily unavailable" }.Any(marker => output.Contains(marker, StringComparison.OrdinalIgnoreCase))) return "infrastructure";
        if (new[] { "command not found", "is not recognized", "No such file or directory", "SDK not found", "Cannot find module" }.Any(marker => output.Contains(marker, StringComparison.OrdinalIgnoreCase))) return "missing-prerequisite";
        return "product";
    }
    private static string? SafeId(string value, List<string> d) { if (string.IsNullOrWhiteSpace(value) || value.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_'))) { d.Add("ERROR: Run ID may contain only letters, digits, hyphen, and underscore."); return null; } return value; }
    private static WorkflowRunState? ReadState(string path, List<string> d) { try { return JsonSerializer.Deserialize<WorkflowRunState>(File.ReadAllText(path), JsonOptions) ?? throw new JsonException("empty"); } catch (JsonException ex) { d.Add($"ERROR: Workflow state is invalid: {ex.Message}"); return null; } }
    private static void WriteState(string path, WorkflowRunState state) { var tmp = path + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(state, JsonOptions)); File.Move(tmp, path, true); }
    private CisRepositoryContext? Resolve(string path, out List<string> d) { var r = _resolver.Resolve(path); d = r.Errors.Select(x => "ERROR: " + x).ToList(); return r.Context; }
    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static WorkflowResult New(CisRepositoryContext? c, string s, string? id, WorkflowDefinition? w, WorkflowRunState? r, IReadOnlyList<WorkflowDefinition> all, IReadOnlyList<string> d, bool a) => new(s, c?.RepositoryPath, id, w, r, all, d, a);
}
