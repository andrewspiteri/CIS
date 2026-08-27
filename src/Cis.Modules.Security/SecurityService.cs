using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Workflow;

namespace Cis.Modules.Security;

public sealed record SecurityResult(
    string Status,
    string? RepositoryPath,
    string? RunId,
    IReadOnlyList<SecuritySuiteProfile> Suites,
    SecurityRunManifest? Manifest,
    IReadOnlyList<AcceptedSecurityFinding> Acceptances,
    string? SummaryPath,
    IReadOnlyList<string> Diagnostics,
    bool Applied)
{
    public int ExitCode => Diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal))
        || Manifest?.Status is "failed" or "invalid-evidence" ? 4 : 0;
}

public sealed partial class SecurityService
{
    public const string LocalRoot = ".cis/local/security";
    private const long MaximumArtifactBytes = 50L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HashSet<string> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        "static-analysis", "sast", "secret", "dependency", "filesystem", "configuration", "image", "dast",
    };
    private readonly ICisRepositoryContextResolver _resolver;
    private readonly WorkflowService _workflows;
    private readonly ICisTextGenerationService _textGeneration;
    private readonly IReadOnlyDictionary<string, ICisSecurityResultAdapter> _adapters;

    public SecurityService(ICisRepositoryContextResolver resolver, WorkflowService workflows,
        IEnumerable<ICisSecurityResultAdapter> adapters, ICisTextGenerationService? textGeneration = null)
    {
        _resolver = resolver;
        _workflows = workflows;
        _textGeneration = textGeneration ?? new UnavailableTextGenerationService();
        _adapters = adapters.ToDictionary(item => item.Format, StringComparer.OrdinalIgnoreCase);
    }

    public SecurityResult Inventory(string repositoryPath)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return Result(null, null, [], null, [], null, diagnostics, false, "invalid-repository");
        var suites = ReadProfile(context, diagnostics);
        var acceptances = ReadAcceptances(context, diagnostics, requireFile: false);
        if (diagnostics.Any(IsError)) return Result(context, null, suites, null, acceptances, null, diagnostics, false, "invalid");
        var root = LocalPath(context.RepositoryPath);
        Directory.CreateDirectory(root);
        Write(Path.Combine(root, "inventory.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            repositoryId = context.RepositoryId,
            repositoryPath = context.RepositoryPath,
            profileDigest = ProfileDigest(context),
            generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            suites,
        }, JsonOptions));
        return Result(context, null, suites, null, acceptances, null, diagnostics, true, "inventoried");
    }

    public SecurityResult Validate(string repositoryPath, bool strict)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return Result(null, null, [], null, [], null, diagnostics, false, "invalid-repository");
        var suites = ReadProfile(context, diagnostics);
        ValidateProfile(context, suites, strict, diagnostics);
        var acceptances = ReadAcceptances(context, diagnostics, requireFile: strict);
        return Result(context, null, suites, null, acceptances, null, diagnostics, false,
            diagnostics.Any(IsError) ? "invalid" : "valid");
    }

    public SecurityResult ValidateExceptions(string repositoryPath, bool strict)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return Result(null, null, [], null, [], null, diagnostics, false, "invalid-repository");
        var acceptances = ReadAcceptances(context, diagnostics, requireFile: strict);
        return Result(context, null, [], null, acceptances, null, diagnostics, false,
            diagnostics.Any(IsError) ? "invalid" : "valid");
    }

    public SecurityResult Reconcile(string repositoryPath, string runId)
    {
        var validation = Validate(repositoryPath, true);
        if (validation.RepositoryPath is null || validation.Diagnostics.Any(IsError)) return validation with { RunId = runId };
        var context = _resolver.Resolve(validation.RepositoryPath).Context!;
        var diagnostics = validation.Diagnostics.ToList();
        var workflow = _workflows.Status(context.RepositoryPath, runId);
        if (workflow.Run is null || workflow.Diagnostics.Count > 0)
        {
            diagnostics.AddRange(workflow.Diagnostics.Select(item => item.StartsWith("ERROR:", StringComparison.Ordinal) ? item : "ERROR: " + item));
            return Result(context, runId, validation.Suites, null, validation.Acceptances, null, diagnostics, false, "invalid-run");
        }
        var definition = _workflows.Describe(context.RepositoryPath, workflow.Run.WorkflowId).Workflow;
        if (definition is null)
        {
            diagnostics.Add($"ERROR: Workflow definition is unavailable: {workflow.Run.WorkflowId}");
            return Result(context, runId, validation.Suites, null, validation.Acceptances, null, diagnostics, false, "invalid-run");
        }

        var executions = new List<SecuritySuiteExecution>();
        foreach (var suite in validation.Suites)
        {
            var steps = definition.Steps.Where(step => step.Suites.Contains(suite.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (steps.Length == 0) { executions.Add(Unavailable(suite, "No workflow step is bound to this security suite.")); continue; }
            var states = workflow.Run.Steps.Where(state => steps.Any(step => step.Id.Equals(state.Id, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (states.Length == 0) { executions.Add(Unavailable(suite, "The bound workflow step did not run.")); continue; }
            var failed = states.LastOrDefault(state => state.Status is "failed" or "blocked");
            var resultPath = SafePath(context.RepositoryPath, suite.ResultPath, diagnostics, suite.Id, required: true);
            if (resultPath is null || !File.Exists(resultPath))
            {
                executions.Add(failed is not null ? Failed(suite, failed) : Invalid(suite, "Workflow completed without the declared scanner result."));
                continue;
            }
            if (new FileInfo(resultPath).Length > MaximumArtifactBytes)
            {
                executions.Add(Invalid(suite, "Scanner result exceeds the 50 MiB evidence limit."));
                continue;
            }
            if (!_adapters.TryGetValue(suite.ResultFormat, out var adapter))
            {
                executions.Add(Invalid(suite, $"No security result adapter is registered for '{suite.ResultFormat}'."));
                continue;
            }
            try
            {
                var parsed = ApplyAcceptances(adapter.Read(new(context.RepositoryPath, suite, resultPath)), suite, validation.Acceptances);
                if (failed is not null && parsed.Status is "passed" or "passed-with-findings")
                    parsed = parsed with { Status = "failed", FailureKind = ParseFailure(failed.FailureKind), Diagnostics = [.. parsed.Diagnostics, failed.Error ?? "Scanner command failed."] };
                executions.Add(parsed);
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
            {
                executions.Add(Invalid(suite, $"Scanner evidence is unreadable: {exception.Message}"));
            }
        }

        var revision = Revision(context.RepositoryPath);
        executions = executions.Select(execution =>
        {
            var suite = validation.Suites.Single(item => item.Id.Equals(execution.SuiteId, StringComparison.OrdinalIgnoreCase));
            var steps = definition.Steps.Where(step => step.Suites.Contains(suite.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
            var states = workflow.Run.Steps.Where(state => steps.Any(step => step.Id.Equals(state.Id, StringComparison.OrdinalIgnoreCase))).ToArray();
            var artifacts = execution.Artifacts.Select(item => Correlate(item, runId, null, suite, revision))
                .Concat(WorkflowArtifacts(context.RepositoryPath, runId, suite, states, revision, diagnostics))
                .DistinctBy(item => (item.Path, item.Digest, item.SuiteId, item.Attempt)).OrderBy(item => item.Path).ThenBy(item => item.Attempt).ToArray();
            return execution with { Artifacts = artifacts };
        }).ToList();
        var allArtifacts = executions.SelectMany(item => item.Artifacts).DistinctBy(item => (item.Path, item.Digest, item.SuiteId, item.Attempt)).ToArray();
        var status = executions.Any(item => item.Status is "failed" or "findings" or "invalid-evidence") ? "failed"
            : executions.Any(item => item.Status == "unavailable") ? "incomplete"
            : executions.Any(item => item.Status == "passed-with-findings") ? "passed-with-findings" : "passed";
        var manifest = new SecurityRunManifest(1, runId, workflow.Run.WorkflowId, workflow.Run.WorkflowDigest,
            context.RepositoryId, revision, ProfileDigest(context), $"{RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}",
            workflow.Run.CreatedAtUtc, workflow.Run.UpdatedAtUtc, status, executions, allArtifacts);
        var runRoot = Path.Combine(LocalPath(context.RepositoryPath), "runs", runId);
        Directory.CreateDirectory(runRoot);
        Write(Path.Combine(runRoot, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
        Write(Path.Combine(runRoot, "findings.json"), JsonSerializer.Serialize(executions.SelectMany(item => item.Findings), JsonOptions));
        Write(Path.Combine(runRoot, "artifacts.json"), JsonSerializer.Serialize(allArtifacts, JsonOptions));
        return Result(context, runId, validation.Suites, manifest, validation.Acceptances, null, diagnostics, true, "reconciled");
    }

    public SecurityResult Status(string repositoryPath, string? runId)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return Result(null, runId, [], null, [], null, diagnostics, false, "invalid-repository");
        runId = string.IsNullOrWhiteSpace(runId) ? LatestRun(context.RepositoryPath) : runId;
        if (string.IsNullOrWhiteSpace(runId))
        {
            diagnostics.Add("ERROR: No reconciled security run is available.");
            return Result(context, null, ReadProfile(context, diagnostics), null, ReadAcceptances(context, diagnostics, false), null, diagnostics, false, "missing-run");
        }
        var path = Path.Combine(LocalPath(context.RepositoryPath), "runs", runId, "manifest.json");
        if (!File.Exists(path)) diagnostics.Add($"ERROR: No security manifest was found for run '{runId}'.");
        SecurityRunManifest? manifest = null;
        if (diagnostics.Count == 0)
        {
            try { manifest = JsonSerializer.Deserialize<SecurityRunManifest>(File.ReadAllText(path), JsonOptions); }
            catch (JsonException exception) { diagnostics.Add("ERROR: Security manifest is invalid: " + exception.Message); }
        }
        return Result(context, runId, ReadProfile(context, diagnostics), manifest, ReadAcceptances(context, diagnostics, false), null,
            diagnostics, false, diagnostics.Any(IsError) ? "invalid" : "reported");
    }

    public SecurityResult Summarise(string repositoryPath, string runId, bool noLlm)
    {
        var status = Status(repositoryPath, runId);
        if (status.RepositoryPath is null || status.Manifest is null || status.Diagnostics.Any(IsError)) return status;
        var diagnostics = status.Diagnostics.ToList();
        var deterministic = DeterministicSummary(status.Manifest);
        var output = deterministic;
        string provider = "none", model = "none", generationStatus = noLlm ? "disabled" : "unavailable";
        var prompt = SecurityPrompt(status.Manifest);
        if (!noLlm)
        {
            var generated = _textGeneration.Generate(new(prompt, AllowRemote: false, TimeoutSeconds: 120, MaxOutputTokens: 700));
            generationStatus = generated.Status; provider = generated.Provider ?? "none"; model = generated.Model ?? "none";
            if (generated.IsSuccess && generated.IsLocal && !string.IsNullOrWhiteSpace(generated.Text))
                output = deterministic + "\n## Local AI triage\n\n" + SecurityEvidence.Redact(generated.Text) + "\n";
            else diagnostics.Add($"WARNING: Local AI security summary was unavailable ({generated.Status}); deterministic summary retained.");
        }
        var root = Path.Combine(LocalPath(status.RepositoryPath), "runs", runId);
        var summaryPath = Path.Combine(root, "summary.md");
        Write(summaryPath, output);
        Write(Path.Combine(root, "summary-metadata.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, runId, generationStatus, provider, model,
            localOnly = true, promptDigest = "sha256:" + Hash(prompt), manifestDigest = "sha256:" + Hash(JsonSerializer.Serialize(status.Manifest)),
            generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"), deterministicVerdictPreserved = true,
        }, JsonOptions));
        return status with { Status = "summarised", SummaryPath = Path.GetRelativePath(status.RepositoryPath, summaryPath).Replace('\\', '/'), Diagnostics = diagnostics, Applied = true };
    }

    private static SecuritySuiteExecution ApplyAcceptances(SecuritySuiteExecution execution, SecuritySuiteProfile suite,
        IReadOnlyList<AcceptedSecurityFinding> acceptances)
    {
        var findings = execution.Findings.Select(finding =>
        {
            var accepted = acceptances.FirstOrDefault(item => item.Scanner.Equals(finding.Scanner, StringComparison.OrdinalIgnoreCase)
                && item.Fingerprint.Equals(finding.Fingerprint, StringComparison.Ordinal));
            return accepted is null ? finding : finding with { Status = "accepted", AcceptanceId = accepted.Id };
        }).ToArray();
        var fail = suite.FailSeverities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blocking = findings.Any(item => item.Status != "accepted" && fail.Contains(item.Severity));
        return execution with
        {
            Status = blocking ? "findings" : findings.Length > 0 ? "passed-with-findings" : "passed",
            FailureKind = blocking ? SecurityFailureKind.Finding : SecurityFailureKind.None,
            Accepted = findings.Count(item => item.Status == "accepted"), Findings = findings,
        };
    }

    private static string DeterministicSummary(SecurityRunManifest manifest)
    {
        var lines = new List<string> { $"# Security run {manifest.RunId}", "", $"- Status: **{manifest.Status}**", $"- Revision: `{manifest.RepositoryRevision}`", "", "| Suite | Category | Status | Critical | High | Medium | Low | Accepted |", "| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |" };
        lines.AddRange(manifest.Suites.Select(item => $"| {item.SuiteId} | {item.Category} | {item.Status} | {item.Critical} | {item.High} | {item.Medium} | {item.Low} | {item.Accepted} |"));
        lines.AddRange(["", "Scanner findings and policy determine the verdict. Any model-generated triage below is advisory only.", ""]);
        return string.Join('\n', lines);
    }

    private static string SecurityPrompt(SecurityRunManifest manifest)
    {
        var lines = new List<string>
        {
            "Summarize these normalized security findings for a developer. Use only supplied facts.",
            "Do not change severity, acceptance, or the deterministic verdict. Do not invent code or remediation evidence.",
            "Return concise Markdown: likely themes, highest-priority files/rules, and one next investigation step.",
        };
        foreach (var finding in manifest.Suites.SelectMany(item => item.Findings).Take(200))
        {
            var message = finding.Category == "secret" ? "redacted secret-like finding" : SecurityEvidence.Redact(finding.Message);
            lines.Add($"- {finding.Severity}|{finding.Scanner}|{finding.RuleId}|{finding.Path}:{finding.StartLine?.ToString() ?? "-"}|{finding.Status}|{message}");
        }
        return string.Join('\n', lines);
    }

    private IReadOnlyList<SecuritySuiteProfile> ReadProfile(CisRepositoryContext context, ICollection<string> diagnostics)
    {
        var path = ProfilePath(context);
        if (!File.Exists(path)) { diagnostics.Add($"ERROR: Security-suite profile was not found: {Path.GetRelativePath(context.RepositoryPath, path)}"); return []; }
        var suites = new List<SecuritySuiteProfile>(); Dictionary<string, int>? columns = null;
        foreach (var line in File.ReadLines(path))
        {
            if (!line.TrimStart().StartsWith('|')) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(item => item.Trim()).ToArray();
            if (cells.Length < 5) continue;
            if (cells[0].Equals("Suite ID", StringComparison.OrdinalIgnoreCase)) { columns = cells.Select((name, index) => (name, index)).ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase); continue; }
            if (columns is null || cells.All(item => item.All(character => character is '-' or ':' or ' '))) continue;
            string Value(string name, string fallback = "-") => columns.TryGetValue(name, out var index) && index < cells.Length ? cells[index] : fallback;
            suites.Add(new(Value("Suite ID"), Value("Component"), Value("Category"), Value("Tool"), Value("Command"), Value("Working directory", "."),
                Value("Result format"), Value("Result path"), Value("Target"), Value("Applies when"), Value("CI tier"), Value("Fail severities", "critical,high"), Value("Artifacts")));
        }
        if (suites.Count == 0) diagnostics.Add("ERROR: Security-suite profile contains no suites.");
        return suites.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void ValidateProfile(CisRepositoryContext context, IReadOnlyList<SecuritySuiteProfile> suites, bool strict, ICollection<string> diagnostics)
    {
        foreach (var duplicate in suites.GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)) diagnostics.Add($"ERROR: Security suite ID is duplicated: {duplicate.Key}");
        foreach (var suite in suites)
        {
            if (!SafeId().IsMatch(suite.Id)) diagnostics.Add($"ERROR: Security suite ID is invalid: {suite.Id}");
            if (!Categories.Contains(suite.Category)) diagnostics.Add($"ERROR: Security suite '{suite.Id}' has unsupported category '{suite.Category}'.");
            if (!_adapters.ContainsKey(suite.ResultFormat)) diagnostics.Add($"ERROR: Security suite '{suite.Id}' has unsupported result format '{suite.ResultFormat}'.");
            if (string.IsNullOrWhiteSpace(suite.Command) || suite.Command == "-") diagnostics.Add($"ERROR: Security suite '{suite.Id}' has no command.");
            SafePath(context.RepositoryPath, suite.WorkingDirectory, diagnostics, suite.Id, false);
            var result = SafePath(context.RepositoryPath, suite.ResultPath, diagnostics, suite.Id, true);
            if (strict && result is not null && !result.StartsWith(Path.Combine(context.RepositoryPath, ".cis", "local", "security") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                diagnostics.Add($"ERROR: Security suite '{suite.Id}' result path must remain under .cis/local/security/.");
        }
    }

    private static IReadOnlyList<AcceptedSecurityFinding> ReadAcceptances(CisRepositoryContext context, ICollection<string> diagnostics, bool requireFile)
    {
        var path = AcceptancePath(context);
        if (!File.Exists(path)) { if (requireFile) diagnostics.Add($"ERROR: Accepted-security-findings registry was not found: {Path.GetRelativePath(context.RepositoryPath, path)}"); return []; }
        var results = new List<AcceptedSecurityFinding>(); Dictionary<string, int>? columns = null;
        foreach (var line in File.ReadLines(path))
        {
            if (!line.TrimStart().StartsWith('|')) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(item => item.Trim().Trim('`')).ToArray();
            if (cells.Length < 5) continue;
            if (cells[0].Equals("Finding ID", StringComparison.OrdinalIgnoreCase)) { columns = cells.Select((name, index) => (name, index)).ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase); continue; }
            if (columns is null || cells.All(item => item.All(character => character is '-' or ':' or ' '))) continue;
            string Value(string name) => columns.TryGetValue(name, out var index) && index < cells.Length ? cells[index] : string.Empty;
            var id = Value("Finding ID"); if (string.IsNullOrWhiteSpace(id) || id == "-") continue;
            var expiryText = Value("Accepted until");
            if (!DateOnly.TryParseExact(expiryText, "yyyy-MM-dd", out var expiry)) { diagnostics.Add($"ERROR: Acceptance '{id}' has invalid Accepted until date."); continue; }
            var entry = new AcceptedSecurityFinding(id, Value("Scanner"), Value("Fingerprint"), Value("Classification"), Value("Reason"), expiry,
                Value("Owner"), Value("Approved by"), Value("Approval reference"));
            if (!SafeId().IsMatch(id)) diagnostics.Add($"ERROR: Acceptance ID is invalid: {id}");
            if (string.IsNullOrWhiteSpace(entry.Scanner) || string.IsNullOrWhiteSpace(entry.Fingerprint) || entry.Fingerprint.Contains('*') || entry.Fingerprint.Contains('?')) diagnostics.Add($"ERROR: Acceptance '{id}' must use an exact scanner and fingerprint.");
            if (string.IsNullOrWhiteSpace(entry.Reason) || string.IsNullOrWhiteSpace(entry.Owner) || string.IsNullOrWhiteSpace(entry.ApprovedBy) || string.IsNullOrWhiteSpace(entry.ApprovalReference)) diagnostics.Add($"ERROR: Acceptance '{id}' is missing governance metadata.");
            if (expiry < DateOnly.FromDateTime(DateTime.UtcNow)) diagnostics.Add($"ERROR: Acceptance '{id}' expired on {expiry:yyyy-MM-dd}.");
            results.Add(entry);
        }
        foreach (var duplicate in results.GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)) diagnostics.Add($"ERROR: Acceptance ID is duplicated: {duplicate.Key}");
        return results;
    }

    private static IReadOnlyList<SecurityArtifact> WorkflowArtifacts(string repository, string runId, SecuritySuiteProfile suite,
        IReadOnlyList<WorkflowStepState> states, string revision, ICollection<string> diagnostics)
    {
        var root = Path.Combine(repository, WorkflowService.RunsPath.Replace('/', Path.DirectorySeparatorChar), runId);
        if (!Directory.Exists(root)) return [];
        var artifacts = new List<SecurityArtifact>();
        foreach (var state in states)
        foreach (var path in Directory.EnumerateFiles(root, "*.log", SearchOption.TopDirectoryOnly).Where(path => AttemptLog(Path.GetFileName(path), state.Id)))
        {
            if (new FileInfo(path).Length > MaximumArtifactBytes) { diagnostics.Add($"ERROR: Security workflow log exceeds the 50 MiB limit: {path}"); continue; }
            artifacts.Add(Correlate(SecurityEvidence.Artifact(repository, "workflow-log", path), runId, Attempt(Path.GetFileName(path), state.Id) ?? state.Attempt, suite, revision));
        }
        return artifacts;
    }

    private static SecurityArtifact Correlate(SecurityArtifact artifact, string runId, int? attempt, SecuritySuiteProfile suite, string revision)
        => artifact with { RunId = runId, Attempt = attempt, SuiteId = suite.Id, Component = suite.Component, RepositoryRevision = revision };
    private static bool AttemptLog(string file, string step) => file.Equals(step + ".log", StringComparison.OrdinalIgnoreCase) || (file.StartsWith(step + ".attempt-", StringComparison.OrdinalIgnoreCase) && file.EndsWith(".log", StringComparison.OrdinalIgnoreCase));
    private static int? Attempt(string file, string step) => file.Equals(step + ".log", StringComparison.OrdinalIgnoreCase) ? 1 : int.TryParse(file[(step.Length + ".attempt-".Length)..^4], out var value) ? value : null;

    private static SecuritySuiteExecution Unavailable(SecuritySuiteProfile suite, string message) => new(suite.Id, suite.Category, suite.Tool, "unavailable", SecurityFailureKind.MissingPrerequisite, 0, 0, 0, 0, 0, [], [], [message]);
    private static SecuritySuiteExecution Invalid(SecuritySuiteProfile suite, string message) => new(suite.Id, suite.Category, suite.Tool, "invalid-evidence", SecurityFailureKind.InvalidEvidence, 0, 0, 0, 0, 0, [], [], [message]);
    private static SecuritySuiteExecution Failed(SecuritySuiteProfile suite, WorkflowStepState state) => new(suite.Id, suite.Category, suite.Tool, "failed", ParseFailure(state.FailureKind), 0, 0, 0, 0, 0, [], [], [state.Error ?? "Scanner command failed."]);
    private static SecurityFailureKind ParseFailure(string value) => value.ToLowerInvariant() switch { "product" => SecurityFailureKind.Scanner, "infrastructure" => SecurityFailureKind.Infrastructure, "missing-prerequisite" => SecurityFailureKind.MissingPrerequisite, "timeout" => SecurityFailureKind.Timeout, "cancelled" => SecurityFailureKind.Cancelled, _ => SecurityFailureKind.Unknown };

    private CisRepositoryContext? Resolve(string repositoryPath, out List<string> diagnostics) { var resolution = _resolver.Resolve(repositoryPath); diagnostics = resolution.Errors.Select(item => "ERROR: " + item).ToList(); return resolution.Context; }
    private static string ProfilePath(CisRepositoryContext context) => Path.Combine(context.DocumentationPath, "references", "security-suite-profile.md");
    private static string AcceptancePath(CisRepositoryContext context) => Path.Combine(context.DocumentationPath, "references", "accepted-security-findings.md");
    private static string ProfileDigest(CisRepositoryContext context) { using var stream = File.OpenRead(ProfilePath(context)); return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static string LocalPath(string repository) => Path.Combine(repository, LocalRoot.Replace('/', Path.DirectorySeparatorChar));
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Revision(string repository)
    {
        try { using var process = new Process { StartInfo = new("git") { WorkingDirectory = repository, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } }; process.StartInfo.ArgumentList.Add("rev-parse"); process.StartInfo.ArgumentList.Add("HEAD"); process.Start(); var output = process.StandardOutput.ReadToEnd(); process.WaitForExit(10_000); return process.ExitCode == 0 ? output.Trim() : "unavailable"; }
        catch { return "unavailable"; }
    }
    private static string? LatestRun(string repository) { var root = Path.Combine(LocalPath(repository), "runs"); return Directory.Exists(root) ? Directory.EnumerateDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc).Select(Path.GetFileName).FirstOrDefault() : null; }
    private static string? SafePath(string repository, string relative, ICollection<string> diagnostics, string suite, bool required)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative == "-") { if (required) diagnostics.Add($"ERROR: Security suite '{suite}' has no result path."); return null; }
        try { var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository)); var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))); if (!path.Equals(root, StringComparison.OrdinalIgnoreCase) && !path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { diagnostics.Add($"ERROR: Security suite '{suite}' path escapes the repository: {relative}"); return null; } return path; }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { diagnostics.Add($"ERROR: Security suite '{suite}' path is invalid: {exception.Message}"); return null; }
    }
    private static void Write(string path, string content) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temporary = path + ".tmp"; File.WriteAllText(temporary, content.EndsWith('\n') ? content : content + "\n"); File.Move(temporary, path, true); }
    private static bool IsError(string value) => value.StartsWith("ERROR:", StringComparison.Ordinal);
    private static SecurityResult Result(CisRepositoryContext? context, string? run, IReadOnlyList<SecuritySuiteProfile> suites, SecurityRunManifest? manifest, IReadOnlyList<AcceptedSecurityFinding> acceptances, string? summary, IReadOnlyList<string> diagnostics, bool applied, string status) => new(status, context?.RepositoryPath, run, suites, manifest, acceptances, summary, diagnostics, applied);
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)] private static partial Regex SafeId();
}
