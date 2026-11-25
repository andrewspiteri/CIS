using System.Text.Json.Nodes;
using Cis.Abstractions;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class EngineeringCompletionTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("schema-one", false)]
    [InlineData("schema-three", false)]
    [InlineData("plain-passed", false)]
    [InlineData("missing-scope", false)]
    [InlineData("unknown-policy", false)]
    [InlineData("empty-selection", false)]
    [InlineData("duplicate-selection", false)]
    [InlineData("selection-mismatch", false)]
    [InlineData("overlap", false)]
    [InlineData("wrong-workflow", false)]
    [InlineData("wrong-id", false)]
    [InlineData("wrong-digest", false)]
    [InlineData("changed-profile", false)]
    [InlineData("zero-tests", false)]
    [InlineData("skipped", false)]
    [InlineData("failed", false)]
    [InlineData("changed-report", false)]
    [InlineData("null-suites", false)]
    [InlineData("null-selection", false)]
    [InlineData("wrong-report-run", false)]
    [InlineData("wrong-report-suite", false)]
    [InlineData("wrong-own-run", false)]
    [InlineData("cross-suite-report", false)]
    public void SelectedSuiteNativeEvidenceRequiresExplicitCurrentBoundScope(string scenario, bool accepted)
    {
        using var fixture = new Fixture();
        var manifest = SelectedNativeManifest(fixture);
        if (scenario == "cross-suite-report")
        {
            manifest = manifest with { Suites = [manifest.Suites[0], manifest.Suites[0] with { SuiteId = "unrelated" }] };
            manifest = manifest with { Scope = TestReconcileScope.Create(manifest, ["focused", "unrelated"], []) };
        }
        var node = System.Text.Json.JsonSerializer.SerializeToNode(manifest,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        switch (scenario)
        {
            case "schema-one": node["schemaVersion"] = 1; break;
            case "schema-three": node["schemaVersion"] = 3; break;
            case "plain-passed": node["status"] = "passed"; break;
            case "missing-scope": node.AsObject().Remove("scope"); break;
            case "unknown-policy": node["scope"]!["policy"] = "unknown"; break;
            case "empty-selection": node["scope"]!["selectedSuiteIds"] = new JsonArray(); break;
            case "duplicate-selection": node["scope"]!["selectedSuiteIds"] = new JsonArray("focused", "FOCUSED"); break;
            case "selection-mismatch": node["scope"]!["selectedSuiteIds"] = new JsonArray("another"); break;
            case "overlap": node["scope"]!["omittedSuiteIds"] = new JsonArray("focused"); break;
            case "wrong-workflow": node["scope"]!["workflowRunId"] = "another"; break;
            case "wrong-id": node["scope"]!["id"] = "another"; break;
            case "wrong-digest": node["scope"]!["digest"] = new string('0', 64); break;
            case "changed-profile": fixture.WriteJson("docs/references/test-suite-profile.md", new { fixture = "changed profile" }); break;
            case "zero-tests": node["suites"]![0]!["total"] = 0; node["suites"]![0]!["passed"] = 0; break;
            case "skipped": node["suites"]![0]!["skipped"] = 1; node["suites"]![0]!["passed"] = 0; break;
            case "failed": node["suites"]![0]!["status"] = "failed"; node["suites"]![0]!["failed"] = 1; break;
            case "changed-report": fixture.WriteJson("native.trx", new { fixture = "changed native bytes" }); break;
            case "null-suites": node["suites"] = null; break;
            case "null-selection": node["scope"]!["selectedSuiteIds"] = null; break;
            case "wrong-report-run":
                node["artifacts"]![0]!["runId"] = "different-run";
                node["suites"]![0]!["artifacts"]![0]!["runId"] = "different-run";
                break;
            case "wrong-report-suite":
                node["artifacts"]![0]!["suiteId"] = "different-suite";
                node["suites"]![0]!["artifacts"]![0]!["suiteId"] = "different-suite";
                break;
            case "wrong-own-run": node["suites"]![0]!["artifacts"]![0]!["runId"] = "different-run"; break;
            case "cross-suite-report":
                node["artifacts"]![0]!["suiteId"] = "unrelated";
                node["suites"]![0]!["artifacts"]![0]!["suiteId"] = "unrelated";
                node["suites"]![1]!["artifacts"]![0]!["suiteId"] = "unrelated";
                break;
        }
        fixture.WriteJson("selected-native.json", node);
        var receipt = fixture.Receipt(EngineeringGateState.Passed) with
        {
            Gates = [new("unit", EngineeringGateState.Passed, "Synthetic selected native protocol; omitted suites are unassessed.",
                [fixture.Artifact("selected-native.json")])]
        };
        Assert.Equal(accepted, fixture.Review(receipt).Count == 0);
    }

    [Theory]
    [InlineData(94, false)]
    [InlineData(95, true)]
    public void SelectedSuiteCoverageRetainsTheChangedProductionThreshold(double lines, bool accepted)
    {
        using var fixture = new Fixture();
        var manifest = SelectedNativeManifest(fixture);
        fixture.WriteJson("coverage.json", new { fixture = "Synthetic native coverage binding protocol" });
        var source = fixture.Artifact("coverage.json");
        var coverageArtifact = new TestArtifact("coverage-result", source.Path, source.Sha256.Replace("sha256:", "", StringComparison.Ordinal),
            new FileInfo(Path.Combine(fixture.Root, source.Path)).Length, manifest.RunId, SuiteId: "focused");
        manifest = manifest with
        {
            Suites = [manifest.Suites[0] with { Coverage = new(lines, lines, lines, lines, "changed-production", source.Path, 100, (int)lines),
                Artifacts = manifest.Suites[0].Artifacts.Concat([coverageArtifact]).ToArray() }],
            Artifacts = manifest.Artifacts.Concat([coverageArtifact]).ToArray()
        };
        fixture.WriteJson("selected-native.json", System.Text.Json.JsonSerializer.SerializeToNode(manifest,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!);
        var receipt = fixture.Receipt(EngineeringGateState.Passed) with
        {
            Gates = [new("coverage", EngineeringGateState.Passed, "Selected scope does not lower the existing changed-line threshold.",
                [fixture.Artifact("selected-native.json")])]
        };
        Assert.Equal(accepted, fixture.Review(receipt, gateId: "coverage").Count == 0);
    }

    private static TestRunManifest SelectedNativeManifest(Fixture fixture)
    {
        fixture.WriteJson("docs/references/test-suite-profile.md", new { fixture = "Synthetic canonical profile binding" });
        var profile = fixture.Artifact("docs/references/test-suite-profile.md");
        var report = fixture.Artifact("native.trx");
        var artifact = new TestArtifact("test-result", report.Path, report.Sha256.Replace("sha256:", "", StringComparison.Ordinal),
            new FileInfo(Path.Combine(fixture.Root, report.Path)).Length, "RUN-SCOPE", SuiteId: "focused");
        var manifest = new TestRunManifest(2, "RUN-SCOPE", "verify", "workflow-digest", "fixture", "revision",
            profile.Sha256.Replace("sha256:", "", StringComparison.Ordinal), "fixture", "2000-01-01T00:00:00Z", "2000-01-01T00:01:00Z",
            "scope-passed", [new("focused", "unit", "fixture", "passed", TestFailureKind.None, 1, 1, 0, 0, 1,
                [new("TC-SCOPE-001", "Fixture native result", "passed", 1, null, null)], null, null, [artifact], [])], [artifact], InputDigest: "sha256:inputs");
        return manifest with { Scope = TestReconcileScope.Create(manifest, ["focused"], ["unrelated"]) };
    }

    [Theory]
    [InlineData("accepted", true)]
    [InlineData("below-threshold", true)]
    [InlineData("expired", false)]
    [InlineData("unaccepted", false)]
    [InlineData("missing-policy", false)]
    [InlineData("wrong-acceptance", false)]
    [InlineData("wrong-suite-artifact", false)]
    [InlineData("empty-thresholds", false)]
    [InlineData("changed-profile", false)]
    [InlineData("non-gregorian-culture", true)]
    [InlineData("missing-profile", false)]
    [InlineData("redirected-profile", false)]
    [InlineData("unknown-severity", false)]
    [InlineData("empty-severity", false)]
    public void ScannerEvidencePreservesPolicyAndValidatesEachSuite(string scenario, bool expected)
    {
        using var fixture = new Fixture();
        using var culture = new ScopedCulture(scenario == "non-gregorian-culture" ? "th-TH" : "en-US");
        fixture.WriteJson("docs/references/security-suite-profile.md", new { scope = "Synthetic scanner scope" });
        var profile = fixture.Artifact("docs/references/security-suite-profile.md");
        fixture.WriteJson("scanner.json", new[] { new { rule = "synthetic-rule" } });
        var report = fixture.Artifact("scanner.json");
        var artifact = new
        {
            kind = "scanner-result",
            path = report.Path,
            digest = report.Sha256,
            bytes = new FileInfo(Path.Combine(fixture.Root, report.Path)).Length
        };
        var acceptance = new
        {
            id = "SEC-1",
            scanner = "fixture",
            fingerprint = scenario == "wrong-acceptance" ? "different" : "exact",
            reason = "Synthetic protocol test, not a real security exception",
            owner = "fixture",
            approvedBy = "fixture",
            approvalReference = "synthetic-only",
            acceptedUntil = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(scenario == "expired" ? -1 : 1)
                .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        };
        fixture.WriteJson("security.json", new
        {
            schemaVersion = 1,
            inputDigest = "sha256:inputs",
            status = "passed-with-findings",
            profilePath = profile.Path,
            profileDigest = profile.Sha256,
            suites = new[] { new
            {
                status = "passed-with-findings", tool = "fixture", category = "sast", blockingSeverities = new[] { "high", "critical" },
                findings = new[] { new { severity = scenario == "below-threshold" ? "low" : "high", scanner = "fixture", fingerprint = "exact",
                    status = scenario is "unaccepted" or "below-threshold" ? "new" : "accepted", acceptanceId = "SEC-1" } },
                artifacts = new[] { artifact }
            } },
            artifacts = new[] { artifact },
            acceptances = new[] { acceptance }
        });
        var manifestPath = Path.Combine(fixture.Root, "security.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        if (scenario == "missing-policy") manifest["suites"]![0]!.AsObject().Remove("blockingSeverities");
        if (scenario == "empty-thresholds") manifest["suites"]![0]!["blockingSeverities"] = new JsonArray();
        if (scenario == "changed-profile") fixture.WriteJson(profile.Path, new { scope = "Changed scanner scope" });
        if (scenario == "missing-profile") manifest.AsObject().Remove("profilePath");
        if (scenario == "redirected-profile")
        {
            File.Copy(Path.Combine(fixture.Root, profile.Path), Path.Combine(fixture.Root, "other-profile.md"));
            manifest["profilePath"] = "other-profile.md";
        }
        if (scenario is "unknown-severity" or "empty-severity")
        {
            manifest["suites"]![0]!["findings"]![0]!["status"] = "new";
            manifest["suites"]![0]!["findings"]![0]!["severity"] = scenario == "unknown-severity" ? "unclassified" : "";
        }
        if (scenario == "wrong-suite-artifact") manifest["suites"]![0]!["artifacts"]![0]!["path"] = "unverified.json";
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        var receipt = fixture.Receipt(EngineeringGateState.Passed) with
        {
            Gates = [new("security", EngineeringGateState.Passed, "Synthetic native scanner policy protocol.", [fixture.Artifact("security.json")])]
        };
        Assert.Equal(expected, fixture.Review(receipt, gateId: "security").Count == 0);
    }

    [Theory]
    [InlineData("coverage", "unit", false, true)]
    [InlineData("coverage", "mutation", false, false)]
    [InlineData("coverage", "unit", true, false)]
    [InlineData("mutation", "mutation", false, true)]
    [InlineData("mutation", "unit", false, false)]
    [InlineData("mutation", "mutation", true, false)]
    [InlineData("coverage", "unit", false, false, 96, 99.0)]
    [InlineData("coverage", "unit", false, true, 99, 99.0)]
    [InlineData("mutation", "mutation", false, false, 85, 90.0)]
    [InlineData("mutation", "mutation", false, true, 90, 90.0)]
    [InlineData("coverage", "unit", false, true, 0, null, 0)]
    [InlineData("coverage", "unit", false, true, 0, null, 1)]
    [InlineData("coverage", "unit", false, false, 0, null, 1, 1)]
    [InlineData("coverage", "unit", true, false, 0, null, 0)]
    [InlineData("coverage", "unit", false, false, 0, null, 0, 0, "missing-uninstrumented")]
    [InlineData("coverage", "unit", false, false, 0, null, 0, 0, "missing-changed")]
    [InlineData("coverage", "unit", false, false, 0, null, 0, 0, "null-uninstrumented")]
    public void MeasurementMustBelongToPassingSuite(string gate, string layer, bool wrongSource, bool expected, double measured = 100, double? threshold = null, int? changedLines = null, int uninstrumented = 0, string? omitted = null)
    {
        using var fixture = new Fixture();
        fixture.WriteJson("measurement.json", new { description = "Synthetic measurement binding protocol" });
        var report = fixture.Artifact("measurement.json");
        var path = Path.Combine(fixture.Root, "result.json");
        var manifest = JsonNode.Parse(File.ReadAllText(path))!;
        var suite = manifest["suites"]![0]!;
        if (threshold is not null)
            fixture.WriteJson("policy.json", new { schemaVersion = 1, minimumCoverageLines = gate == "coverage" ? threshold.Value : 95, minimumMutationScore = gate == "mutation" ? threshold.Value : 80 });
        suite["layer"] = layer;
        suite[gate] = gate == "coverage"
            ? JsonNode.Parse("""{"lines":100,"measuredLines":1,"scope":"changed-production"}""")
            : JsonNode.Parse("""{"score":100,"killed":1}""");
        suite[gate]!["sourcePath"] = wrongSource ? "another-suite.json" : report.Path;
        suite[gate]![gate == "coverage" ? "lines" : "score"] = measured;
        if (changedLines is not null)
        {
            suite[gate]!["measuredLines"] = 0;
            suite[gate]!["changedProductionLines"] = changedLines.Value;
            suite[gate]!["uninstrumentedChangedFiles"] = uninstrumented;
            suite[gate]!["baseRevision"] = new string('a', 40);
            if (omitted == "missing-uninstrumented") suite[gate]!.AsObject().Remove("uninstrumentedChangedFiles");
            if (omitted == "missing-changed") suite[gate]!.AsObject().Remove("changedProductionLines");
            if (omitted == "null-uninstrumented") suite[gate]!["uninstrumentedChangedFiles"] = null;
        }
        var artifact = new JsonObject
        {
            ["kind"] = gate == "coverage" ? "coverage-result" : "mutation-result",
            ["path"] = report.Path,
            ["digest"] = report.Sha256,
            ["bytes"] = new FileInfo(Path.Combine(fixture.Root, report.Path)).Length
        };
        manifest["artifacts"]!.AsArray().Add(artifact);
        suite["artifacts"]!.AsArray().Add(artifact.DeepClone());
        File.WriteAllText(path, manifest.ToJsonString());
        var receipt = fixture.Receipt(EngineeringGateState.Passed) with
        {
            Gates = [new(gate, changedLines is null ? EngineeringGateState.Passed : EngineeringGateState.Inapplicable, "Synthetic measurement evidence protocol.", [fixture.Artifact("result.json")])]
        };
        Assert.Equal(expected, fixture.Review(receipt, gateId: gate).Count == 0);
    }
    private sealed class ScopedCulture : IDisposable
    {
        private readonly System.Globalization.CultureInfo _previous = System.Globalization.CultureInfo.CurrentCulture;
        public ScopedCulture(string name) => System.Globalization.CultureInfo.CurrentCulture = new(name);
        public void Dispose() => System.Globalization.CultureInfo.CurrentCulture = _previous;
    }
}
