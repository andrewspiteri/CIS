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
        if (!missingProfile)
            participant.Write("docs/cis/references/test-suite-profile.md", Profile("unit", "junit", ".cis/local/results/unit.xml"));
        var run = new TestRunManifest(1, "RUN-1", "verify", "digest", manifestOwner, "revision", "profile",
            "test", "2026-08-26T00:00:00Z", "2026-08-26T00:01:00Z", outcome,
            [new("api-unit", "unit", "xunit", outcome, TestFailureKind.None, 1,
                outcome == "passed" ? 1 : 0, outcome == "failed" ? 1 : 0, outcome == "skipped" ? 1 : 0, 1,
                [new("TC-AUTH-001", "TC-AUTH-001 establishes identity", outcome, 1, "tests/IdentityTests.cs", null)], null, null, [], [])], []);
        participant.Write(".cis/local/testing/runs/RUN-1/manifest.json", JsonSerializer.Serialize(run, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var resolver = new CisRepositoryContextResolver();
        var registry = new TraceWorkspaceRegistry(new(authority.Path, "fixture",
            [new("fixture", authority.Path, "docs/cis", "authority"), new("application", participant.Path, "docs/cis")]));
        var service = new TestingService(resolver, new WorkflowService(resolver), [new JUnitTestResultAdapter()], registry);

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
