using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Repository;
using Cis.Modules.Testing;
using Cis.Modules.Workflow;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed partial class TestingServiceTests
{
    [Fact]
    public void SelectedSuiteReconciliationPreservesFullEvidenceAndNativeWorkflowIdentity()
    {
        using var repository = SelectedSuiteRepository();
        var service = Service();
        var full = service.Reconcile(repository.Path, "RUN-SCOPE");
        Assert.Equal("incomplete", full.Manifest!.Status);
        Assert.Null(full.Manifest.Scope);
        var ordinary = Path.Combine(repository.Path, TestingService.LocalRoot, "runs", "RUN-SCOPE");
        var fullBytes = Directory.GetFiles(ordinary).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        Assert.DoesNotContain("\"scope\"", File.ReadAllText(Path.Combine(ordinary, "manifest.json")), StringComparison.Ordinal);

        var selected = service.Reconcile(repository.Path, "RUN-SCOPE", ["API-UNIT"]);

        Assert.Equal(0, selected.ExitCode);
        Assert.True(selected.Applied);
        Assert.Equal("scope-passed", selected.Manifest!.Status);
        Assert.Equal(2, selected.Manifest.SchemaVersion);
        Assert.Equal("RUN-SCOPE", selected.Manifest.RunId);
        var scope = Assert.IsType<TestReconcileScope>(selected.Manifest.Scope);
        Assert.True(scope.Matches(selected.Manifest));
        Assert.Equal(["api-unit"], scope.SelectedSuiteIds);
        Assert.Equal(["other"], scope.OmittedSuiteIds);
        Assert.Equal(scope.Id, selected.RunId);
        Assert.All(selected.Manifest.Artifacts, artifact => Assert.Equal("RUN-SCOPE", artifact.RunId));
        Assert.Equal("passed", Assert.Single(selected.Manifest.Suites).Status);
        foreach (var file in fullBytes) Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(ordinary, file.Key!)));

        var scoped = Path.Combine(repository.Path, TestingService.LocalRoot, "scoped-runs", scope.Id, "manifest.json");
        var before = File.GetLastWriteTimeUtc(scoped);
        Assert.False(service.Reconcile(repository.Path, "RUN-SCOPE", ["api-unit"]).Applied);
        Assert.Equal(before, File.GetLastWriteTimeUtc(scoped));
        repository.Write("docs/cis/changes/CIS-0001/test-cases.md", "### TC-AUTH-001: Identity\n");
        var trace = service.Trace(repository.Path, "CIS-0001", scope.Id);
        Assert.Equal("traced", trace.Status);
        Assert.Equal("scope-passed", trace.Manifest!.Status);
        Assert.Equal("RUN-SCOPE", service.Status(repository.Path, "CIS-0001", null).RunId);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("blank")]
    [InlineData("duplicate")]
    [InlineData("unsafe")]
    [InlineData("unknown")]
    [InlineData("unbound")]
    [InlineData("too-many")]
    [InlineData("missing-inputs")]
    public void SelectedSuiteReconciliationRejectsUnverifiedSelectionBeforePublication(string scenario)
    {
        using var repository = SelectedSuiteRepository();
        string[] selection = scenario switch
        {
            "empty" => [], "blank" => [""], "duplicate" => ["api-unit", "API-UNIT"],
            "unsafe" => ["../api-unit"], "unknown" => ["absent"], "unbound" => ["other"],
            "too-many" => Enumerable.Range(0, 101).Select(index => "suite-" + index).ToArray(), _ => ["api-unit"]
        };
        if (scenario == "missing-inputs")
        {
            var path = Path.Combine(repository.Path, ".cis/local/workflows/RUN-SCOPE/state.json");
            var state = JsonSerializer.Deserialize<WorkflowRunState>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            repository.Write(".cis/local/workflows/RUN-SCOPE/state.json", JsonSerializer.Serialize(state with { InputDigest = null }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }

        var result = Service().Reconcile(repository.Path, "RUN-SCOPE", selection);

        Assert.Equal(4, result.ExitCode);
        Assert.Equal("invalid-selection", result.Status);
        Assert.False(result.Applied);
        Assert.False(Directory.Exists(Path.Combine(repository.Path, TestingService.LocalRoot, "scoped-runs")));
    }

    [Fact]
    public void SelectedSuiteReconciliationKeepsDistinctScopeAndResumeEvidenceImmutable()
    {
        using var repository = SelectedSuiteRepository(bindOther: true);
        var service = Service();
        var first = service.Reconcile(repository.Path, "RUN-SCOPE", ["api-unit"]);
        var firstPath = Path.Combine(repository.Path, TestingService.LocalRoot, "scoped-runs", first.RunId!, "manifest.json");
        var original = File.ReadAllBytes(firstPath);
        var all = service.Reconcile(repository.Path, "RUN-SCOPE", ["other", "api-unit"]);
        Assert.NotEqual(first.RunId, all.RunId);
        Assert.Equal("scope-passed", all.Manifest!.Status);
        Assert.Empty(all.Manifest.Scope!.OmittedSuiteIds);
        var statePath = Path.Combine(repository.Path, ".cis/local/workflows/RUN-SCOPE/state.json");
        var state = JsonSerializer.Deserialize<WorkflowRunState>(File.ReadAllText(statePath), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        repository.Write(".cis/local/workflows/RUN-SCOPE/state.json", JsonSerializer.Serialize(state with
        { UpdatedAtUtc = DateTimeOffset.Parse(state.UpdatedAtUtc).AddSeconds(1).ToString("O") }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var resumed = service.Reconcile(repository.Path, "RUN-SCOPE", ["api-unit"]);
        Assert.NotEqual(first.RunId, resumed.RunId);
        Assert.Equal(original, File.ReadAllBytes(firstPath));

        repository.Write(".cis/local/results/unit.xml", "<testsuite><testcase name=\"TC-AUTH-001 changed bytes\" /></testsuite>");
        var conflict = service.Reconcile(repository.Path, "RUN-SCOPE", ["api-unit"]);
        Assert.Equal("invalid-evidence", conflict.Status);
        Assert.False(conflict.Applied);
        Assert.Contains(conflict.Diagnostics, message => message.Contains("different bytes", StringComparison.Ordinal));
    }

    [Fact]
    public void ReconcileCommandSelectsSuitesExplicitlyAndRejectsMissingOptionValue()
    {
        using var repository = SelectedSuiteRepository(bindOther: true);
        var registry = new MutationCommandRegistry();
        new TestingModule().RegisterCommands(registry, new MutationCommandServices(Service()));
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var previous = Console.Out;
        try
        {
            Console.SetOut(output);
            Assert.Equal(0, registry.Root.Parse(["test", "reconcile", "--run", "RUN-SCOPE", "--repo", repository.Path,
                "--suite", "api-unit", "--suite", "other", "--format", "json"]).Invoke());
        }
        finally { Console.SetOut(previous); }
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("scope-passed", result.RootElement.GetProperty("manifest").GetProperty("status").GetString());
        Assert.Equal(2, result.RootElement.GetProperty("manifest").GetProperty("scope").GetProperty("selectedSuiteIds").GetArrayLength());
        Assert.NotEmpty(registry.Root.Parse(["test", "reconcile", "--run", "RUN-SCOPE", "--repo", repository.Path, "--suite"]).Errors);
    }

    [Theory]
    [InlineData("<testsuite />", "failed")]
    [InlineData("<testsuite><testcase name=\"failed\"><failure message=\"failure\" /></testcase></testsuite>", "failed")]
    [InlineData("<testsuite><testcase name=\"skipped\"><skipped /></testcase></testsuite>", "incomplete")]
    public void SelectedSuiteReconciliationDoesNotPromoteNonPassingNativeResults(string report, string expected)
    {
        using var repository = SelectedSuiteRepository();
        repository.Write(".cis/local/results/unit.xml", report);
        var result = Service().Reconcile(repository.Path, "RUN-SCOPE", ["api-unit"]);
        Assert.Equal(expected, result.Manifest!.Status);
        Assert.NotEqual("scope-passed", result.Manifest.Status);
        Assert.Equal(2, result.Manifest.SchemaVersion);
    }

    [Fact]
    public void SelectedSuiteReferencesCannotTraverseOutsideTheLocalEvidenceNamespace()
    {
        using var repository = SelectedSuiteRepository();
        var result = Service().Trace(repository.Path, "CIS-0001", "../outside");
        Assert.Equal("invalid-run", result.Status);
        Assert.Equal(4, result.ExitCode);
    }

    [Fact]
    public async Task SelectedSuitePublicationSerializesIdenticalObserversWithoutReplacingEvidence()
    {
        using var repository = SelectedSuiteRepository();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
            Service().Reconcile(repository.Path, "RUN-SCOPE", ["api-unit"]))));
        Assert.All(results, result => Assert.Equal(0, result.ExitCode));
        Assert.Single(results, result => result.Applied);
        Assert.Equal(results[0].RunId, results[1].RunId);
        var manifest = Assert.IsType<TestRunManifest>(results[0].Manifest);
        Assert.True(Assert.IsType<TestReconcileScope>(manifest.Scope).Matches(manifest));
    }

    [Theory]
    [InlineData("ordinary", false)]
    [InlineData("ordinary", true)]
    [InlineData("symbolic", false)]
    [InlineData("symbolic", true)]
    [InlineData("hard", false)]
    [InlineData("hard", true)]
    public void SelectedSuitePublicationPreservesUnexplainedTemporaryPathsAndTheirTargets(string kind, bool existingEvidence)
    {
        using var repository = SelectedSuiteRepository();
        using var outside = TestRepository.Create();
        outside.Write("protected.txt", "Protected target bytes must remain unchanged.");
        var target = Path.Combine(outside.Path, "protected.txt");
        var result = Service().Reconcile(repository.Path, "RUN-SCOPE", ["api-unit"]);
        var root = Path.Combine(repository.Path, TestingService.LocalRoot, "scoped-runs", result.RunId!);
        var originals = Directory.GetFiles(root).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        if (!existingEvidence)
            foreach (var original in originals)
            {
                var path = Path.Combine(root, original.Key);
                Assert.True(CisPathSafety.IsUnderRoot(repository.Path, path));
                File.Delete(path); // Remove only this test's newly published fixture payloads.
            }
        var temporary = Path.Combine(root, "manifest.json.tmp");
        if (kind == "ordinary") File.WriteAllText(temporary, "Unexplained previous partial publication.");
        else if (kind == "symbolic") File.CreateSymbolicLink(temporary, target);
        else
        {
            var start = new System.Diagnostics.ProcessStartInfo(OperatingSystem.IsWindows() ? "fsutil.exe" : "/bin/ln")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in OperatingSystem.IsWindows()
                ? new[] { "hardlink", "create", temporary, target } : new[] { target, temporary }) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!;
            Assert.True(process.WaitForExit(5000));
            Assert.Equal(0, process.ExitCode);
        }
        var temporaryBytes = File.ReadAllBytes(temporary);
        var rejected = Service().Reconcile(repository.Path, "RUN-SCOPE", ["api-unit"]);
        Assert.Equal("invalid-evidence", rejected.Status);
        Assert.False(rejected.Applied);
        Assert.Equal("Protected target bytes must remain unchanged.", File.ReadAllText(target));
        Assert.Equal(temporaryBytes, File.ReadAllBytes(temporary));
        foreach (var original in originals)
            if (existingEvidence) Assert.Equal(original.Value, File.ReadAllBytes(Path.Combine(root, original.Key)));
            else Assert.False(File.Exists(Path.Combine(root, original.Key)));
    }

    private static TestRepository SelectedSuiteRepository(bool bindOther = false)
    {
        var repository = TestRepository.Create();
        var profile = Profile("unit", "junit", ".cis/local/results/unit.xml");
        repository.Write("docs/cis/references/test-suite-profile.md", profile + "\n"
            + "| other | api | unit | fixture | test | . | junit | .cis/local/results/unit.xml | - | - | - | always | pr | retain |\n");
        repository.Write("docs/cis/workflows/verify.md", """
            | Step | Command | Depends on | Working directory | Test suites | Continue on failure | Timeout seconds |
            |---|---|---|---|---|---|---:|
            """ + "\n| unit | dotnet --version | - | . | " + (bindOther ? "api-unit, other" : "api-unit") + " | no | 30 |\n");
        var resolver = new CisRepositoryContextResolver();
        var context = resolver.Resolve(repository.Path).Context!;
        var definition = new WorkflowService(resolver).Describe(repository.Path, "verify").Workflow!;
        var started = DateTimeOffset.UtcNow.AddMinutes(-2);
        var state = new WorkflowRunState(2, "RUN-SCOPE", definition.Id, definition.Digest, "succeeded", started.ToString("O"),
            started.AddMinutes(1).ToString("O"), [new("unit", "succeeded", 0, started.ToString("O"), started.AddMinutes(1).ToString("O"), 1, "unit.log", null)],
            CisExecutionIdentity.Capture(context));
        repository.Write(".cis/local/workflows/RUN-SCOPE/state.json", JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        repository.Write(".cis/local/workflows/RUN-SCOPE/unit.log", "Fixture workflow output; this protocol fixture does not claim a real native test run.\n");
        repository.Write(".cis/local/results/unit.xml", "<testsuite><testcase name=\"TC-AUTH-001 identity\" /></testsuite>");
        return repository;
    }
}
