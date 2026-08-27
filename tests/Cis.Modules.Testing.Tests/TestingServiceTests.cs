using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Repository;
using Cis.Modules.Testing;
using Cis.Modules.Workflow;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class TestingServiceTests
{
    [Fact]
    public void Inventory_ParsesAndPersistsAValidProfile()
    {
        using var repository = TestRepository.Create();
        repository.Write("docs/cis/references/test-suite-profile.md", Profile("unit", "junit", ".cis/local/results/unit.xml"));
        var service = Service();

        var result = service.Inventory(repository.Path);

        Assert.Equal("inventoried", result.Status);
        Assert.Equal("unit", Assert.Single(result.Suites).Layer);
        Assert.True(File.Exists(Path.Combine(repository.Path, ".cis", "local", "testing", "inventory.json")));
    }

    [Fact]
    public void ValidateStrict_RejectsEvidenceOutsideTheLocalDerivedRoot()
    {
        using var repository = TestRepository.Create();
        repository.Write("docs/cis/references/test-suite-profile.md", Profile("unit", "junit", "test-results/unit.xml"));

        var result = Service().Validate(repository.Path, strict: true);

        Assert.Equal("invalid", result.Status);
        Assert.Contains(result.Diagnostics, item => item.Contains(".cis/local", StringComparison.Ordinal));
    }

    [Fact]
    public void JUnitAdapter_ExtractsExactCaseIdentityAndArtifactHash()
    {
        using var repository = TestRepository.Create();
        repository.Write(".cis/local/results/unit.xml", "<testsuite><testcase classname=\"unit\" name=\"TC-AUTH-001 establishes identity\" time=\"0.2\" /></testsuite>");
        var path = Path.Combine(repository.Path, ".cis", "local", "results", "unit.xml");
        var suite = Suite("api-unit", "unit", "junit", ".cis/local/results/unit.xml");

        var execution = new JUnitTestResultAdapter().Read(new(repository.Path, suite, path, null, null));

        Assert.Equal("passed", execution.Status);
        Assert.Equal("TC-AUTH-001", Assert.Single(execution.Cases).Id);
        Assert.Equal(64, Assert.Single(execution.Artifacts).Digest.Length);
    }

    [Fact]
    public void EmptyResult_IsInvalidEvidenceRatherThanPassed()
    {
        using var repository = TestRepository.Create();
        repository.Write(".cis/local/results/unit.xml", "<testsuite />");
        var path = Path.Combine(repository.Path, ".cis", "local", "results", "unit.xml");

        var execution = new JUnitTestResultAdapter().Read(new(repository.Path,
            Suite("api-unit", "unit", "junit", ".cis/local/results/unit.xml"), path, null, null));

        Assert.Equal("invalid-evidence", execution.Status);
        Assert.Equal(TestFailureKind.InvalidEvidence, execution.FailureKind);
    }

    [Fact]
    public void SkippedOnlyJUnitResult_IsExplicitRatherThanPassed()
    {
        using var repository = TestRepository.Create();
        repository.Write(".cis/local/results/provider.xml", "<testsuite><testcase classname=\"provider\" name=\"managed provider smoke\"><skipped message=\"credentials unavailable\" /></testcase></testsuite>");
        var path = Path.Combine(repository.Path, ".cis", "local", "results", "provider.xml");

        var execution = new JUnitTestResultAdapter().Read(new(repository.Path,
            Suite("api-provider", "integration", "junit", ".cis/local/results/provider.xml"), path, null, null));

        Assert.Equal("skipped", execution.Status);
        Assert.Equal(1, execution.Skipped);
        Assert.Equal("skipped", Assert.Single(execution.Cases).Status);
    }

    [Fact]
    public void Trace_RejectsSourceOnlyReferencesWithoutPassedExecutionEvidence()
    {
        using var repository = TestRepository.Create();
        repository.Write("docs/cis/references/test-suite-profile.md", Profile("unit", "junit", ".cis/local/results/unit.xml"));
        repository.Write("docs/cis/changes/CIS-0001/test-cases.md", "### TC-AUTH-001: Establish identity\n\n- Automated test references: `tests/auth.test.ts`\n");
        var run = new TestRunManifest(1, "RUN-1", "verify", "digest", "fixture", "revision", "profile",
            "test", "2026-08-26T00:00:00Z", "2026-08-26T00:01:00Z", "passed",
            [new("api-unit", "unit", "vitest", "passed", TestFailureKind.None, 1, 1, 0, 0, 1,
                [new("", "auth source mentions no executable case identity", "passed", 1, "tests/auth.test.ts", null)], null, null, [], [])], []);
        repository.Write(".cis/local/testing/runs/RUN-1/manifest.json", JsonSerializer.Serialize(run, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var result = Service().Trace(repository.Path, "CIS-0001", "RUN-1");

        Assert.Equal("incomplete", result.Status);
        Assert.Contains(result.Diagnostics, item => item.Contains("TC-AUTH-001", StringComparison.Ordinal));
    }

    [Fact]
    public void Trace_RejectsChangeIdPathTraversal()
    {
        using var repository = TestRepository.Create();

        var result = Service().Trace(repository.Path, "../outside", "RUN-1");

        Assert.Equal("invalid-change", result.Status);
        Assert.Contains(result.Diagnostics, item => item.Contains("single portable path segment", StringComparison.Ordinal));
    }

    [Fact]
    public void VitestAdapter_ParsesPassedSkippedAndFailedCasesWithCoverage()
    {
        using var repository = TestRepository.Create();
        repository.Write(".cis/local/results/vitest.json", """{"testResults":[{"name":"test/auth.test.ts","assertionResults":[{"fullName":"TC-AUTH-001 works","status":"passed","duration":3},{"fullName":"TC-AUTH-002 waits","status":"skipped"},{"fullName":"TC-AUTH-003 fails","status":"failed","failureMessages":["expected true"]}]}]}""");
        repository.Write(".cis/local/results/coverage.json", """{"total":{"lines":{"pct":96},"statements":{"pct":95},"functions":{"pct":97},"branches":{"pct":94}}}""");
        var execution = new VitestJsonResultAdapter().Read(new(repository.Path,
            Suite("api-unit", "unit", "vitest-json", ".cis/local/results/vitest.json"),
            Path.Combine(repository.Path, ".cis/local/results/vitest.json"),
            Path.Combine(repository.Path, ".cis/local/results/coverage.json"), null));

        Assert.Equal("failed", execution.Status);
        Assert.Equal(3, execution.Total);
        Assert.Equal(96, execution.Coverage!.Lines);
        Assert.Equal(TestFailureKind.Product, execution.FailureKind);
    }

    [Fact]
    public void PlaywrightAdapter_UsesFinalAttemptAndExactCaseIdentity()
    {
        using var repository = TestRepository.Create();
        repository.Write(".cis/local/results/playwright.json", """{"suites":[{"specs":[{"title":"TC-BROWSER-001 journey","file":"test/journey.spec.ts","tests":[{"results":[{"status":"failed","duration":2},{"status":"passed","duration":4}]}]}]}]}""");
        var execution = new PlaywrightJsonResultAdapter().Read(new(repository.Path,
            Suite("web-browser", "browser", "playwright-json", ".cis/local/results/playwright.json"),
            Path.Combine(repository.Path, ".cis/local/results/playwright.json"), null, null));

        Assert.Equal("passed", execution.Status);
        Assert.Equal("TC-BROWSER-001", Assert.Single(execution.Cases).Id);
        Assert.Equal(4, execution.DurationMilliseconds);
    }

    [Fact]
    public void TrxAdapter_ParsesNamespacedResults()
    {
        using var repository = TestRepository.Create();
        repository.Write(".cis/local/results/test.trx", """<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult testName="TC-DOTNET-001 saves" outcome="Passed" duration="00:00:00.125" /></Results></TestRun>""");
        var execution = new TrxTestResultAdapter().Read(new(repository.Path,
            Suite("dotnet", "unit", "trx", ".cis/local/results/test.trx"),
            Path.Combine(repository.Path, ".cis/local/results/test.trx"), null, null));

        Assert.Equal("passed", execution.Status);
        Assert.Equal(125, execution.DurationMilliseconds);
        Assert.Equal("TC-DOTNET-001", Assert.Single(execution.Cases).Id);
    }

    [Fact]
    public void StrykerAdapter_ReportsSurvivorsAsFindings()
    {
        using var repository = TestRepository.Create();
        repository.Write(".cis/local/results/stryker.json", """{"files":{"src/app.ts":{"mutants":[{"status":"Killed"},{"status":"Survived"},{"status":"Timeout"},{"status":"NoCoverage"}]}}}""");
        var execution = new StrykerJsonResultAdapter().Read(new(repository.Path,
            Suite("api-mutation", "mutation", "stryker-json", ".cis/local/results/stryker.json"),
            Path.Combine(repository.Path, ".cis/local/results/stryker.json"), null, null));

        Assert.Equal("findings", execution.Status);
        Assert.Equal(2, execution.Mutation!.Killed + execution.Mutation.TimedOut);
        Assert.Equal(1, execution.Mutation.Survived);
    }

    [Fact]
    public void MalformedAdapterEvidence_IsRejectedByReconcileBoundary()
    {
        using var repository = TestRepository.Create();
        repository.Write(".cis/local/results/vitest.json", "not-json");
        Assert.ThrowsAny<JsonException>(() => new VitestJsonResultAdapter().Read(new(repository.Path,
            Suite("api-unit", "unit", "vitest-json", ".cis/local/results/vitest.json"),
            Path.Combine(repository.Path, ".cis/local/results/vitest.json"), null, null)));
    }

    [Fact]
    public void Reconcile_HashesAndCorrelatesWorkflowAndSuiteDiagnostics()
    {
        using var repository = TestRepository.Create();
        repository.Write("docs/cis/references/test-suite-profile.md", Profile("unit", "junit", ".cis/local/results/unit.xml"));
        repository.Write("docs/cis/workflows/verify.md", """
            | Step | Command | Depends on | Working directory | Test suites | Continue on failure | Timeout seconds |
            |---|---|---|---|---|---|---:|
            | unit | dotnet --version | - | . | api-unit | no | 30 |
            """);
        repository.Write(".cis/local/results/unit.xml", "<testsuite><testcase classname=\"unit\" name=\"TC-AUTH-001 establishes identity\" time=\"0.2\" /></testsuite>");
        repository.Write(".cis/local/workflows/RUN-LOG/unit.log", "[stdout] test output\n");
        repository.Write(".cis/local/testing/diagnostics/api-unit/RUN-LOG/attempt-1/runtime.log", "sanitized runtime evidence\n");
        repository.Write(".cis/local/testing/diagnostics/api-unit/RUN-LOG/attempt-2/runtime.log", "sanitized rerun evidence\n");
        var resolver = new CisRepositoryContextResolver();
        var workflows = new WorkflowService(resolver);
        var definition = workflows.Describe(repository.Path, "verify").Workflow!;
        var runStartedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var state = new WorkflowRunState(2, "RUN-LOG", definition.Id, definition.Digest, "succeeded",
            runStartedAt.ToString("O"), runStartedAt.AddMinutes(1).ToString("O"),
            [new WorkflowStepState("unit", "succeeded", 0, runStartedAt.ToString("O"), runStartedAt.AddMinutes(1).ToString("O"), 60_000, "unit.log", null)]);
        repository.Write(".cis/local/workflows/RUN-LOG/state.json",
            JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var result = Service().Reconcile(repository.Path, "RUN-LOG");

        Assert.Equal("reconciled", result.Status);
        var execution = Assert.Single(result.Manifest!.Suites);
        Assert.Contains(execution.Artifacts, item => item.Kind == "test-result");
        Assert.Contains(execution.Artifacts, item => item.Kind == "workflow-log" && item.Attempt == 1);
        Assert.Contains(execution.Artifacts, item => item.Kind == "test-diagnostic" && item.Attempt == 1);
        Assert.Contains(execution.Artifacts, item => item.Kind == "test-diagnostic" && item.Attempt == 2);
        Assert.All(execution.Artifacts, item =>
        {
            Assert.Equal("RUN-LOG", item.RunId);
            Assert.Equal("api-unit", item.SuiteId);
            Assert.Equal("api", item.Component);
            Assert.Equal(64, item.Digest.Length);
            Assert.Contains("TC-AUTH-001", item.TestCaseIds!);
        });
    }

    private static TestingService Service()
    {
        var resolver = new CisRepositoryContextResolver();
        var adapters = new ICisTestResultAdapter[]
        {
            new JUnitTestResultAdapter(), new CucumberJUnitTestResultAdapter(), new VitestJsonResultAdapter(),
            new PlaywrightJsonResultAdapter(), new TrxTestResultAdapter(), new StrykerJsonResultAdapter(),
        };
        return new TestingService(resolver, new WorkflowService(resolver), adapters);
    }

    private static TestSuiteProfile Suite(string id, string layer, string format, string result)
        => new(id, "api", layer, "fixture", "test", ".", format, result, "-", "-", "-", "always", "pr", "retain");

    private static string Profile(string layer, string format, string result) => $"""
        # Test suite profile

        | Suite ID | Component | Layer | Framework | Command | Working directory | Result format | Result path | Coverage path | Mutation path | Prerequisites | Applies when | CI tier | Artifacts |
        |---|---|---|---|---|---|---|---|---|---|---|---|---|---|
        | api-unit | api | {layer} | fixture | test | . | {format} | {result} | - | - | - | always | pr | retain |
        """;

    private sealed class TestRepository : IDisposable
    {
        public string Path { get; }
        private TestRepository(string path) => Path = path;
        public static TestRepository Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-testing-tests", Guid.NewGuid().ToString("N"));
            var repository = new TestRepository(path);
            repository.Write(".cis/repository.yml", "schema_version: 1\nrepository:\n  id: fixture\ndocumentation_root: docs/cis\n");
            repository.Write("docs/cis/catalog.yml", "schema_version: 1\nrepository: fixture\ndocuments: []\n");
            return repository;
        }
        public void Write(string relative, string content)
        {
            var path = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
