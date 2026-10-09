using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData("impact.md", false)]
    [InlineData("test-cases.md", true)]
    public void ParticipantRunRejectsDossierChangesDuringProviderExecution(string name, bool addition)
    {
        using var authority = AgentRepository.Create();
        using var participant = AgentRepository.Create();
        ConfigureParticipantTask(authority, participant);
        // Impact is required for eligibility; an optional absent document exercises the separate dossier check.
        var relative = "docs/cis/changes/CIS-0001/" + name;
        var path = Path.Combine(authority.Path, relative);
        if (addition && File.Exists(path)) File.Delete(path);
        participant.Write(".cis/engineering-defaults.json", """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        var provider = new FakeProvider(duringExecution: _ => authority.Write(relative, "Changed operational context during review."));
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], new WorkspaceRegistry(new CisRepositoryContextResolver()),
            clock: Clock, taskContracts: new MutableTaskContract());
        var authorityContext = new CisRepositoryContextResolver().Resolve(authority.Path).Context!;
        var executionBefore = CisExecutionIdentity.Capture(authorityContext);
        var run = ParticipantReview(service, authority);
        Assert.True(run.Run is not null, string.Join("; ", run.Diagnostics));
        Assert.Equal(CisAgentRunStates.InvalidEvidence, run.Run!.Manifest.Status);
        var expected = addition ? "Authority execution inputs changed during the participant run" : "Bound authority context changed during execution";
        Assert.Contains(run.Diagnostics, message => message.Contains(expected, StringComparison.Ordinal));
        if (addition) Assert.Equal(executionBefore, CisExecutionIdentity.Capture(authorityContext));
    }

    [Theory]
    [InlineData("impact.md")]
    [InlineData("design.md")]
    public void ParticipantReviewRetainsDossierIdentityAndRejectsLaterAdditions(string name)
    {
        using var authority = AgentRepository.Create();
        using var participant = AgentRepository.Create();
        ConfigureParticipantTask(authority, participant);
        participant.Write(".cis/engineering-defaults.json", """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        var context = new CisRepositoryContextResolver().Resolve(authority.Path).Context!;
        var expected = CisTaskAuthorityContext.Capture(context, "CIS-0001");
        var provider = new FakeProvider();
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], new WorkspaceRegistry(new CisRepositoryContextResolver()),
            clock: Clock, taskContracts: new MutableTaskContract());
        var run = ParticipantReview(service, authority);
        Assert.Equal(CisAgentRunStates.Succeeded, run.Run!.Manifest.Status);
        Assert.Equal(expected, run.Run.Manifest.AuthorityDossierDigest);
        authority.Write("docs/cis/changes/CIS-0001/" + name, "Changed dossier context after review.");
        var resumed = service.Resume(authority.Path, run.Run.Manifest.RunId, "Continue", "Reviewer", "Check dossier freshness", false,
            TestContext.Current.CancellationToken);
        Assert.NotEqual(0, resumed.ExitCode);
        Assert.Equal(1, provider.ExecuteCalls);
        Assert.Contains(resumed.Diagnostics, message => message.Contains("Authority execution inputs changed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParticipantReviewBindsAuthorityInputsOutsideTheDocumentSnapshot(bool mutateDuringRun)
    {
        using var authority = AgentRepository.Create();
        using var participant = AgentRepository.Create();
        ConfigureParticipantTask(authority, participant);
        participant.Write(".cis/engineering-defaults.json", """{"schemaVersion":1,"requireTaskCompletion":true,"additionalGates":[]}""");
        var expected = CisExecutionIdentity.Capture(new CisRepositoryContextResolver().Resolve(authority.Path).Context!);
        var provider = new FakeProvider(duringExecution: _ => { if (mutateDuringRun) authority.Write("additional-policy.md", "Changed policy outside bound documents."); });
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], new WorkspaceRegistry(new CisRepositoryContextResolver()),
            clock: Clock, taskContracts: new MutableTaskContract());

        var run = ParticipantReview(service, authority);

        Assert.Equal(expected, run.Run!.Manifest.AuthorityInputDigest);
        Assert.Equal(mutateDuringRun ? CisAgentRunStates.InvalidEvidence : CisAgentRunStates.Succeeded, run.Run.Manifest.Status);
        if (!mutateDuringRun) authority.Write("additional-policy.md", "Changed after review.");
        var resumed = service.Resume(authority.Path, run.Run.Manifest.RunId, "Continue", "Reviewer", "Check source freshness", false,
            TestContext.Current.CancellationToken);
        Assert.Contains(resumed.Diagnostics, message => message.Contains("Authority execution inputs changed", StringComparison.Ordinal));
        Assert.Equal(1, provider.ExecuteCalls);
    }
}
