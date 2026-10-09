using Cis.Abstractions;
using Cis.Modules.Repository;
using Cis.Modules.Security;
using Cis.Modules.Workflow;
using System.Text.Json.Nodes;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class EngineeringCompletionTests
{
    [Theory]
    [InlineData("clean", "docs")]
    [InlineData("below-threshold", "docs")]
    [InlineData("accepted", "docs")]
    [InlineData("clean", "docs/cis")]
    [InlineData("below-threshold", "docs/cis")]
    [InlineData("accepted", "docs/cis")]
    public void CompletionConsumesManifestActuallyWrittenBySecurityReconciliation(string scenario, string documentationRoot)
    {
        using var fixture = new Fixture();
        Assert.Equal(0, new RepositoryInitializer().Initialize(new(fixture.Root, documentationRoot, false, true, AdoptEngineeringDefaults: true)).ExitCode);
        const string report = ".cis/local/security/results/semgrep.json";
        File.WriteAllText(Path.Combine(fixture.Root, documentationRoot + "/references/security-suite-profile.md"), """
            | Suite ID | Component | Category | Tool | Command | Working directory | Result format | Result path | Target | Applies when | CI tier | Fail severities | Artifacts |
            |---|---|---|---|---|---|---|---|---|---|---|---|---|
            | fixture-scan | app | sast | semgrep | scan | . | semgrep-json | .cis/local/security/results/semgrep.json | . | always | pr | critical,high | retain |
            """);
        File.WriteAllText(Path.Combine(fixture.Root, documentationRoot + "/workflows/fixture-security.md"), """
            | Step | Command | Working directory | Test suites | Depends on | Continue on failure | Timeout seconds |
            |---|---|---|---|---|---|---:|
            | scan | dotnet --version | . | fixture-scan | - | no | 30 |
            """);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(fixture.Root, report))!);
        File.WriteAllText(Path.Combine(fixture.Root, report), scenario == "clean" ? """{"results":[]}"""
            : """{"results":[{"check_id":"fixture.rule","path":"src/app.cs","start":{"line":1},"extra":{"severity":"SEVERITY","message":"Synthetic protocol finding"}}]}"""
                .Replace("SEVERITY", scenario == "accepted" ? "ERROR" : "WARNING", StringComparison.Ordinal));
        if (scenario == "accepted")
        {
            var suite = new SecuritySuiteProfile("fixture-scan", "app", "sast", "semgrep", "scan", ".", "semgrep-json", report, ".", "always", "pr", "critical,high", "retain");
            var finding = Assert.Single(new SemgrepJsonSecurityResultAdapter().Read(new(fixture.Root, suite, Path.Combine(fixture.Root, report))).Findings);
            var until = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            File.WriteAllText(Path.Combine(fixture.Root, documentationRoot + "/references/accepted-security-findings.md"), $"""
                | Finding ID | Scanner | Fingerprint | Classification | Reason | Accepted until | Owner | Approved by | Approval reference |
                |---|---|---|---|---|---|---|---|---|
                | SEC-PROTOCOL | semgrep | {finding.Fingerprint} | false-positive | Synthetic protocol only | {until} | fixture | fixture | synthetic-only |
                """);
        }
        var resolver = new CisRepositoryContextResolver();
        var workflows = new WorkflowService(resolver);
        var run = workflows.Run(fixture.Root, "fixture-security", "SCANNER-PROTOCOL");
        Assert.Equal("succeeded", run.Run!.Status);
        // Protocol test supplies native report bytes and their arrival time; actual scanner execution is qualified separately.
        var step = Assert.Single(run.Run.Steps);
        File.SetLastWriteTimeUtc(Path.Combine(fixture.Root, report), DateTimeOffset.Parse(step.CompletedAtUtc!).UtcDateTime);
        var reconciled = new SecurityService(resolver, workflows, [new SemgrepJsonSecurityResultAdapter()]).Reconcile(fixture.Root, run.RunId!);
        Assert.Equal(scenario == "clean" ? "passed" : "passed-with-findings", reconciled.Manifest!.Status);
        Assert.Contains(reconciled.Manifest.Artifacts, artifact => artifact.Kind == "scanner-result");
        // Consume the original service-written manifest without rewriting any evidence.
        var manifestPath = ".cis/local/security/runs/SCANNER-PROTOCOL/manifest.json";
        var inputs = reconciled.Manifest.InputDigest!;
        Assert.NotNull(inputs);
        var receipt = fixture.Receipt(EngineeringGateState.Passed, inputs) with
        {
            Gates = [new("security", EngineeringGateState.Passed, "Actual SecurityService native manifest contract.", [fixture.Artifact(manifestPath)])]
        };
        Assert.Empty(fixture.Review(receipt, gateId: "security", inputDigest: inputs, documentationRoot: documentationRoot));
        if (scenario == "accepted")
        {
            var original = File.ReadAllText(Path.Combine(fixture.Root, manifestPath));
            foreach (var missing in new[] { false, true })
            {
                var changed = JsonNode.Parse(original)!;
                if (missing) changed["acceptances"] = new JsonArray();
                else changed["acceptances"]![0]!["acceptedUntil"] = "2000-01-01";
                File.WriteAllText(Path.Combine(fixture.Root, manifestPath), changed.ToJsonString());
                var changedReceipt = receipt with
                {
                    Gates = [receipt.Gates[0] with { Artifacts = [fixture.Artifact(manifestPath)] }]
                };
                Assert.NotEmpty(fixture.Review(changedReceipt, gateId: "security", inputDigest: inputs, documentationRoot: documentationRoot));
            }
            File.WriteAllText(Path.Combine(fixture.Root, manifestPath), original);
            Assert.Empty(fixture.Review(receipt, gateId: "security", inputDigest: inputs, documentationRoot: documentationRoot));
        }
        File.AppendAllText(Path.Combine(fixture.Root, report), " ");
        Assert.NotEmpty(fixture.Review(receipt, gateId: "security", inputDigest: inputs, documentationRoot: documentationRoot));
    }
}
