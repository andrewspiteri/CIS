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
    private static readonly HashSet<string> Layers = new(StringComparer.OrdinalIgnoreCase)
    {
        "unit", "architecture", "component", "integration", "business", "frontend-component",
        "browser", "security", "coverage", "mutation", "operational",
    };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ICisRepositoryContextResolver _resolver;
    private readonly ICisWorkspaceRegistry? _workspaceRegistry;
    private readonly WorkflowService _workflows;
    private readonly IReadOnlyDictionary<string, ICisTestResultAdapter> _adapters;

    public TestingService(ICisRepositoryContextResolver resolver, WorkflowService workflows,
        IEnumerable<ICisTestResultAdapter> adapters, ICisWorkspaceRegistry? workspaceRegistry = null)
    {
        _resolver = resolver;
        _workflows = workflows;
        _workspaceRegistry = workspaceRegistry;
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

    public TestingResult Reconcile(string repositoryPath, string runId)
    {
        var validation = Validate(repositoryPath, true);
        if (validation.RepositoryPath is null || validation.Diagnostics.Any(IsError)) return validation with { RunId = runId };
        var context = _resolver.Resolve(validation.RepositoryPath).Context!;
        var diagnostics = validation.Diagnostics.ToList();
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

        var executions = new List<TestSuiteExecution>();
        foreach (var suite in validation.Suites)
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
            if (failedState is not null)
            {
                executions.Add(Failed(suite, failedState));
                continue;
            }

            var resultPath = SafePath(context.RepositoryPath, suite.ResultPath, diagnostics, suite.Id, required: true);
            var coveragePath = SafePath(context.RepositoryPath, suite.CoveragePath, diagnostics, suite.Id, required: false);
            var mutationPath = SafePath(context.RepositoryPath, suite.MutationPath, diagnostics, suite.Id, required: false);
            if (resultPath is null || !File.Exists(resultPath))
            {
                executions.Add(InvalidEvidence(suite, "Workflow succeeded but the declared result file is missing."));
                continue;
            }
            if (!_adapters.TryGetValue(suite.ResultFormat, out var adapter))
            {
                executions.Add(InvalidEvidence(suite, $"No result adapter is registered for '{suite.ResultFormat}'."));
                continue;
            }
            try
            {
                executions.Add(adapter.Read(new(context.RepositoryPath, suite, resultPath,
                    coveragePath is not null && File.Exists(coveragePath) ? coveragePath : null,
                    mutationPath is not null && File.Exists(mutationPath) ? mutationPath : null)));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or System.Xml.XmlException)
            {
                executions.Add(InvalidEvidence(suite, $"Result evidence is unreadable: {exception.Message}"));
            }
        }

        var allArtifacts = executions.SelectMany(item => item.Artifacts)
            .DistinctBy(item => (item.Path, item.Digest))
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var status = executions.Any(item => item.Status is "failed" or "invalid-evidence") ? "failed"
            : executions.Any(item => item.Status is "unavailable") ? "incomplete"
            : executions.Any(item => item.Status is "findings") ? "passed-with-findings" : "passed";
        var manifest = new TestRunManifest(1, runId, workflow.Run.WorkflowId, workflow.Run.WorkflowDigest,
            context.RepositoryId, Revision(context.RepositoryPath), ProfileDigest(context),
            $"{RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}",
            workflow.Run.CreatedAtUtc, workflow.Run.UpdatedAtUtc, status, executions, allArtifacts,
            Environment.GetEnvironmentVariable("CIS_IMPLEMENTER"),
            Environment.GetEnvironmentVariable("CIS_ASSURER"),
            Environment.GetEnvironmentVariable("CIS_ASSURANCE_TECHNIQUE"));
        var runRoot = Path.Combine(context.RepositoryPath, LocalRoot.Replace('/', Path.DirectorySeparatorChar), "runs", runId);
        Directory.CreateDirectory(runRoot);
        Write(Path.Combine(runRoot, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
        Write(Path.Combine(runRoot, "results.json"), JsonSerializer.Serialize(executions, JsonOptions));
        Write(Path.Combine(runRoot, "artifacts.json"), JsonSerializer.Serialize(allArtifacts, JsonOptions));
        return Result(context, null, runId, validation.Suites, manifest, [], diagnostics, true, "reconciled");
    }

    public TestingResult Trace(string repositoryPath, string changeId, string runId)
    {
        var context = Resolve(repositoryPath, out var diagnostics);
        if (context is null) return Result(null, changeId, runId, [], null, [], diagnostics, false, "invalid-repository");
        var testCases = Path.Combine(context.DocumentationPath, "changes", changeId, "test-cases.md");
        if (!File.Exists(testCases))
        {
            diagnostics.Add($"ERROR: Manual test catalogue was not found: {Path.GetRelativePath(context.RepositoryPath, testCases)}");
            return Result(context, changeId, runId, [], null, [], diagnostics, false, "invalid-change");
        }
        var ids = TestCaseHeadingPattern().Matches(File.ReadAllText(testCases)).Select(match => match.Groups["id"].Value.ToUpperInvariant()).Distinct().ToArray();
        var manifests = ReadWorkspaceManifests(context, runId, diagnostics);
        var suiteByCase = manifests.SelectMany(item => item.Suites)
            .SelectMany(suite => suite.Cases.Select(testCase => (Id: suite.SuiteId, Case: testCase)))
            .Where(item => item.Case.Status == "passed" && !string.IsNullOrWhiteSpace(item.Case.Id))
            .GroupBy(item => item.Case.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.Key, item => item.ToArray(), StringComparer.OrdinalIgnoreCase);
        var passed = manifests.SelectMany(item => item.Suites).SelectMany(item => item.Cases)
            .Where(item => item.Status == "passed" && !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.Key, item => item.ToArray(), StringComparer.OrdinalIgnoreCase);
        var trace = ids.Select(id => passed.TryGetValue(id, out var cases)
            ? new TestTraceResult(id, "passed", suiteByCase[id].Select(item => item.Id).Distinct().ToArray(), cases.Select(item => item.Source ?? item.Name).Distinct().ToArray())
            : new TestTraceResult(id, "missing", [], [])).ToArray();
        foreach (var missing in trace.Where(item => item.Status == "missing"))
            diagnostics.Add($"ERROR: {missing.Id} is not present in a passed reconciled test result for run '{runId}'.");
        var combined = manifests.Count == 0 ? null : manifests[0] with
        {
            Suites = manifests.SelectMany(item => item.Suites).ToArray(),
            Artifacts = manifests.SelectMany(item => item.Artifacts).DistinctBy(item => (item.Path, item.Digest)).ToArray(),
            Status = manifests.Any(item => item.Status is "failed" or "invalid-evidence") ? "failed"
                : manifests.Any(item => item.Status == "incomplete") ? "incomplete" : "passed",
        };
        return Result(context, changeId, runId, ReadProfile(context, diagnostics), combined, trace,
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
            var path = Path.Combine(repository.Path, LocalRoot.Replace('/', Path.DirectorySeparatorChar), "runs", runId, "manifest.json");
            if (!File.Exists(path)) continue;
            try
            {
                var manifest = JsonSerializer.Deserialize<TestRunManifest>(File.ReadAllText(path), JsonOptions);
                if (manifest is not null) manifests.Add(manifest);
            }
            catch (JsonException exception)
            {
                diagnostics.Add($"ERROR: Test manifest for {repository.Id} is invalid: {exception.Message}");
            }
        }
        if (manifests.Count == 0) diagnostics.Add($"ERROR: No reconciled test manifest was found for run '{runId}'.");
        return manifests;
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
                var local = Path.Combine(context.RepositoryPath, ".cis", "local") + Path.DirectorySeparatorChar;
                if (!result.StartsWith(local, StringComparison.OrdinalIgnoreCase))
                    diagnostics.Add($"ERROR: Test suite '{suite.Id}' result path must remain under .cis/local/.");
            }
            SafePath(context.RepositoryPath, suite.CoveragePath, diagnostics, suite.Id, required: false);
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
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository));
            var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.Equals(root, StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add($"ERROR: Test suite '{suite}' path escapes the repository: {relative}");
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
            using var process = new Process { StartInfo = new ProcessStartInfo("git") { WorkingDirectory = repository, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
            process.StartInfo.ArgumentList.Add("rev-parse"); process.StartInfo.ArgumentList.Add("HEAD"); process.Start();
            var output = process.StandardOutput.ReadToEnd(); process.WaitForExit(10_000);
            return process.ExitCode == 0 ? output.Trim() : "unavailable";
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
