using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Workflow;

namespace Cis.Modules.Testing;

public sealed record TestTraceResult(string Id, string Status, IReadOnlyList<string> Suites, IReadOnlyList<string> Sources);

public sealed record TestingResult(
    string Status,
    string? RepositoryPath,
    string? ChangeId,
    string? RunId,
    IReadOnlyList<TestSuiteProfile> Suites,
    TestRunManifest? Manifest,
    IReadOnlyList<TestTraceResult> Trace,
    IReadOnlyList<string> Diagnostics,
    bool Applied)
{
    public int ExitCode => Diagnostics.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal))
        || Manifest?.Status is "failed" or "invalid-evidence" ? 4 : 0;
}

public sealed partial class TestingService
{
    public const string LocalRoot = ".cis/local/testing";
    private const long MaximumDiagnosticArtifactBytes = 10L * 1024 * 1024;
    private const int MaximumDiagnosticArtifactsPerSuite = 200;
    private static readonly HashSet<string> Layers = new(StringComparer.OrdinalIgnoreCase)
    {
        "unit", "architecture", "component", "integration", "business", "frontend-component",
        "browser", "security", "coverage", "mutation", "operational", "regression", "api-compatibility",
    };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ICisRepositoryContextResolver _resolver;
    private readonly ICisWorkspaceRegistry? _workspaceRegistry;
    private readonly ICisNativeWorkController? _nativeWork;
    private readonly WorkflowService _workflows;
    private readonly IReadOnlyDictionary<string, ICisTestResultAdapter> _adapters;

    public TestingService(ICisRepositoryContextResolver resolver, WorkflowService workflows,
        IEnumerable<ICisTestResultAdapter> adapters, ICisWorkspaceRegistry? workspaceRegistry = null,
        ICisNativeWorkController? nativeWork = null)
    {
        _resolver = resolver;
        _workflows = workflows;
        _workspaceRegistry = workspaceRegistry;
        _nativeWork = nativeWork;
        _adapters = adapters.ToDictionary(item => item.Format, StringComparer.OrdinalIgnoreCase);
    }

