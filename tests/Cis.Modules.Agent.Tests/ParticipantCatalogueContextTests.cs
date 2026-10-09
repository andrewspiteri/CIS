using System.Security.Cryptography;
using System.Text;
using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    private const string CatalogueCsvPath = "docs/cis/changes/CIS-0001/test-cases.csv";

    [Fact]
    public void ParticipantRun_ReceivesExactAuthorityCatalogueCsvWithoutCopyingItIntoParticipant()
    {
        using var authority = AgentRepository.Create();
        using var participant = AgentRepository.Create(git: true);
        ConfigureParticipantTask(authority, participant);
        const string csv = "\"ID\",\"Expected Result\"\r\n\"TC-FIXTURE-001\",\"Retains identity, UTC time\r\nand exact quantity.\"\r\n";
        authority.Write(CatalogueCsvPath, csv);
        var provider = new FakeProvider();
        var service = new AgentService(new CisRepositoryContextResolver(), [provider],
            new WorkspaceRegistry(new CisRepositoryContextResolver()), clock: Clock);

        var run = service.Run(authority.Path, "CIS-0001", "WORK-090", "fake", CisAgentRunModes.Implement,
            CisAgentPermissions.WorkspaceWrite, "participant", "fake-json", 60, false, "Agent", TestContext.Current.CancellationToken);

        Assert.True(run.ExitCode == 0, string.Join("\n", run.Diagnostics));
        var artifact = Assert.Single(run.Envelope!.AuthorityArtifacts!, item => item.Path == CatalogueCsvPath);
        Assert.Equal(csv, artifact.Content);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(csv))), artifact.Sha256);
        Assert.Contains("TC-FIXTURE-001", provider.LastRequest!.Prompt, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(run.Run!.Manifest.WorkingDirectory, CatalogueCsvPath)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParticipantRun_RejectsCatalogueCsvDriftDuringExecutionOrBeforeResume(bool duringRun)
    {
        using var authority = AgentRepository.Create();
        using var participant = AgentRepository.Create();
        ConfigureParticipantTask(authority, participant);
        authority.Write(CatalogueCsvPath, "ID,Expected Result\nTC-FIXTURE-001,Original result\n");
        void ChangeCsv() => authority.Write(CatalogueCsvPath, "ID,Expected Result\nTC-FIXTURE-001,Changed result\n");
        var provider = new FakeProvider(duringExecution: _ => { if (duringRun) ChangeCsv(); });
        var service = new AgentService(new CisRepositoryContextResolver(), [provider],
            new WorkspaceRegistry(new CisRepositoryContextResolver()), clock: Clock);

        var run = service.Run(authority.Path, "CIS-0001", "WORK-090", "fake", CisAgentRunModes.Review,
            CisAgentPermissions.ReadOnly, "participant", "fake-json", 60, false, "Agent", TestContext.Current.CancellationToken);

        Assert.Equal(duringRun ? CisAgentRunStates.InvalidEvidence : CisAgentRunStates.Succeeded, run.Run!.Manifest.Status);
        if (!duringRun) ChangeCsv();
        var resumed = service.Resume(authority.Path, run.Run.Manifest.RunId, "Continue", "Agent", "Check CSV freshness", false,
            TestContext.Current.CancellationToken);
        Assert.NotEqual(0, resumed.ExitCode);
        Assert.Equal(1, provider.ExecuteCalls);
        Assert.Contains(resumed.Diagnostics, message => message.Contains("authority context", StringComparison.OrdinalIgnoreCase));
    }
}
