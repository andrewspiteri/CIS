using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Repository;
using Cis.Modules.Testing;
using Cis.Modules.Workflow;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed partial class TestingServiceTests
{
    [Theory]
    [InlineData(false, "passed", 0)]
    [InlineData(true, "passed", 4)]
    [InlineData(false, "failed", 4)]
    [InlineData(false, "skipped", 4)]
    [InlineData(false, "passed", 4, "fixture")]
    [InlineData(false, "passed", 4, "unregistered")]
    public void WorkspaceTrace_UsesTheExecutingParticipantProfile(bool missingProfile, string outcome, int expectedExit,
        string manifestOwner = "application")
    {
        using var authority = TestRepository.Create();
        using var participant = TestRepository.Create();
        participant.Write(".cis/repository.yml", "schema_version: 1\nrepository:\n  id: application\ndocumentation_root: docs/cis\n");
        authority.Write("docs/cis/changes/CIS-0001/test-cases.md", "### TC-AUTH-001: Establish identity\n");
        // The authority intentionally has no native profile. Only the participant executes tests.
        participant.Write("docs/cis/references/test-suite-profile.md", Profile("unit", "junit", ".cis/local/results/unit.xml"));
        participant.Write("docs/cis/workflows/verify.md", "| Step | Command | Depends on | Continue on failure | Timeout seconds | Test suites |\n| unit | dotnet --version | - | no | 30 | api-unit |\n");
        var definition = new WorkflowService(new CisRepositoryContextResolver()).Describe(participant.Path, "verify").Workflow!;
        var began = DateTimeOffset.UtcNow.AddMinutes(-1);
        participant.Write(".cis/local/workflows/RUN-1/state.json", JsonSerializer.Serialize(new WorkflowRunState(2,
            "RUN-1", "verify", definition.Digest, "succeeded", began.ToString("O"), began.AddSeconds(30).ToString("O"),
            [new("unit", "succeeded", 0, began.ToString("O"), began.AddSeconds(30).ToString("O"), 1, "unit.log", null)]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var caseOutcome = outcome switch { "failed" => "<failure message=\"Expected identity\"/>", "skipped" => "<skipped/>", _ => "" };
        participant.Write(".cis/local/results/unit.xml", $"<testsuite><testcase name=\"TC-AUTH-001 establishes identity\">{caseOutcome}</testcase></testsuite>");
        var resolver = new CisRepositoryContextResolver();
        var registry = new TraceWorkspaceRegistry(new(authority.Path, "fixture",
            [new("fixture", authority.Path, "docs/cis", "authority"), new("application", participant.Path, "docs/cis")]));
        var service = new TestingService(resolver, new WorkflowService(resolver), [new JUnitTestResultAdapter()], registry);
        var run = service.Reconcile(participant.Path, "RUN-1").Manifest! with { RepositoryId = manifestOwner };
        participant.Write(".cis/local/testing/runs/RUN-1/manifest.json", JsonSerializer.Serialize(run, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        if (missingProfile) File.Delete(Path.Combine(participant.Path, "docs/cis/references/test-suite-profile.md"));

        var result = service.Trace(authority.Path, "CIS-0001", "RUN-1");

        Assert.Equal(expectedExit, result.ExitCode);
        if (expectedExit == 0)
        {
            Assert.Equal("traced", result.Status);
            Assert.Equal("passed", Assert.Single(result.Trace).Status);
            Assert.Equal("api-unit", Assert.Single(result.Suites).Id);
            Assert.Empty(result.Diagnostics);
        }
        else if (manifestOwner != "application")
            Assert.Contains(result.Diagnostics, item => item.Contains("does not match its registered owner", StringComparison.Ordinal));
        else if (missingProfile)
            Assert.Contains(result.Diagnostics, item => item.Contains("profile was not found", StringComparison.Ordinal));
        else
            Assert.Equal("missing", Assert.Single(result.Trace).Status);
    }

    private sealed class TraceWorkspaceRegistry(CisWorkspace workspace) : ICisWorkspaceRegistry
    {
        public CisWorkspaceResolution Resolve(string workspacePath) => new(workspace, []);
    }
}
