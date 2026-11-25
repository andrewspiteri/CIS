using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Plan;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class EngineeringCompletionTests
{
    [Theory]
    [InlineData("complete", true)]
    [InlineData("missing-journey", false)]
    [InlineData("prefix-only", false)]
    [InlineData("unrelated-suite", false)]
    [InlineData("missing-cases", false)]
    [InlineData("skipped-project", false)]
    [InlineData("failed-project", false)]
    [InlineData("stale-inputs", false)]
    [InlineData("tampered-native", false)]
    [InlineData("missing-catalogue", false)]
    [InlineData("missing-requirement", false)]
    [InlineData("duplicate-catalogue-id", false)]
    [InlineData("explicit-task-binding", true)]
    [InlineData("name-only-binding", true)]
    [InlineData("foreign-suite-report", false)]
    [InlineData("wrong-source", false)]
    [InlineData("missing-source", false)]
    [InlineData("dotnet-trx-compatibility", true)]
    public void BrowserCompletionRequiresEveryBoundCurrentJourney(string scenario, bool expected)
    {
        using var fixture = new Fixture();
        var task = fixture.Task with { FrontendType = "backoffice", RequirementIds = ["REQ-001", "REQ-002"] };
        const string first = "TC-PORTAL-STATUS";
        const string second = "TC-PORTAL-DETAIL";
        Directory.CreateDirectory(Path.Combine(fixture.Root, "tests"));
        File.WriteAllText(Path.Combine(fixture.Root, "tests/portal.spec.ts"),
            $"test('{first}', () => {{}});\ntest('{second}', () => {{}});\n");
        File.WriteAllText(Path.Combine(fixture.Root, "tests/unrelated.spec.ts"), "test('TC-UNRELATED', () => {});\n");
        if (scenario == "explicit-task-binding")
            task = task with
            {
                VerificationObligations = [new("browser-journeys", [task.AcceptanceCriteria], "Verify both required browser journeys", "native",
                    [first + " and " + second]) { SharedGateIds = ["browser"] }]
            };
        if (scenario is not ("missing-catalogue" or "explicit-task-binding"))
        {
            var catalogue = $"### {first}: Current status\n- Requirement: `REQ-001`\n- Frontend type: `backoffice`\n\n";
            if (scenario != "missing-requirement")
                catalogue += $"### {(scenario == "duplicate-catalogue-id" ? first : second)}: Details\n- Requirement: `REQ-002`\n- Frontend type: `backoffice`\n";
            var path = Path.Combine(fixture.Root, "docs/changes/CIS-0001/test-cases.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, catalogue);
        }
        var cases = new List<TestCaseExecution>
        {
            new(first, "chromium / status / " + first, "passed", 1, "tests/portal.spec.ts", null)
        };
        if (scenario is not ("missing-journey" or "missing-cases"))
            cases.Add(new(scenario == "prefix-only" ? second + "-EXTRA" : second,
                "chromium / details / " + (scenario == "prefix-only" ? second + "-EXTRA" : second), "passed", 1, "tests/portal.spec.ts", null));
        if (scenario is "skipped-project" or "failed-project")
            cases.Add(new(second, "firefox / details / " + second, scenario == "skipped-project" ? "skipped" : "failed", 1, "tests/portal.spec.ts", null));
        if (scenario == "name-only-binding") cases = cases.Select((test, index) => test with { Id = "native-" + index }).ToList();
        if (scenario is "wrong-source" or "missing-source")
            cases = cases.Select(test => test with { Source = scenario == "missing-source" ? null : "tests/unrelated.spec.ts" }).ToList();
        if (scenario == "dotnet-trx-compatibility")
        {
            File.Delete(Path.Combine(fixture.Root, "tests/portal.spec.ts"));
            File.WriteAllText(Path.Combine(fixture.Root, "tests/PortalTests.cs"),
                $"class PortalTests {{ [Fact] public void BrowserJourneys() {{ /* {first} {second} */ }} }}");
            cases = cases.Select(test => test with { Source = null }).ToList();
        }
        if (scenario == "missing-cases") cases.Clear();
        fixture.WriteJson("native-browser.json", new { cases });
        var report = fixture.Artifact("native-browser.json");
        var artifacts = new[] { new { kind = "test-result", path = report.Path, digest = report.Sha256,
            bytes = new FileInfo(Path.Combine(fixture.Root, report.Path)).Length,
            suiteId = scenario == "foreign-suite-report" ? "another-suite" : "portal-browser" } };
        fixture.WriteJson("browser-manifest.json", new
        {
            schemaVersion = 1,
            inputDigest = scenario == "stale-inputs" ? "sha256:old" : "sha256:inputs",
            status = "passed",
            suites = new[] { new { suiteId = "portal-browser", layer = scenario == "unrelated-suite" ? "unit" : "browser",
                framework = scenario == "dotnet-trx-compatibility" ? "dotnet-test" : "playwright",
                status = "passed", total = Math.Max(1, cases.Count), passed = Math.Max(1, cases.Count), failed = 0, skipped = 0,
                cases = cases.Select(test => new { id = test.Id, name = test.Name, status = test.Status, source = test.Source }), artifacts } },
            artifacts
        });
        var manifest = fixture.Artifact("browser-manifest.json");
        if (scenario == "tampered-native") File.AppendAllText(Path.Combine(fixture.Root, report.Path), " ");
        var receipt = fixture.Receipt(EngineeringGateState.Passed) with
        {
            TaskDigest = EngineeringCompletionReview.TaskDigest(task),
            RequirementIds = task.RequirementIds,
            Gates = [new("browser", EngineeringGateState.Passed, "The required current portal journeys passed in native browser results.", [manifest])],
            VerificationObligations = task.VerificationObligations.Select(obligation => new EngineeringObligationEvidence(obligation.Id,
                obligation.Criteria, EngineeringGateState.Passed, "Both exact journey identities occur in current native evidence.", [manifest])).ToArray()
        };
        fixture.WriteJson(".cis/local/human-implementation.json", new
        {
            schemaVersion = 1,
            runId = receipt.ImplementerRunId,
            kind = "human-implemented",
            actor = "Synthetic protocol author",
            taskId = task.Id,
            taskContractDigest = receipt.TaskDigest,
            inputDigest = receipt.InputDigest,
            completedAtUtc = "2000-01-01T00:00:00Z"
        });
        receipt = receipt with { Implementation = fixture.Artifact(".cis/local/human-implementation.json") };
        var errors = fixture.Review(receipt, gateId: "browser", task: task);
        Assert.Equal(expected, errors.Count == 0);
        if (!expected) Assert.Contains(errors, error => error.Contains("browser", StringComparison.OrdinalIgnoreCase)
            || error.Contains("catalogue", StringComparison.OrdinalIgnoreCase));
    }
}
