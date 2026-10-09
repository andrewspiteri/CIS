using System.Text.Json.Nodes;
using Cis.Abstractions;
using Cis.Modules.Change;
using Cis.Modules.Repository;
using Cis.Modules.Security;
using Cis.Modules.Verify;
using Cis.Modules.Workflow;

namespace Cis.Modules.Delivery.Tests;

public sealed partial class ExecutionModulesTests
{
    [Theory]
    [InlineData("missing", "CIS-VERIFY-SECURITY-EVIDENCE")]
    [InlineData("owner", "CIS-VERIFY-SECURITY-OWNER")]
    [InlineData("input", "CIS-VERIFY-SECURITY-STALE")]
    [InlineData("artifact", "CIS-VERIFY-SECURITY-INTEGRITY")]
    [InlineData("profile", "CIS-VERIFY-SECURITY-INTEGRITY")]
    [InlineData("authority-missing", "CIS-VERIFY-SECURITY-EVIDENCE")]
    [InlineData("valid", null)]
    public void VerifyChecksParticipantSecurityInItsOwningRepository(string scenario, string? expectedCode)
    {
        using var authority = ExecutionRepository.Create(id: "authority");
        using var participant = ExecutionRepository.Create(id: "participant");
        authority.Write(".cis/workspace.yml", $"""
            schema_version: 2
            ecosystem:
              id: fixture
              name: Fixture
            product:
              id: fixture
              name: Fixture
            repositories:
            - id: authority
              path: .
              documentation_root: docs/cis
              role: authority
              participation: owned
              relationship: none
              components: []
            - id: participant
              path: "{participant.Path.Replace('\\', '/')}"
              documentation_root: docs/cis
              role: participant
              participation: owned
              relationship: none
              components: []
            """);
        authority.Write("docs/cis/changes/CIS-0001/proposal.md",
            "---\nchange_id: CIS-0001\ntitle: Security routing\noutcome: Check producer evidence\nstatus: Active\nbaseline_kind: graph\nbaseline: sha256:original\ngraph_build_id: sha256:original\nimpact_roots: []\n---\n");
        participant.Write(".cis/engineering-defaults.json", """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        participant.Write("docs/cis/references/accepted-security-findings.md", """
            | Finding ID | Scanner | Fingerprint | Classification | Reason | Accepted until | Owner | Approved by | Approval reference |
            |---|---|---|---|---|---|---|---|---|
            """);
        participant.Write("docs/cis/references/security-suite-profile.md", """
            | Suite ID | Component | Category | Tool | Command | Working directory | Result format | Result path | Target | Applies when | CI tier | Fail severities | Artifacts |
            |---|---|---|---|---|---|---|---|---|---|---|---|---|
            | fixture-scan | app | sast | semgrep | scan | . | semgrep-json | .cis/local/security/results/semgrep.json | . | always | pr | critical,high | retain |
            """);
        participant.Write("docs/cis/workflows/fixture-security.md", """
            | Step | Command | Working directory | Test suites | Depends on | Continue on failure | Timeout seconds |
            |---|---|---|---|---|---|---:|
            | scan | dotnet --version | . | fixture-scan | - | no | 30 |
            """);
        var resolver = new CisRepositoryContextResolver();
        var workflows = new WorkflowService(resolver);
        var security = new SecurityService(resolver, workflows, [new SemgrepJsonSecurityResultAdapter()]);
        if (scenario != "missing")
        {
            // Native adapter protocol fixture, not a claim of executing a real scanner.
            var report = Path.Combine(participant.Path, ".cis/local/security/results/semgrep.json");
            participant.Write(".cis/local/security/results/semgrep.json", """{"results":[]}""");
            var run = workflows.Run(participant.Path, "fixture-security", "VERIFY-SECURITY");
            Assert.Equal("succeeded", run.Run!.Status);
            File.SetLastWriteTimeUtc(report, DateTimeOffset.Parse(Assert.Single(run.Run.Steps).CompletedAtUtc!).UtcDateTime);
            var reconciled = security.Reconcile(participant.Path, run.RunId!);
            Assert.True(reconciled.Manifest?.Status == "passed", string.Join("; ", reconciled.Diagnostics));
            if (scenario == "owner")
            {
                var path = Path.Combine(participant.Path, ".cis/local/security/runs/VERIFY-SECURITY/manifest.json");
                var manifest = JsonNode.Parse(File.ReadAllText(path))!;
                manifest["repositoryId"] = "some-other-repository";
                File.WriteAllText(path, manifest.ToJsonString());
            }
            if (scenario == "input") participant.Write("src/new-input.txt", "changed since execution");
            if (scenario == "artifact") File.AppendAllText(report, " ");
            if (scenario == "profile") File.AppendAllText(Path.Combine(participant.Path, "docs/cis/references/security-suite-profile.md"), "\nChanged scanner policy.\n");
            if (scenario == "authority-missing") authority.Write("docs/cis/references/security-suite-profile.md",
                File.ReadAllText(Path.Combine(participant.Path, "docs/cis/references/security-suite-profile.md")));
        }
        var registry = new WorkspaceRegistry(resolver);
        var service = new VerifyService(resolver, new ChangeDossierStore(resolver), [],
            workspaceRegistry: registry, security: security);
        var result = service.Validate(authority.Path, "CIS-0001");
        var findings = result.Findings.Where(x => x.Code.StartsWith("CIS-VERIFY-SECURITY", StringComparison.Ordinal)).ToArray();
        if (expectedCode is null) Assert.Empty(findings);
        else Assert.Contains(findings, x => x.Code == expectedCode && x.Path!.StartsWith(
            scenario == "authority-missing" ? "authority::" : "participant::", StringComparison.Ordinal));
    }
}
