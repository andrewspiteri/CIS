using System.Security.Cryptography;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Plan;
using Cis.Modules.Repository;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class DeliveryWorkflowTests
{
    [Theory]
    [InlineData("source-only", false)]
    [InlineData("passed", true)]
    [InlineData("unrelated-case", false)]
    [InlineData("failed", false)]
    [InlineData("skipped", false)]
    [InlineData("stale", false)]
    [InlineData("tampered", false)]
    [InlineData("newer-failure", false)]
    [InlineData("wrong-source", false)]
    public void FrontendCatalogueNeedsCurrentNativeResultsBeyondSourceDiscovery(string scenario, bool expected)
    {
        using var repository = TemporaryRepository.Create();
        var services = CreateServices();
        var change = Assert.IsType<ChangeDossier>(services.Changes.Create(new(repository.Path,
            "Required browser coverage", "Operators see the current status.", [new ChangeRoot("orders-api", "component")])).Change);
        foreach (var finding in services.Impacts.Analyse(new(repository.Path, change.Id, [], 2, 200, false)).Findings)
            services.Impacts.Disposition(repository.Path, change.Id, finding.Id, "accepted", "Reviewed the bounded status display scope.");
        DefineAcceptanceCriteria(services.Changes.DossierFile(change, "proposal.md"));
        const string specification = "docs/cis/specs/browser-coverage.md";
        repository.Write(specification, """
            ---
            title: Browser coverage
            type: feature-specification
            ---

            ## Functional requirements

            | ID | Surface | Frontend type | Requirement | Acceptance criteria |
            | --- | --- | --- | --- | --- |
            | STATUS-001 | frontend | backoffice | Display the current status. | The operator sees the current status. |
            """);
        repository.Write("tests/status.spec.ts", "test('TC-STATUS-001-001 displays status', () => {});\n");
        repository.Write("tests/unrelated.spec.ts", "test('TC-UNRELATED displays something else', () => {});\n");
        var imported = services.Plans.ImportSpec(new FeatureSpecImportRequest(repository.Path, change.Id, specification));
        Assert.True(imported.ExitCode == 0, string.Join("; ", imported.Errors.Concat(imported.Validation?.Errors ?? [])));
        var catalogue = File.ReadAllText(services.Changes.DossierFile(change, "test-cases.md"));
        Assert.Contains("- Automation status: Source linked", catalogue, StringComparison.Ordinal);
        Assert.Contains("automated_test_case_count: 0", catalogue, StringComparison.Ordinal);
        Assert.Contains(services.Plans.ValidateManualTestAutomationCoverage(repository.Path, change.Id),
            error => error.Contains("no current executed native result", StringComparison.Ordinal));
        if (scenario != "source-only")
        {
            var context = new CisRepositoryContextResolver().Resolve(repository.Path).Context!;
            var inputDigest = CisExecutionIdentity.Capture(context);
            WriteManifest("first", scenario == "newer-failure" ? "passed" : scenario,
                DateTimeOffset.UtcNow.AddMinutes(-1), context, inputDigest);
            if (scenario == "newer-failure") WriteManifest("second", "failed", DateTimeOffset.UtcNow, context, inputDigest);
            if (scenario == "stale") repository.Write("Changed.ts", "export const changed = true;\n");
            if (scenario == "tampered") File.AppendAllText(Path.Combine(repository.Path, ".cis/local/testing/runs/first/native.json"), " ");
        }
        var errors = services.Plans.ValidateManualTestAutomationCoverage(repository.Path, change.Id);
        Assert.Equal(expected, errors.Count == 0);
        if (!expected) Assert.Contains(errors, error => error.Contains("TC-STATUS-001-001", StringComparison.Ordinal));

        void WriteManifest(string runId, string outcome, DateTimeOffset completed, CisRepositoryContext context, string inputDigest)
        {
            var status = outcome is "failed" or "skipped" ? outcome : "passed";
            var cases = new[] { new { id = outcome == "unrelated-case" ? "TC-OTHER-001" : "TC-STATUS-001-001",
                name = "chromium / status", status, durationMilliseconds = 1,
                source = outcome == "wrong-source" ? "tests/unrelated.spec.ts" : "tests/status.spec.ts" } };
            var reportPath = $".cis/local/testing/runs/{runId}/native.json";
            repository.Write(reportPath, JsonSerializer.Serialize(new { cases }));
            var bytes = File.ReadAllBytes(Path.Combine(repository.Path, reportPath));
            var artifacts = new[] { new { kind = "test-result", path = reportPath,
                digest = Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes = bytes.Length, suiteId = "status-browser" } };
            repository.Write($".cis/local/testing/runs/{runId}/manifest.json", JsonSerializer.Serialize(new
            {
                schemaVersion = 1, repositoryId = context.RepositoryId, inputDigest, completedAtUtc = completed.ToString("O"),
                status = status == "passed" ? "passed" : "failed",
                suites = new[] { new { suiteId = "status-browser", layer = "browser", framework = "playwright", status, total = 1,
                    passed = status == "passed" ? 1 : 0, failed = status == "failed" ? 1 : 0, skipped = status == "skipped" ? 1 : 0,
                    cases, artifacts } }, artifacts
            }));
        }
    }
}
