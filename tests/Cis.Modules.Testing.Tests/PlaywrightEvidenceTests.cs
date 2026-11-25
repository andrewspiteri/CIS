using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Cis.Modules.Repository;
using Cis.Modules.Testing;
using Cis.Modules.Workflow;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed partial class TestingServiceTests
{
    private const string BrowserResultPath = ".cis/local/results/playwright.json";
    private const string BrowserMixedResults = """
        {"suites":[{"title":"journey.spec.ts","specs":[
          {"title":"TC-BROWSER-001 loads","file":"tests/journey.spec.ts","tests":[{"projectName":"chromium","expectedStatus":"passed","status":"expected","results":[{"status":"passed","duration":1}]}]},
          {"title":"TC-BROWSER-002 saves","file":"tests/journey.spec.ts","tests":[{"projectName":"chromium","expectedStatus":"passed","status":"unexpected","results":[{"status":"failed","duration":2,"error":{"message":"Save button did not update the view."}}]}]},
          {"title":"TC-BROWSER-003 waits","file":"tests/journey.spec.ts","tests":[{"projectName":"chromium","expectedStatus":"skipped","status":"skipped","results":[{"status":"skipped","duration":0}]}]}
        ]}],"errors":[]}
        """;

    [Theory]
    [InlineData("product", TestFailureKind.Product)]
    [InlineData("infrastructure", TestFailureKind.Infrastructure)]
    [InlineData("timeout", TestFailureKind.Timeout)]
    [InlineData("cancelled", TestFailureKind.Cancelled)]
    public void Reconcile_FailedPlaywrightKeepsNativeCasesAndOriginalProcessFailure(string kind, TestFailureKind expected)
    {
        using var repository = TestRepository.Create();
        BrowserWorkflow(repository, "failed", kind);
        repository.Write(BrowserResultPath, BrowserMixedResults);

        var result = Service().Reconcile(repository.Path, "browser-run");

        var suite = Assert.Single(result.Manifest!.Suites);
        Assert.Equal("failed", result.Manifest.Status);
        Assert.Equal("failed", suite.Status);
        Assert.Equal(expected, suite.FailureKind);
        Assert.Equal((3, 1, 1, 1), (suite.Total, suite.Passed, suite.Failed, suite.Skipped));
        Assert.Equal("Save button did not update the view.", suite.Cases.Single(item => item.Id == "TC-BROWSER-002").Failure);
        Assert.Contains(suite.Diagnostics, item => item == "Browser process failed.");
        var report = Assert.Single(suite.Artifacts, item => item.Kind == "test-result");
        Assert.Equal("browser-run", report.RunId);
        Assert.Equal("api-unit", report.SuiteId);
        Assert.Equal(1, report.Attempt);
        Assert.Equal(64, report.Digest.Length);
        Assert.Equal(new[] { "TC-BROWSER-001", "TC-BROWSER-002", "TC-BROWSER-003" }, report.TestCaseIds);
        Assert.Equal(4, result.ExitCode);
    }

    [Fact]
    public void Reconcile_PassingPlaywrightCasesCannotClearAProcessFailure()
    {
        using var repository = TestRepository.Create();
        BrowserWorkflow(repository, "failed", "infrastructure");
        repository.Write(BrowserResultPath, BrowserSingleResult("passed", "passed", "expected"));

        var result = Service().Reconcile(repository.Path, "browser-run");

        var suite = Assert.Single(result.Manifest!.Suites);
        Assert.Equal("failed", suite.Status);
        Assert.Equal(TestFailureKind.Infrastructure, suite.FailureKind);
        Assert.Equal(1, suite.Passed);
        Assert.Equal(4, result.ExitCode);
    }

    [Theory]
    [InlineData("missing", "missing")]
    [InlineData("stale", "predates")]
    [InlineData("future", "after this execution")]
    [InlineData("malformed", "unreadable")]
    [InlineData("wrong-shape", "must be an array")]
    [InlineData("empty", "no test cases")]
    public void Reconcile_FailedPlaywrightRejectsUnavailableNativeEvidence(string defect, string diagnostic)
    {
        using var repository = TestRepository.Create();
        BrowserWorkflow(repository, "failed", "product");
        if (defect != "missing") repository.Write(BrowserResultPath, defect switch
        {
            "malformed" => "not-json",
            "wrong-shape" => "{\"suites\":{}}",
            "empty" => "{\"suites\":[]}",
            _ => BrowserMixedResults,
        });
        if (defect is "stale" or "future")
            File.SetLastWriteTimeUtc(Path.Combine(repository.Path, BrowserResultPath), DateTime.UtcNow.AddHours(defect == "stale" ? -1 : 1));

        var result = Service().Reconcile(repository.Path, "browser-run");

        var suite = Assert.Single(result.Manifest!.Suites);
        Assert.Equal("failed", suite.Status);
        Assert.Equal(TestFailureKind.Product, suite.FailureKind);
        Assert.Empty(suite.Cases);
        Assert.Contains(suite.Diagnostics, item => item.Contains(diagnostic, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(suite.Diagnostics, item => item == "Browser process failed.");
        Assert.Equal(4, result.ExitCode);
    }

    [Fact]
    public void Reconcile_SuccessfulLegacyBrowserRunStillRejectsStaleEvidence()
    {
        using var repository = TestRepository.Create();
        BrowserWorkflow(repository, captureInputs: false);
        repository.Write(BrowserResultPath, BrowserSingleResult("passed", "passed", "expected"));
        File.SetLastWriteTimeUtc(Path.Combine(repository.Path, BrowserResultPath), DateTime.UtcNow.AddHours(-1));

        var suite = Assert.Single(Service().Reconcile(repository.Path, "browser-run").Manifest!.Suites);

        Assert.Equal("invalid-evidence", suite.Status);
        Assert.Empty(suite.Cases);
        Assert.Contains(suite.Diagnostics, item => item.Contains("predates", StringComparison.Ordinal));
    }

    [Fact]
    public void Reconcile_BlockedBrowserStepCannotImportAnUnrelatedReport()
    {
        using var repository = TestRepository.Create();
        BrowserWorkflow(repository, "blocked", "missing-prerequisite");
        repository.Write(BrowserResultPath, BrowserMixedResults);

        var suite = Assert.Single(Service().Reconcile(repository.Path, "browser-run").Manifest!.Suites);

        Assert.Equal("failed", suite.Status);
        Assert.Empty(suite.Cases);
        Assert.DoesNotContain(suite.Artifacts, item => item.Kind == "test-result");
    }

    [Fact]
    public void Reconcile_UsesTheCurrentRunAndAttemptResultPath()
    {
        using var repository = TestRepository.Create();
        const string template = ".cis/local/testing/results/browser/{run-id}/attempt-{attempt}/playwright.json";
        const string current = ".cis/local/testing/results/browser/browser-run/attempt-2/playwright.json";
        BrowserWorkflow(repository, resultPath: template, attempt: 2);
        repository.Write(".cis/local/testing/results/browser/browser-run/attempt-1/playwright.json", BrowserMixedResults);
        repository.Write(current, BrowserSingleResult("passed", "passed", "expected"));

        var suite = Assert.Single(Service().Reconcile(repository.Path, "browser-run").Manifest!.Suites);

        Assert.Equal("passed", suite.Status);
        Assert.Equal(1, suite.Total);
        var report = Assert.Single(suite.Artifacts, item => item.Kind == "test-result");
        Assert.Equal(current, report.Path);
        Assert.Equal(2, report.Attempt);
    }

    [Fact]
    public void Reconcile_MissingRetryReportCannotReuseAnEarlierPassingAttempt()
    {
        using var repository = TestRepository.Create();
        const string template = ".cis/local/testing/results/browser/{run-id}/attempt-{attempt}/playwright.json";
        BrowserWorkflow(repository, "failed", "infrastructure", template, attempt: 2);
        repository.Write(".cis/local/testing/results/browser/browser-run/attempt-1/playwright.json",
            BrowserSingleResult("passed", "passed", "expected"));

        var suite = Assert.Single(Service().Reconcile(repository.Path, "browser-run").Manifest!.Suites);

        Assert.Equal("failed", suite.Status);
        Assert.Empty(suite.Cases);
        Assert.Contains(suite.Diagnostics, item => item.Contains("missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Reconcile_TokenizedReportPathsRequireAnUnambiguousProducingStep()
    {
        using var repository = TestRepository.Create();
        BrowserWorkflow(repository, resultPath: ".cis/local/testing/{run-id}/attempt-{attempt}/playwright.json");
        File.AppendAllText(Path.Combine(repository.Path, "docs/cis/workflows/verify.md"), "| other | dotnet --version | - | no | 30 | api-unit |\n");
        var definition = new WorkflowService(new CisRepositoryContextResolver()).Describe(repository.Path, "verify").Workflow!;
        const string statePath = ".cis/local/workflows/browser-run/state.json";
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var state = JsonSerializer.Deserialize<WorkflowRunState>(File.ReadAllText(Path.Combine(repository.Path, statePath)), json)!;
        state = state with { WorkflowDigest = definition.Digest, Steps = [state.Steps[0], state.Steps[0] with { Id = "other" }] };
        repository.Write(statePath, JsonSerializer.Serialize(state, json));

        var suite = Assert.Single(Service().Reconcile(repository.Path, "browser-run").Manifest!.Suites);

        Assert.Equal("invalid-evidence", suite.Status);
        Assert.Contains(suite.Diagnostics, item => item.Contains("exactly one workflow step", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("passed", "passed", "expected", "passed")]
    [InlineData("passed", "failed", "unexpected", "failed")]
    [InlineData("failed", "failed", "expected", "failed")]
    [InlineData("expected", "passed", "expected", "failed")]
    [InlineData("timedOut", "passed", "unexpected", "failed")]
    [InlineData("interrupted", "passed", "unexpected", "failed")]
    [InlineData("skipped", "skipped", "skipped", "skipped")]
    public void PlaywrightAdapter_OnlyActualExpectedPassesQualify(string actual, string expected, string outcome, string status)
    {
        using var repository = TestRepository.Create();
        repository.Write(BrowserResultPath, BrowserSingleResult(actual, expected, outcome));

        var execution = ReadBrowser(repository);

        Assert.Equal(status, Assert.Single(execution.Cases).Status);
        Assert.Equal(status == "passed" ? 1 : 0, execution.Passed);
    }

    [Fact]
    public void PlaywrightAdapter_PreservesProjectAndNestedTitleIdentity()
    {
        using var repository = TestRepository.Create();
        repository.Write(BrowserResultPath, """
            {"suites":[{"title":"journey.spec.ts","suites":[{"title":"permissions","specs":[{
              "title":"TC-BROWSER-001 edits","file":"tests/journey.spec.ts","tests":[
                {"projectName":"chromium","results":[{"status":"passed"}]},
                {"projectName":"firefox","results":[{"status":"failed"}]}
              ]
            }]}]}]}
            """);

        var execution = ReadBrowser(repository);

        Assert.Equal((2, 1, 1), (execution.Total, execution.Passed, execution.Failed));
        Assert.All(execution.Cases, item => Assert.Equal("TC-BROWSER-001", item.Id));
        Assert.Contains(execution.Cases, item => item.Name == "[chromium] journey.spec.ts > permissions > TC-BROWSER-001 edits");
        Assert.Contains(execution.Cases, item => item.Name == "[firefox] journey.spec.ts > permissions > TC-BROWSER-001 edits");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PlaywrightAdapter_ResolvesSourceAgainstTheNativeReportRoot(bool absoluteRoot)
    {
        using var repository = TestRepository.Create();
        repository.Write("apps/portal/tests/journey.spec.ts", "// Native source fixture.\n");
        var report = JsonNode.Parse(BrowserSingleResult("passed", "passed", "expected"))!;
        report["config"] = new JsonObject { ["rootDir"] = absoluteRoot ? Path.Combine(repository.Path, "apps/portal/tests") : "tests" };
        report["suites"]![0]!["specs"]![0]!["file"] = "journey.spec.ts";
        repository.Write(BrowserResultPath, report.ToJsonString());
        var suite = Suite("browser", "browser", "playwright-json", BrowserResultPath) with { WorkingDirectory = "apps/portal" };

        var execution = new PlaywrightJsonResultAdapter().Read(new(repository.Path, suite,
            Path.Combine(repository.Path, BrowserResultPath), null, null));

        Assert.Equal("apps/portal/tests/journey.spec.ts", Assert.Single(execution.Cases).Source);
    }

    [Fact]
    public void PlaywrightAdapter_GlobalRunnerErrorsPreventPassing()
    {
        using var repository = TestRepository.Create();
        var report = JsonNode.Parse(BrowserSingleResult("passed", "passed", "expected"))!;
        report["errors"] = JsonNode.Parse("[{\"message\":\"Global teardown failed.\"}]");
        repository.Write(BrowserResultPath, report.ToJsonString());

        var execution = ReadBrowser(repository);

        Assert.Equal("failed", execution.Status);
        Assert.Equal(TestFailureKind.Infrastructure, execution.FailureKind);
        Assert.Equal(1, execution.Passed);
        Assert.Contains(execution.Diagnostics, item => item.Contains("Global teardown failed.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"suites\":{}}")]
    [InlineData("{\"suites\":[null]}")]
    [InlineData("{\"suites\":[{\"specs\":{}}]}")]
    [InlineData("{\"suites\":[{\"specs\":[{\"tests\":[{\"results\":{}}]}]}]}")]
    public void PlaywrightAdapter_MalformedShapesProduceInvalidEvidence(string report)
    {
        using var repository = TestRepository.Create();
        repository.Write(BrowserResultPath, report);

        Assert.Throws<InvalidDataException>(() => ReadBrowser(repository));
    }

    [Fact]
    public void Trace_CurrentNativeBrowserCasePasses()
    {
        using var repository = TestRepository.Create();
        BrowserWorkflow(repository);
        repository.Write(BrowserResultPath, BrowserSingleResult("passed", "passed", "expected"));
        Service().Reconcile(repository.Path, "browser-run");
        repository.Write("docs/cis/changes/CIS-0001/test-cases.md", "### TC-BROWSER-001: Load view\n");

        var result = Service().Trace(repository.Path, "CIS-0001", "browser-run");

        Assert.Equal("traced", result.Status);
        Assert.Equal("passed", Assert.Single(result.Trace).Status);
    }

    [Fact]
    public void Trace_TargetedPassingBrowserCaseIsNotMissingBecauseAnUnboundSuiteIsUnavailable()
    {
        using var repository = TestRepository.Create();
        BrowserWorkflow(repository);
        File.AppendAllText(Path.Combine(repository.Path, "docs/cis/references/test-suite-profile.md"),
            "\n| unrelated | api | unit | fixture | test | . | junit | .cis/local/results/unrelated.xml | - | - | - | always | pr | retain |\n");
        const string statePath = ".cis/local/workflows/browser-run/state.json";
        var state = JsonNode.Parse(File.ReadAllText(Path.Combine(repository.Path, statePath)))!;
        state["inputDigest"] = CisExecutionIdentity.Capture(new CisRepositoryContextResolver().Resolve(repository.Path).Context!);
        repository.Write(statePath, state.ToJsonString());
        repository.Write(BrowserResultPath, BrowserSingleResult("passed", "passed", "expected"));
        var reconciled = Service().Reconcile(repository.Path, "browser-run");
        Assert.Equal("incomplete", reconciled.Manifest!.Status);
        repository.Write("docs/cis/changes/CIS-0001/test-cases.md", "### TC-BROWSER-001: Load view\n");

        var result = Service().Trace(repository.Path, "CIS-0001", "browser-run");

        Assert.Equal("traced", result.Status);
        Assert.Equal("passed", Assert.Single(result.Trace).Status);
    }

    [Fact]
    public void Trace_LegacyBrowserRunWithoutAnInputDigestRequiresAFreshRun()
    {
        using var repository = TestRepository.Create();
        BrowserWorkflow(repository, captureInputs: false);
        repository.Write(BrowserResultPath, BrowserSingleResult("passed", "passed", "expected"));
        Service().Reconcile(repository.Path, "browser-run");
        repository.Write("docs/cis/changes/CIS-0001/test-cases.md", "### TC-BROWSER-001: Load view\n");

        var result = Service().Trace(repository.Path, "CIS-0001", "browser-run");

        Assert.Equal("incomplete", result.Status);
        Assert.DoesNotContain(result.Trace, item => item.Status == "passed");
        Assert.Contains(result.Diagnostics, item => item.Contains("start a fresh workflow run", StringComparison.Ordinal));
    }

    [Fact]
    public void BrowserWorkflow_CapturesUnadoptedInputsAndRejectsChangedInputsOnResume()
    {
        using var repository = TestRepository.Create();
        BrowserWorkflow(repository);
        var resolver = new CisRepositoryContextResolver();
        var context = resolver.Resolve(repository.Path).Context!;
        Assert.Null(CisExecutionIdentity.CaptureIfAdopted(context));
        var digest = CisExecutionIdentity.Capture(context);
        var service = new WorkflowService(resolver);

        var run = service.Run(repository.Path, "verify", "fresh-browser-run");

        Assert.Equal("succeeded", run.Status);
        Assert.Equal(digest, run.Run!.InputDigest);
        File.AppendAllText(Path.Combine(repository.Path, "tests/journey.spec.ts"), "\n// Changed behavior.\n");
        var resumed = service.Run(repository.Path, "verify", "fresh-browser-run");
        Assert.Equal("inputs-changed", resumed.Status);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("skipped")]
    public void Trace_PassingProjectCannotHideAnotherProjectWithoutAPass(string otherStatus)
    {
        using var repository = TestRepository.Create();
        BrowserWorkflow(repository);
        var report = JsonNode.Parse(BrowserSingleResult("passed", "passed", "expected"))!;
        var tests = report["suites"]![0]!["specs"]![0]!["tests"]!.AsArray();
        tests.Add(JsonNode.Parse(BrowserSingleResult(otherStatus, "passed", "unexpected"))!["suites"]![0]!["specs"]![0]!["tests"]![0]!.DeepClone());
        repository.Write(BrowserResultPath, report.ToJsonString());
        Service().Reconcile(repository.Path, "browser-run");
        repository.Write("docs/cis/changes/CIS-0001/test-cases.md", "### TC-BROWSER-001: Load view\n");

        var result = Service().Trace(repository.Path, "CIS-0001", "browser-run");

        Assert.Equal("incomplete", result.Status);
        Assert.Equal("missing", Assert.Single(result.Trace).Status);
    }

    [Theory]
    [InlineData("report")]
    [InlineData("profile")]
    [InlineData("workflow")]
    [InlineData("input-digest")]
    [InlineData("source")]
    public void Trace_RejectsEvidenceWhoseNativeReportOrMaterialBasisChanged(string change)
    {
        using var repository = TestRepository.Create();
        BrowserWorkflow(repository);
        repository.Write(BrowserResultPath, BrowserSingleResult("passed", "passed", "expected"));
        Service().Reconcile(repository.Path, "browser-run");
        repository.Write("docs/cis/changes/CIS-0001/test-cases.md", "### TC-BROWSER-001: Load view\n");
        if (change == "input-digest")
        {
            const string manifestPath = ".cis/local/testing/runs/browser-run/manifest.json";
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(repository.Path, manifestPath)))!;
            manifest["inputDigest"] = "different-inputs";
            repository.Write(manifestPath, manifest.ToJsonString());
        }
        else File.AppendAllText(Path.Combine(repository.Path, change switch
        {
            "report" => BrowserResultPath,
            "profile" => "docs/cis/references/test-suite-profile.md",
            "source" => "tests/journey.spec.ts",
            _ => "docs/cis/workflows/verify.md",
        }), "\n");

        var result = Service().Trace(repository.Path, "CIS-0001", "browser-run");

        Assert.Equal("incomplete", result.Status);
        Assert.DoesNotContain(result.Trace, item => item.Status == "passed");
        Assert.Contains(result.Diagnostics, item => item.StartsWith("ERROR:", StringComparison.Ordinal));
    }

    private static TestSuiteExecution ReadBrowser(TestRepository repository)
        => new PlaywrightJsonResultAdapter().Read(new(repository.Path,
            Suite("browser", "browser", "playwright-json", BrowserResultPath),
            Path.Combine(repository.Path, BrowserResultPath), null, null));

    private static string BrowserSingleResult(string actual, string expected, string outcome)
        => JsonSerializer.Serialize(new
        {
            suites = new[] { new { specs = new[] { new
            {
                title = "TC-BROWSER-001 loads", file = "tests/journey.spec.ts",
                tests = new[] { new { projectName = "chromium", expectedStatus = expected, status = outcome,
                    results = new[] { new { status = actual, duration = 1 } } } },
            } } } },
        });

    private static void BrowserWorkflow(TestRepository repository, string state = "succeeded", string failureKind = "none",
        string resultPath = BrowserResultPath, int attempt = 1, bool captureInputs = true)
    {
        repository.Write("docs/cis/references/test-suite-profile.md", Profile("browser", "playwright-json", resultPath));
        repository.Write("docs/cis/workflows/verify.md", "| Step | Command | Depends on | Continue on failure | Timeout seconds | Test suites |\n| browser | dotnet --version | - | no | 30 | api-unit |\n");
        repository.Write("tests/journey.spec.ts", "test('TC-BROWSER-001 loads', async ({ page }) => { await expect(page.getByRole('heading')).toHaveText('Overview'); });\n");
        var resolver = new CisRepositoryContextResolver();
        var definition = new WorkflowService(resolver).Describe(repository.Path, "verify").Workflow!;
        var digest = captureInputs ? CisExecutionIdentity.Capture(resolver.Resolve(repository.Path).Context!) : null;
        var started = DateTimeOffset.UtcNow.AddMinutes(-1);
        var completed = DateTimeOffset.UtcNow.AddMinutes(1);
        repository.Write(".cis/local/workflows/browser-run/state.json", JsonSerializer.Serialize(new WorkflowRunState(2,
            "browser-run", "verify", definition.Digest, state, started.ToString("O"), completed.ToString("O"),
            [new("browser", state, state == "succeeded" ? 0 : 1, started.ToString("O"), completed.ToString("O"), 1,
                "browser.log", state == "succeeded" ? null : "Browser process failed.", Attempt: attempt, FailureKind: failureKind)], digest),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