    public TestingResult Inventory(string repositoryPath)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return Result(null, null, null, [], null, [], diagnostics, false, "invalid-repository");
        var suites = ReadProfile(context, diagnostics);
        if (diagnostics.Any(IsError)) return Result(context, null, null, suites, null, [], diagnostics, false, "invalid");
        var root = Path.Combine(context.RepositoryPath, LocalRoot.Replace('/', Path.DirectorySeparatorChar));
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
        return Result(context, null, null, suites, null, [], diagnostics, true, "inventoried");
    }

    public TestingResult Validate(string repositoryPath, bool strict)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return Result(null, null, null, [], null, [], diagnostics, false, "invalid-repository");
        var suites = ReadProfile(context, diagnostics);
        ValidateProfile(context, suites, strict, diagnostics);
        return Result(context, null, null, suites, null, [], diagnostics, false,
            diagnostics.Any(IsError) ? "invalid" : "valid");
    }

    public TestingResult Reconcile(string repositoryPath, string runId) => ReconcileCore(repositoryPath, runId, null);

    public TestingResult Reconcile(string repositoryPath, string runId, IReadOnlyList<string> suiteIds)
        => ReconcileCore(repositoryPath, runId, suiteIds ?? []);

    private TestingResult ReconcileCore(string repositoryPath, string runId, IReadOnlyList<string>? suiteIds)
    {
        var validation = Validate(repositoryPath, true);
        if (validation.RepositoryPath is null || validation.Diagnostics.Any(IsError)) return validation with { RunId = runId };
        var context = _resolver.Resolve(validation.RepositoryPath).Context!;
        var diagnostics = validation.Diagnostics.ToList();
        var selected = SelectSuites(validation.Suites, suiteIds, diagnostics);
        if (diagnostics.Any(IsError))
            return Result(context, null, runId, validation.Suites, null, [], diagnostics, false, "invalid-selection");
        var workflow = _workflows.Status(context.RepositoryPath, runId);
        if (workflow.Run is null || workflow.Diagnostics.Count > 0)
        {
            diagnostics.AddRange(workflow.Diagnostics.Select(item => item.StartsWith("ERROR:", StringComparison.Ordinal) ? item : "ERROR: " + item));
            return Result(context, null, runId, validation.Suites, null, [], diagnostics, false, "invalid-run");
        }
        var definition = _workflows.Describe(context.RepositoryPath, workflow.Run.WorkflowId).Workflow;
        if (definition is null)
        {
            diagnostics.Add($"ERROR: Workflow definition is unavailable: {workflow.Run.WorkflowId}");
            return Result(context, null, runId, validation.Suites, null, [], diagnostics, false, "invalid-run");
        }

        if (suiteIds is not null && (string.IsNullOrWhiteSpace(workflow.Run.InputDigest) || definition.Digest != workflow.Run.WorkflowDigest
            || selected.Any(suite => !definition.Steps.Any(step => step.Suites.Contains(suite.Id, StringComparer.OrdinalIgnoreCase)))))
        {
            diagnostics.Add("ERROR: Selected-suite reconciliation requires recorded workflow inputs, the exact workflow definition and a bound step for every selected suite.");
            return Result(context, null, runId, validation.Suites, null, [], diagnostics, false, "invalid-selection");
        }

        var executions = new List<TestSuiteExecution>();
        foreach (var suite in selected)
        {
            var steps = definition.Steps.Where(step => step.Suites.Contains(suite.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (steps.Length == 0)
            {
                executions.Add(Unavailable(suite, "No workflow step is bound to this suite."));
                continue;
            }
            var states = workflow.Run.Steps.Where(state => steps.Any(step => step.Id.Equals(state.Id, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (states.Length == 0)
            {
                executions.Add(Unavailable(suite, "The bound workflow step did not run."));
                continue;
            }
            var failedState = states.LastOrDefault(state => state.Status is "failed" or "blocked");
            var playwright = suite.ResultFormat.Equals("playwright-json", StringComparison.OrdinalIgnoreCase);
            var failedNativeReport = failedState is not null
                && (playwright || suite.ResultFormat.Equals("stryker-json", StringComparison.OrdinalIgnoreCase))
                && states.Length == steps.Length && states.All(state => state.Status is "failed" or "succeeded")
                && (!playwright || states.All(state => DateTimeOffset.TryParse(state.CompletedAtUtc, out _)));
            if (failedState is not null && !failedNativeReport)
            {
                executions.Add(Failed(suite, failedState));
                continue;
            }
            if (!failedNativeReport && (states.Length != steps.Length || states.Any(state => state.Status != "succeeded")))
            {
                executions.Add(Unavailable(suite, "Every bound workflow step must finish successfully before its reports can be reconciled."));
                continue;
            }

            if ((suite.ResultPath.Contains("{run-id}", StringComparison.Ordinal)
                || suite.ResultPath.Contains("{attempt}", StringComparison.Ordinal)) && states.Length != 1)
            {
                executions.Add(InvalidEvidence(suite, "A native result path with run or attempt placeholders must be bound to exactly one workflow step."));
                continue;
            }
            var reportAttempt = states[0].Attempt;
            var declaredResultPath = suite.ResultPath.Replace("{run-id}", runId, StringComparison.Ordinal)
                .Replace("{attempt}", reportAttempt.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
            var resultPath = SafePath(context.RepositoryPath, declaredResultPath, diagnostics, suite.Id, required: true);
            var beforePaths = diagnostics.Count;
            var coveragePath = CoveragePath(context.RepositoryPath, suite.CoveragePath, diagnostics, suite.Id, resolve: true);
            var mutationPath = SafePath(context.RepositoryPath, suite.MutationPath, diagnostics, suite.Id, required: false);
            if (diagnostics.Count > beforePaths)
            {
                executions.Add(InvalidEvidence(suite, "Declared native report selection is unsafe, missing or ambiguous."));
                continue;
            }
            if (resultPath is null || !File.Exists(resultPath))
            {
                executions.Add(InvalidEvidence(suite, "The declared native result file is missing."));
                continue;
            }
            if (workflow.Run.InputDigest is not null || failedNativeReport || playwright)
            {
                var starts = states.Select(state => DateTimeOffset.TryParse(state.StartedAtUtc, out var value) ? value : DateTimeOffset.MaxValue);
                var started = playwright ? starts.Max() : starts.Min();
                var reports = new[] { resultPath, coveragePath, mutationPath }.Where(path => path is not null).ToArray();
                if (reports.Any(path => !File.Exists(path) || File.GetLastWriteTimeUtc(path!) < started.UtcDateTime))
                {
                    executions.Add(InvalidEvidence(suite, "A declared native report is missing or predates this execution; use retained per-run output paths."));
                    continue;
                }
                var completed = states.Select(state => DateTimeOffset.TryParse(state.CompletedAtUtc, out var value)
                    ? value : DateTimeOffset.MinValue).Max();
                if (playwright && reports.Any(path => File.GetLastWriteTimeUtc(path!) > completed.UtcDateTime))
                {
                    executions.Add(InvalidEvidence(suite, "A declared native browser report was written after this execution completed."));
                    continue;
                }
            }
            if (!_adapters.TryGetValue(suite.ResultFormat, out var adapter))
            {
                executions.Add(InvalidEvidence(suite, $"No result adapter is registered for '{suite.ResultFormat}'."));
                continue;
            }
            try
            {
                var assessment = suite.ResultFormat.Equals("stryker-json", StringComparison.OrdinalIgnoreCase)
                    ? MutationAssessmentThreshold(context.RepositoryPath, resultPath) : null;
                var execution = adapter.Read(new(context.RepositoryPath, suite, resultPath,
                    coveragePath is not null && File.Exists(coveragePath) ? coveragePath : null,
                    mutationPath is not null && File.Exists(mutationPath) ? mutationPath : null, assessment?.Threshold));
                if (assessment is { } bound)
                {
                    var artifact = TestResultEvidence.Artifact(context.RepositoryPath, "mutation-assessment", resultPath + ".assessment.json");
                    if (execution.Mutation?.ReportSha256 != bound.ReportSha256 || artifact.Digest != bound.AssessmentSha256)
                        throw new InvalidDataException("Mutation report or assessment changed while it was being read.");
                    execution = execution with { Artifacts = execution.Artifacts.Concat([artifact]).ToArray() };
                    if (bound.RunContext is { } runContext)
                        execution = ApplyMutationRunContext(context.RepositoryPath, execution, runContext);
                }
                executions.Add(execution);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or System.Xml.XmlException)
            {
                executions.Add(InvalidEvidence(suite, $"Result evidence is unreadable: {exception.Message}"));
            }
        }

        var revision = Revision(context.RepositoryPath);
        var runStartedAt = DateTimeOffset.TryParse(workflow.Run.CreatedAtUtc, out var parsedRunStartedAt)
            ? parsedRunStartedAt : DateTimeOffset.MinValue;
        executions = executions.Select(execution =>
        {
            var suite = validation.Suites.Single(item => item.Id.Equals(execution.SuiteId, StringComparison.OrdinalIgnoreCase));
            var steps = definition.Steps.Where(step => step.Suites.Contains(suite.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
            var states = workflow.Run.Steps.Where(state => steps.Any(step => step.Id.Equals(state.Id, StringComparison.OrdinalIgnoreCase))).ToArray();
            var failedState = states.LastOrDefault(state => state.Status is "failed" or "blocked");
            if (failedState is not null && (suite.ResultFormat.Equals("stryker-json", StringComparison.OrdinalIgnoreCase)
                || suite.ResultFormat.Equals("playwright-json", StringComparison.OrdinalIgnoreCase)))
            {
                // Preserve the failed process observation while retaining any
                // fresh native outcomes that could still be assessed.
                execution = execution with
                {
                    Status = "failed",
                    FailureKind = ParseFailure(failedState.FailureKind),
                    Diagnostics = new[] { failedState.Error ?? $"Workflow step {failedState.Id} failed." }
                        .Concat(execution.Diagnostics).Distinct(StringComparer.Ordinal).ToArray(),
                };
            }
            var caseIds = execution.Cases.Select(item => item.Id).Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
            var nativeAttempt = states.Length == 1 ? (int?)states[0].Attempt : null;
            var artifacts = execution.Artifacts
                .Select(artifact => Correlate(artifact, runId, nativeAttempt, suite, revision, caseIds))
                .Concat(WorkflowLogArtifacts(context.RepositoryPath, runId, suite, states, revision, caseIds, diagnostics))
                .Concat(SuiteDiagnosticArtifacts(context.RepositoryPath, runId, suite, revision, caseIds, runStartedAt, diagnostics))
                .DistinctBy(item => (item.Path, item.Digest, item.SuiteId, item.Attempt))
                .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Attempt)
                .ToArray();
            return execution with { Artifacts = artifacts };
        }).ToList();

        var allArtifacts = executions.SelectMany(item => item.Artifacts)
            .DistinctBy(item => (item.Path, item.Digest, item.SuiteId, item.Attempt))
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var status = executions.Any(item => item.Status is "failed" or "invalid-evidence"
                || item.Mutation is { Qualified: false }) ? "failed"
            : executions.Any(item => item.Status is "unavailable" or "skipped") ? "incomplete"
            : executions.Any(item => item.Status is "findings") ? "passed-with-findings" : "passed";
        var manifest = new TestRunManifest(1, runId, workflow.Run.WorkflowId, workflow.Run.WorkflowDigest,
            context.RepositoryId, revision, ProfileDigest(context),
            $"{RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}",
            workflow.Run.CreatedAtUtc, workflow.Run.UpdatedAtUtc, status, executions, allArtifacts,
            Environment.GetEnvironmentVariable("CIS_IMPLEMENTER"),
            Environment.GetEnvironmentVariable("CIS_ASSURER"),
            Environment.GetEnvironmentVariable("CIS_ASSURANCE_TECHNIQUE"), workflow.Run.InputDigest);
        if (suiteIds is not null)
        {
            manifest = manifest with { SchemaVersion = 2, Status = status == "passed" ? "scope-passed" : status };
            manifest = manifest with
            {
                Scope = TestReconcileScope.Create(manifest,
                selected.Select(suite => suite.Id).Order(StringComparer.Ordinal).ToArray(),
                validation.Suites.Select(suite => suite.Id).Except(selected.Select(suite => suite.Id), StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.Ordinal).ToArray())
            };
        }
        var reference = manifest.Scope?.Id ?? runId;
        var runRoot = Path.Combine(context.RepositoryPath, LocalRoot.Replace('/', Path.DirectorySeparatorChar),
            manifest.Scope is null ? "runs" : "scoped-runs", reference);
        if (manifest.Scope is not null)
            return PublishScope(context, runRoot, reference, selected, manifest, executions, allArtifacts, diagnostics);
        Directory.CreateDirectory(runRoot);
        Write(Path.Combine(runRoot, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
        Write(Path.Combine(runRoot, "results.json"), JsonSerializer.Serialize(executions, JsonOptions));
        Write(Path.Combine(runRoot, "artifacts.json"), JsonSerializer.Serialize(allArtifacts, JsonOptions));
        return Result(context, null, reference, suiteIds is null ? validation.Suites : selected, manifest, [], diagnostics, true, "reconciled");
    }

    private static (double Threshold, string ReportSha256, string AssessmentSha256, JsonElement? RunContext)? MutationAssessmentThreshold(string repository, string report)
    {
        var sidecar = report + ".assessment.json";
        if (!File.Exists(sidecar)) return null;
        if (CisPathSafety.ContainsReparsePoint(repository, sidecar) || new FileInfo(sidecar).Length > 1024 * 1024)
            throw new InvalidDataException("Mutation assessment must be an unlinked JSON sidecar no larger than 1 MiB.");
        var bytes = File.ReadAllBytes(sidecar);
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Mutation assessment exceeds 1 MiB.");
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var errors = new List<string>();
        StrykerJsonResultAdapter.CheckDuplicateProperties(root, "$", errors);
        if (errors.Count > 0) throw new InvalidDataException("Mutation assessment contains duplicate JSON properties.");
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("mutation", out var mutation)
            || mutation.ValueKind != JsonValueKind.Object || !mutation.TryGetProperty("breakThreshold", out var threshold)
            || threshold.ValueKind != JsonValueKind.Number || !threshold.TryGetDouble(out var value)
            || !double.IsFinite(value) || value is < 0 or > 100)
            throw new InvalidDataException("Mutation assessment requires a finite break threshold from 0 through 100.");
        var digest = TestResultEvidence.Artifact(repository, "mutation-result", report).Digest;
        if (!mutation.TryGetProperty("reportSha256", out var hash) || hash.ValueKind != JsonValueKind.String || hash.GetString() != digest
            || !root.TryGetProperty("artifacts", out var artifacts) || artifacts.ValueKind != JsonValueKind.Array
            || !artifacts.EnumerateArray().Any(artifact => artifact.ValueKind == JsonValueKind.Object
                && artifact.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "mutation-result"
                && artifact.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String && path.GetString() == TestResultEvidence.Relative(repository, report)
                && artifact.TryGetProperty("digest", out var sha) && sha.ValueKind == JsonValueKind.String && sha.GetString() == digest))
            throw new InvalidDataException("Mutation assessment does not identify these exact raw report bytes.");
        // Recompute all scores and native outcomes. Run context can only restrict
        // qualification; a stored qualified flag can never upgrade the report.
        JsonElement? runContext = root.TryGetProperty("cisRun", out var run) ? run.Clone() : null;
        return (value, digest, Convert.ToHexStringLower(SHA256.HashData(bytes)), runContext);
    }

    private static TestArtifact Correlate(TestArtifact artifact, string runId, int? attempt, TestSuiteProfile suite,
        string revision, IReadOnlyList<string> caseIds)
        => artifact with
        {
            RunId = runId,
            Attempt = attempt,
            SuiteId = suite.Id,
            Component = suite.Component,
            RepositoryRevision = revision,
            TestCaseIds = caseIds,
        };

    private static IReadOnlyList<TestArtifact> WorkflowLogArtifacts(string repository, string runId, TestSuiteProfile suite,
        IReadOnlyList<WorkflowStepState> states, string revision, IReadOnlyList<string> caseIds, ICollection<string> diagnostics)
    {
        var runRoot = Path.Combine(repository, WorkflowService.RunsPath.Replace('/', Path.DirectorySeparatorChar), runId);
        if (!Directory.Exists(runRoot)) return [];
        var artifacts = new List<TestArtifact>();
        foreach (var state in states)
        {
            foreach (var path in Directory.EnumerateFiles(runRoot, "*.log", SearchOption.TopDirectoryOnly)
                         .Where(path => IsAttemptLog(Path.GetFileName(path), state.Id))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!ValidateDiagnosticArtifact(path, suite.Id, diagnostics)) continue;
                var attempt = Attempt(Path.GetFileName(path), state.Id) ?? state.Attempt;
                artifacts.Add(Correlate(TestResultEvidence.Artifact(repository, "workflow-log", path), runId,
                    attempt, suite, revision, caseIds));
            }
        }
        return artifacts;
    }

    private static IReadOnlyList<TestArtifact> SuiteDiagnosticArtifacts(string repository, string runId,
        TestSuiteProfile suite, string revision, IReadOnlyList<string> caseIds, DateTimeOffset runStartedAt,
        ICollection<string> diagnostics)
    {
        var suiteRoot = Path.Combine(repository, LocalRoot.Replace('/', Path.DirectorySeparatorChar), "diagnostics", suite.Id);
        if (!Directory.Exists(suiteRoot)) return [];
        var runRoot = Regex.IsMatch(runId, @"\A[A-Za-z0-9._-]{1,128}\z", RegexOptions.CultureInvariant)
            ? Path.Combine(suiteRoot, runId) : Path.Combine(suiteRoot, "__invalid-run-id__");
        var root = Directory.Exists(runRoot) ? runRoot : suiteRoot;
        var paths = CisPathSafety.EnumerateFiles(root)
            .Where(path => IsCurrentDiagnosticArtifact(path, suite.Id, runStartedAt, diagnostics))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length > MaximumDiagnosticArtifactsPerSuite)
        {
            diagnostics.Add($"ERROR: Test suite '{suite.Id}' produced {paths.Length} diagnostic artifacts; the limit is {MaximumDiagnosticArtifactsPerSuite}.");
            paths = paths.Take(MaximumDiagnosticArtifactsPerSuite).ToArray();
        }
        return paths.Where(path => ValidateDiagnosticArtifact(path, suite.Id, diagnostics))
            .Select(path => Correlate(TestResultEvidence.Artifact(repository, "test-diagnostic", path), runId,
                DiagnosticAttempt(root, path), suite, revision, caseIds)).ToArray();
    }

    private static int? DiagnosticAttempt(string root, string path)
    {
        foreach (var segment in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.StartsWith("attempt-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(segment["attempt-".Length..], out var attempt)) return attempt;
        }
        return null;
    }

    private static bool IsCurrentDiagnosticArtifact(string path, string suiteId, DateTimeOffset runStartedAt,
        ICollection<string> diagnostics)
    {
        if (runStartedAt == DateTimeOffset.MinValue
            || new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) >= runStartedAt.AddSeconds(-2)) return true;
        diagnostics.Add($"WARNING: Ignored stale diagnostic artifact for test suite '{suiteId}': {path}");
        return false;
    }

    private static bool ValidateDiagnosticArtifact(string path, string suiteId, ICollection<string> diagnostics)
    {
        var length = new FileInfo(path).Length;
        if (length <= MaximumDiagnosticArtifactBytes) return true;
        diagnostics.Add($"ERROR: Test suite '{suiteId}' diagnostic artifact exceeds the 10 MiB limit: {path}");
        return false;
    }

    private static bool IsAttemptLog(string fileName, string stepId)
        => fileName.Equals(stepId + ".log", StringComparison.OrdinalIgnoreCase)
           || (fileName.StartsWith(stepId + ".attempt-", StringComparison.OrdinalIgnoreCase)
               && fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
               && int.TryParse(fileName[(stepId.Length + ".attempt-".Length)..^4], out _));

    private static int? Attempt(string fileName, string stepId)
        => fileName.Equals(stepId + ".log", StringComparison.OrdinalIgnoreCase) ? 1
            : int.TryParse(fileName[(stepId.Length + ".attempt-".Length)..^4], out var attempt) ? attempt : null;

    public TestingResult Trace(string repositoryPath, string changeId, string runId)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return Result(null, changeId, runId, [], null, [], diagnostics, false, "invalid-repository");
        if (!SafeIdPattern().IsMatch(runId))
        {
            diagnostics.Add("ERROR: Test run reference must be a single portable path segment.");
            return Result(context, changeId, runId, [], null, [], diagnostics, false, "invalid-run");
        }
        if (!SafeIdPattern().IsMatch(changeId))
        {
            diagnostics.Add("ERROR: Change ID must be a single portable path segment.");
            return Result(context, changeId, runId, [], null, [], diagnostics, false, "invalid-change");
        }
        if (!SafeIdPattern().IsMatch(runId))
        {
            diagnostics.Add("ERROR: Run ID must be a single portable path segment.");
            return Result(context, changeId, runId, [], null, [], diagnostics, false, "invalid-run");
        }
        changeId = Path.GetFileName(changeId);
        var testCases = Path.Combine(context.DocumentationPath, "changes", changeId, "test-cases.md");
        if (!File.Exists(testCases))
        {
            diagnostics.Add($"ERROR: Manual test catalogue was not found: {Path.GetRelativePath(context.RepositoryPath, testCases)}");
            return Result(context, changeId, runId, [], null, [], diagnostics, false, "invalid-change");
        }
        var ids = TestCaseHeadingPattern().Matches(File.ReadAllText(testCases)).Select(match => match.Groups["id"].Value.ToUpperInvariant()).Distinct().ToArray();
        var manifests = ReadWorkspaceManifests(context, runId, diagnostics);
        var suiteByCase = manifests.SelectMany(manifest => manifest.Suites
            .SelectMany(suite => suite.Cases.Select(testCase => (Id: suite.SuiteId, Case: testCase,
                Passed: suite.Status == "passed" && testCase.Status == "passed"))))
            .Where(item => !string.IsNullOrWhiteSpace(item.Case.Id))
            .GroupBy(item => item.Case.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.Key, item => item.ToArray(), StringComparer.OrdinalIgnoreCase);
        var trace = ids.Select(id => suiteByCase.TryGetValue(id, out var cases) && cases.All(item => item.Passed)
            ? new TestTraceResult(id, "passed", cases.Select(item => item.Id).Distinct().ToArray(), cases.Select(item => item.Case.Source ?? item.Case.Name).Distinct().ToArray())
            : new TestTraceResult(id, "missing", [], [])).ToArray();
        foreach (var missing in trace.Where(item => item.Status == "missing"))
            diagnostics.Add($"ERROR: {missing.Id} is not present in a passed reconciled test result for run '{runId}'.");
        var combined = manifests.Count == 0 ? null : manifests[0] with
        {
            Suites = manifests.SelectMany(item => item.Suites).ToArray(),
            Artifacts = manifests.SelectMany(item => item.Artifacts)
                .DistinctBy(item => (item.Path, item.Digest, item.SuiteId, item.Attempt)).ToArray(),
            Status = manifests.Any(item => item.Status is "failed" or "invalid-evidence") ? "failed"
                : manifests.Any(item => item.Status == "incomplete") ? "incomplete"
                : manifests.Any(item => item.Scope is not null) ? "scope-passed" : "passed",
        };
        var profiles = ReadTraceProfiles(context, manifests, diagnostics);
        return Result(context, changeId, runId, profiles, combined, trace,
            diagnostics, false, diagnostics.Any(IsError) ? "incomplete" : "traced");
    }

    public TestingResult Status(string repositoryPath, string changeId, string? runId)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return Result(null, changeId, runId, [], null, [], diagnostics, false, "invalid-repository");
        runId = string.IsNullOrWhiteSpace(runId) ? LatestRun(context.RepositoryPath) : runId;
        if (string.IsNullOrWhiteSpace(runId))
        {
            diagnostics.Add("ERROR: No reconciled test run is available.");
            return Result(context, changeId, null, ReadProfile(context, diagnostics), null, [], diagnostics, false, "missing-run");
        }
        return Trace(context.RepositoryPath, changeId, runId);
    }

    private IReadOnlyList<TestRunManifest> ReadWorkspaceManifests(CisRepositoryContext context, string runId, ICollection<string> diagnostics)
    {
        var repositories = new List<(string Id, string Path)> { (context.RepositoryId, context.RepositoryPath) };
        if (_workspaceRegistry is not null)
        {
            var resolution = _workspaceRegistry.Resolve(context.RepositoryPath);
            if (resolution.IsSuccess && resolution.Workspace is not null)
            {
                repositories = resolution.Workspace.Repositories.Select(item => (item.Id, item.RepositoryPath)).ToList();
            }
        }
        var manifests = new List<TestRunManifest>();
        foreach (var repository in repositories)
        {
            var ordinary = Path.Combine(repository.Path, LocalRoot.Replace('/', Path.DirectorySeparatorChar), "runs", runId, "manifest.json");
            var scoped = Path.Combine(repository.Path, LocalRoot.Replace('/', Path.DirectorySeparatorChar), "scoped-runs", runId, "manifest.json");
            if (File.Exists(ordinary) && File.Exists(scoped))
            { diagnostics.Add($"ERROR: Test evidence reference '{runId}' is ambiguous; preserve both records and choose a different workflow identity."); continue; }
            var path = File.Exists(ordinary) ? ordinary : scoped;
            if (!File.Exists(path)) continue;
            if (path == scoped && CisPathSafety.ContainsReparsePoint(repository.Path, path))
            { diagnostics.Add($"ERROR: Selected-suite manifest '{runId}' must remain unlinked."); continue; }
            try
            {
                var manifest = JsonSerializer.Deserialize<TestRunManifest>(File.ReadAllText(path), JsonOptions);
                if (manifest is not null)
                {
                    if (manifest.SchemaVersion == 1 ? manifest.Scope is not null || path == scoped
                        : manifest.SchemaVersion != 2 || path == ordinary || manifest.Scope is null
                            || manifest.Scope.Id != runId || !manifest.Scope.Matches(manifest))
                        diagnostics.Add($"ERROR: Test manifest scope/schema is invalid for '{runId}'.");
                    else if (!string.Equals(manifest.RepositoryId, repository.Id, StringComparison.Ordinal))
                        diagnostics.Add($"ERROR: Test manifest repository '{manifest.RepositoryId}' does not match its registered owner '{repository.Id}'.");
                    else if (TraceManifestIsCurrent(repository.Path, manifest.Scope?.WorkflowRunId ?? runId, manifest, diagnostics)) manifests.Add(manifest);
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                diagnostics.Add($"ERROR: Test manifest for {repository.Id} is invalid: {exception.Message}");
            }
        }
        if (manifests.Count == 0) diagnostics.Add($"ERROR: No reconciled test manifest was found for run '{runId}'.");
        return manifests;
    }

    private bool TraceManifestIsCurrent(string repository, string runId, TestRunManifest manifest, ICollection<string> diagnostics)
    {
        var context = _resolver.Resolve(repository).Context;
        if (context is not null && !File.Exists(ProfilePath(context)))
        {
            diagnostics.Add($"ERROR: Test-suite profile was not found for executing repository {manifest.RepositoryId}.");
            return false;
        }
        var browser = manifest.Suites?.Any(suite => string.Equals(suite.Layer, "browser", StringComparison.OrdinalIgnoreCase)) == true;
        if (browser && string.IsNullOrWhiteSpace(manifest.InputDigest))
        {
            diagnostics.Add($"ERROR: Browser evidence for {manifest.RepositoryId} has no execution input digest; start a fresh workflow run to verify current journeys.");
            return false;
        }
        var currentInputDigest = context is null ? null : manifest.InputDigest is not null || browser
            ? CisExecutionIdentity.Capture(context) : CisExecutionIdentity.CaptureIfAdopted(context);
        var workflow = _workflows.Status(repository, runId);
        var definition = _workflows.Describe(repository, manifest.WorkflowId).Workflow;
        if (context is null || manifest.RunId != runId || workflow.Run is null || workflow.Diagnostics.Count > 0
            || definition is null || manifest.WorkflowDigest != definition.Digest || workflow.Run.WorkflowDigest != definition.Digest
            || manifest.StartedAtUtc != workflow.Run.CreatedAtUtc || manifest.CompletedAtUtc != workflow.Run.UpdatedAtUtc
            || manifest.SuiteProfileDigest != ProfileDigest(context) || manifest.RepositoryRevision != Revision(repository)
            || manifest.InputDigest != workflow.Run.InputDigest || manifest.InputDigest != currentInputDigest)
        {
            diagnostics.Add($"ERROR: Test manifest for {manifest.RepositoryId} does not match the current repository, profile, workflow, or execution inputs; reconcile a current run.");
            return false;
        }
        if (manifest.Suites is null || manifest.Artifacts is null
            || manifest.Suites.Any(suite => suite.Cases is null || suite.Artifacts is null
                || (suite.Cases.Count > 0 && !suite.Artifacts.Any(artifact => artifact.Kind == "test-result"
                    && artifact.RunId == runId && artifact.SuiteId == suite.SuiteId
                    && artifact.RepositoryRevision == manifest.RepositoryRevision
                    && manifest.Artifacts.Any(bound => bound.Path == artifact.Path && bound.Digest == artifact.Digest
                        && bound.Bytes == artifact.Bytes && bound.SuiteId == artifact.SuiteId)))))
        {
            diagnostics.Add($"ERROR: Test manifest for {manifest.RepositoryId} has missing or unbound native results.");
            return false;
        }
        foreach (var artifact in manifest.Artifacts.Concat(manifest.Suites.SelectMany(suite => suite.Artifacts))
            .DistinctBy(item => (item.Path, item.Digest, item.Bytes)))
        {
            if (!CisPathSafety.TryResolveUnderRoot(repository, artifact.Path, out var path)
                || CisPathSafety.ContainsReparsePoint(repository, path) || !File.Exists(path))
            {
                diagnostics.Add($"ERROR: Native artifact for {manifest.RepositoryId} is missing or unsafe: {artifact.Path}");
                return false;
            }
            using var stream = File.OpenRead(path);
            if (stream.Length != artifact.Bytes || !Convert.ToHexString(SHA256.HashData(stream)).Equals(artifact.Digest, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add($"ERROR: Native artifact for {manifest.RepositoryId} has changed: {artifact.Path}");
                return false;
            }
        }
        return true;
    }

    private IReadOnlyList<TestSuiteProfile> ReadProfile(CisRepositoryContext context, ICollection<string> diagnostics)
    {
        var path = ProfilePath(context);
        if (!File.Exists(path))
        {
            diagnostics.Add($"ERROR: Test-suite profile was not found: {Path.GetRelativePath(context.RepositoryPath, path)}");
            return [];
        }
        var suites = new List<TestSuiteProfile>();
        Dictionary<string, int>? columns = null;
        foreach (var line in File.ReadLines(path))
        {
            if (!line.TrimStart().StartsWith('|')) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(item => item.Trim()).ToArray();
            if (cells.Length < 5) continue;
            if (cells[0].Equals("Suite ID", StringComparison.OrdinalIgnoreCase))
            {
                columns = cells.Select((name, index) => (name, index)).ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
                continue;
            }
            if (cells.All(item => item.All(character => character is '-' or ':' or ' ')) || columns is null) continue;
            string Value(string name, string fallback = "-") => columns.TryGetValue(name, out var index) && index < cells.Length ? cells[index] : fallback;
            suites.Add(new(Value("Suite ID"), Value("Component"), Value("Layer"), Value("Framework"), Value("Command"),
                Value("Working directory", "."), Value("Result format"), Value("Result path"), Value("Coverage path"),
                Value("Mutation path"), Value("Prerequisites"), Value("Applies when"), Value("CI tier"), Value("Artifacts")));
        }
        if (suites.Count == 0) diagnostics.Add("ERROR: Test-suite profile contains no suites.");
        return suites.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void ValidateProfile(CisRepositoryContext context, IReadOnlyList<TestSuiteProfile> suites, bool strict, ICollection<string> diagnostics)
    {
        foreach (var duplicate in suites.GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            diagnostics.Add($"ERROR: Test suite ID is duplicated: {duplicate.Key}");
        foreach (var suite in suites)
        {
            if (!SafeIdPattern().IsMatch(suite.Id)) diagnostics.Add($"ERROR: Test suite ID is invalid: {suite.Id}");
            if (!Layers.Contains(suite.Layer)) diagnostics.Add($"ERROR: Test suite '{suite.Id}' has unsupported layer '{suite.Layer}'.");
            if (!_adapters.ContainsKey(suite.ResultFormat)) diagnostics.Add($"ERROR: Test suite '{suite.Id}' has unsupported result format '{suite.ResultFormat}'.");
            if (Tokenize(suite.Command).Count == 0) diagnostics.Add($"ERROR: Test suite '{suite.Id}' has no executable command.");
            SafePath(context.RepositoryPath, suite.WorkingDirectory, diagnostics, suite.Id, required: false, directory: true);
            var result = SafePath(context.RepositoryPath, suite.ResultPath, diagnostics, suite.Id, required: true);
            if (strict && result is not null)
            {
                var local = Path.Combine(context.RepositoryPath, ".cis", "local");
                if (!CisPathSafety.IsUnderRoot(local, result, allowRoot: false))
                    diagnostics.Add($"ERROR: Test suite '{suite.Id}' result path must remain under .cis/local/.");
            }
            CoveragePath(context.RepositoryPath, suite.CoveragePath, diagnostics, suite.Id, resolve: false);
            SafePath(context.RepositoryPath, suite.MutationPath, diagnostics, suite.Id, required: false);
        }
    }

    private static string? SafePath(string repository, string relative, ICollection<string> diagnostics, string suite,
        bool required, bool directory = false)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative == "-")
        {
            if (required) diagnostics.Add($"ERROR: Test suite '{suite}' has no declared {(directory ? "directory" : "result path")}.");
            return null;
        }
        try
        {
            if (!CisPathSafety.TryResolveUnderRoot(repository, relative, out var path, allowRoot: directory)
                || CisPathSafety.ContainsReparsePoint(repository, path))
            {
                diagnostics.Add($"ERROR: Test suite '{suite}' path escapes the repository or traverses a linked directory: {relative}");
                return null;
            }
            return path;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            diagnostics.Add($"ERROR: Test suite '{suite}' path is invalid: {exception.Message}");
            return null;
        }
    }

    private static TestSuiteExecution Unavailable(TestSuiteProfile suite, string message)
        => new(suite.Id, suite.Layer, suite.Framework, "unavailable", TestFailureKind.MissingPrerequisite,
            0, 0, 0, 0, 0, [], null, null, [], [message]);

    private static TestSuiteExecution InvalidEvidence(TestSuiteProfile suite, string message)
        => new(suite.Id, suite.Layer, suite.Framework, "invalid-evidence", TestFailureKind.InvalidEvidence,
            0, 0, 0, 0, 0, [], null, null, [], [message]);

    private static TestSuiteExecution Failed(TestSuiteProfile suite, WorkflowStepState state)
        => new(suite.Id, suite.Layer, suite.Framework, "failed", ParseFailure(state.FailureKind),
            0, 0, 0, 0, state.DurationMilliseconds, [], null, null, [],
            [state.Error ?? $"Workflow step {state.Id} failed."]);

    private static TestFailureKind ParseFailure(string value) => value.ToLowerInvariant() switch
    {
        "product" => TestFailureKind.Product,
        "infrastructure" => TestFailureKind.Infrastructure,
        "missing-prerequisite" => TestFailureKind.MissingPrerequisite,
        "timeout" => TestFailureKind.Timeout,
        "cancelled" => TestFailureKind.Cancelled,
        "none" => TestFailureKind.None,
        _ => TestFailureKind.Unknown,
    };

    private CisRepositoryContext? Resolve(string repositoryPath, out List<string> diagnostics)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        diagnostics = resolution.Errors.Select(item => "ERROR: " + item).ToList();
        return resolution.Context;
    }

    private static IReadOnlyList<string> Tokenize(string command)
    {
        var result = new List<string>(); var current = new StringBuilder(); var quoted = false;
        foreach (var character in command)
        {
            if (character == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
            }
            else current.Append(character);
        }
        if (!quoted && current.Length > 0) result.Add(current.ToString());
        return quoted ? [] : result;
    }

    private static string ProfilePath(CisRepositoryContext context)
        => Path.Combine(context.DocumentationPath, "references", "test-suite-profile.md");

    private static string ProfileDigest(CisRepositoryContext context)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ProfilePath(context)))).ToLowerInvariant();

    private static string Revision(string repository)
    {
        try
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = repository };
            start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD");
            var result = CisProcessSafety.Run(start, TimeSpan.FromSeconds(10));
            return !result.TimedOut && result.ExitCode == 0 ? result.StandardOutput.Trim() : "unavailable";
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return "unavailable";
        }
    }

    private static string? LatestRun(string repository)
    {
        var root = Path.Combine(repository, LocalRoot.Replace('/', Path.DirectorySeparatorChar), "runs");
        return Directory.Exists(root)
            ? Directory.EnumerateDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc).Select(Path.GetFileName).FirstOrDefault()
            : null;
    }

    private static void Write(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, path, true);
    }

    private static bool IsError(string value) => value.StartsWith("ERROR:", StringComparison.Ordinal);

    private static TestingResult Result(CisRepositoryContext? context, string? change, string? run,
        IReadOnlyList<TestSuiteProfile> suites, TestRunManifest? manifest, IReadOnlyList<TestTraceResult> trace,
        IReadOnlyList<string> diagnostics, bool applied, string status)
        => new(status, context?.RepositoryPath, change, run, suites, manifest, trace, diagnostics, applied);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdPattern();

    [GeneratedRegex(@"(?im)^###\s+(?<id>TC-[A-Z0-9]+(?:-[A-Z0-9]+)*):[^\r\n]*\r?$")]
    private static partial Regex TestCaseHeadingPattern();
}
