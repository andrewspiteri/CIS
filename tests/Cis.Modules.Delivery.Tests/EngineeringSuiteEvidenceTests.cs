using System.Text.Json.Nodes;
using Cis.Abstractions;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class EngineeringCompletionTests
{
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
