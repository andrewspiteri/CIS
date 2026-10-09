using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Fact]
    public void ParticipantRun_ReceivesBoundAuthorityDocumentsWithoutOverwritingParticipantFiles()
    {
        using var authority = AgentRepository.Create();
        using var participant = AgentRepository.Create(git: true);
        ConfigureParticipantTask(authority, participant);
        participant.Write("docs/cis/specs/business-requirements.md", "Participant source evidence, not product authority.");
        var provider = new FakeProvider();
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], new WorkspaceRegistry(new CisRepositoryContextResolver()), clock: Clock);

        var run = service.Run(authority.Path, "CIS-0001", "WORK-090", "fake", CisAgentRunModes.Implement,
            CisAgentPermissions.WorkspaceWrite, "participant", "fake-json", 60, false, "Agent", TestContext.Current.CancellationToken);

        Assert.True(run.ExitCode == 0, string.Join("\n", run.Diagnostics));
        Assert.True(run.Run!.Manifest.IsolatedWorktree);
        Assert.NotEmpty(run.Envelope!.AuthorityArtifacts!);
        Assert.Contains(run.Envelope.AuthorityArtifacts!, item => item.Path.EndsWith("feature-specification.md", StringComparison.Ordinal)
            && item.Content.Contains("Preserve the reading identity and UTC event time.", StringComparison.Ordinal));
        Assert.Contains("Approved product requirements.", provider.LastRequest!.Prompt, StringComparison.Ordinal);
        Assert.Contains("Approved technical direction.", provider.LastRequest.Prompt, StringComparison.Ordinal);
        Assert.Contains("authority-relative identities", provider.LastRequest.Prompt, StringComparison.Ordinal);
        Assert.Equal("Participant source evidence, not product authority.", File.ReadAllText(Path.Combine(run.Run.Manifest.WorkingDirectory, "docs/cis/specs/business-requirements.md")));
        Assert.False(File.Exists(Path.Combine(run.Run.Manifest.WorkingDirectory, "docs/cis/specs/features/readings/feature-specification.md")));
        Assert.Empty(run.Run.Result!.ChangedFiles);
    }

    [Fact]
    public void ParticipantRun_RejectsChangedAuthorityEvidenceAndStaleResume()
    {
        using var authority = AgentRepository.Create();
        using var participant = AgentRepository.Create();
        ConfigureParticipantTask(authority, participant);
        var provider = new FakeProvider(duringExecution: _ => authority.Write("docs/cis/specs/business-requirements.md", "Changed approved scope."));
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], new WorkspaceRegistry(new CisRepositoryContextResolver()), clock: Clock);

        var run = service.Run(authority.Path, "CIS-0001", "WORK-090", "fake", CisAgentRunModes.Review,
            CisAgentPermissions.ReadOnly, "participant", "fake-json", 60, false, "Agent", TestContext.Current.CancellationToken);

        Assert.Equal(CisAgentRunStates.InvalidEvidence, run.Run!.Manifest.Status);
        Assert.Contains(run.Diagnostics, message => message.Contains("authority context changed", StringComparison.Ordinal));
        var resumed = service.Resume(authority.Path, run.Run.Manifest.RunId, "Continue", "Agent", "Retry", false, TestContext.Current.CancellationToken);
        Assert.NotEqual(0, resumed.ExitCode);
        Assert.Equal(1, provider.ExecuteCalls);
        Assert.Contains(resumed.Diagnostics, message => message.Contains("authority context", StringComparison.Ordinal));
    }

    [Fact]
    public void ParticipantRun_BlocksOversizedContextBeforeProviderExecution()
    {
        using var authority = AgentRepository.Create();
        using var participant = AgentRepository.Create();
        ConfigureParticipantTask(authority, participant);
        authority.Write("docs/cis/specs/business-requirements.md", new string('x', 256 * 1024 + 1));
        var provider = new FakeProvider();
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], new WorkspaceRegistry(new CisRepositoryContextResolver()), clock: Clock);

        var run = service.Run(authority.Path, "CIS-0001", "WORK-090", "fake", CisAgentRunModes.Review,
            CisAgentPermissions.ReadOnly, "participant", "fake-json", 60, false, "Agent", TestContext.Current.CancellationToken);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Equal(0, provider.ExecuteCalls);
        Assert.Contains(run.Diagnostics, message => message.Contains("bounded size", StringComparison.Ordinal));
    }

    private static void ConfigureParticipantTask(AgentRepository authority, AgentRepository participant)
    {
        authority.AddEligibleChange();
        participant.Write(".cis/repository.yml", "schema_version: 1\nrepository:\n  id: participant\ndocumentation_root: docs/cis\n");
        authority.Write(".cis/workspace.yml", $$"""
            schema_version: 2
            ecosystem:
              id: fixture
              name: Fixture
            product:
              id: fixture
              name: Fixture
            repositories:
            - id: agent-fixture
              path: .
              documentation_root: docs/cis
              role: authority
              participation: owned
              relationship: none
              components: []
            - id: participant
              path: {{participant.Path.Replace('\\', '/')}}
              documentation_root: docs/cis
              role: participant
              participation: owned
              relationship: none
              components: []
            """);
        const string task = "docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md";
        authority.Write(task, authority.Read(task).Replace("targets: [\"agent-fixture\"]", "targets: [\"participant\"]", StringComparison.Ordinal));
        const string plan = "docs/cis/changes/CIS-0001/plan.md";
        authority.Write(plan, authority.Read(plan).Replace("status: Approved", "status: Approved\nfeature_spec_path: docs/cis/specs/features/readings/feature-specification.md", StringComparison.Ordinal));
        authority.Write("docs/cis/specs/features/readings/feature-specification.md", "Preserve the reading identity and UTC event time.");
        var featureHash = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            File.ReadAllBytes(Path.Combine(authority.Path, "docs/cis/specs/features/readings/feature-specification.md"))));
        authority.Write(plan, authority.Read(plan).Replace("status: Approved", "status: Approved\nfeature_spec_sha256: " + featureHash, StringComparison.Ordinal));
        authority.Write(task, authority.Read(task).Replace("task_status: Ready", "task_status: Ready\nfeature_spec_sha256: " + featureHash, StringComparison.Ordinal));
        authority.Write("docs/cis/specs/business-requirements.md", "Approved product requirements.");
        authority.Write("docs/cis/specs/technical-intent-spec.md", "Approved technical direction.");
    }
}
