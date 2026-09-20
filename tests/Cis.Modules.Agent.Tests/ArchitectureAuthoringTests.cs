using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Agent;
using Cis.Modules.Repository;
using Xunit;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("coverage")]
    [InlineData("protected")]
    [InlineData("snapshot")]
    [InlineData("extra-file")]
    [InlineData("upstream-drift")]
    [InlineData("sheet-drift")]
    [InlineData("resume")]
    [InlineData("owner-rejected")]
    public void ArchitectureAuthoring_BindsBothFilesAndImplementationWithoutGrantingAuthority(string scenario)
    {
        using var repository = AgentRepository.Create(git: false);
        repository.AddBrd();
        repository.Write(".cis/local/test-source/content.md", "# Declared API\nPurchase a deposit.\n");
        repository.Write("src/modules/deposits/purchase.controller.ts", "export const purchase = () => purchaseService.initiate();\n");
        repository.Write("src/modules/deposits/purchase.service.ts", "const maximumDeposit = 42;\n");
        const string target = "docs/cis/architecture/overall-solution-design.md";
        const string sheet = "docs/cis/references/component-sheet.md";
        const string technical = "docs/cis/specs/technical-intent-spec.md";
        const string original = "---\nstatus: Review Required\n---\n# Architecture\nTODO: Complete executive summary through human review of workspace evidence.\n";
        repository.Write(target, original);
        repository.Write(sheet, "---\nstatus: Review Required\n---\n# Component sheet\n");
        repository.Write(technical, "---\nstatus: Draft\n---\n# Technical intent\nObserved application boundaries.\n");
        var beforeSheet = repository.Read(sheet); var beforeBrd = repository.Read("docs/cis/specs/business-requirements.md");
        var provider = new FakeProvider(draftSolutionDesign: true, omitImplementationCoverage: scenario == "coverage",
            alterProtected: scenario == "protected", mutateImplementation: scenario == "snapshot", extraFile: scenario == "extra-file",
            omitCoverageOnFirstExecution: scenario == "resume", duringExecution: scenario == "upstream-drift"
                ? _ => repository.Write(technical, "Human changed technical direction.\n")
                : scenario == "sheet-drift" ? _ => repository.Write(sheet, beforeSheet + "\nHuman edit.\n") : null);
        var owner = new ArchitectureDraftOwner(scenario == "owner-rejected");
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], clock: Clock,
            sourceEvidenceRegistrars: [new ProjectedRepositoryEvidence()], solutionDesignDrafts: owner);
        var result = service.AuthorSolutionDesign(repository.Path, [repository.Path], "fake", null, 60, false,
            "Andrew", TestContext.Current.CancellationToken);
        if (scenario == "resume")
        {
            Assert.False(result.Applied);
            result = service.Resume(repository.Path, result.Run!.Manifest.RunId, "Repair coverage.", "Andrew", "Complete architecture", false, TestContext.Current.CancellationToken);
            Assert.Equal(2, result.Run!.Manifest.Attempt);
        }
        if (scenario is "success" or "resume")
        {
            Assert.True(result.Applied, string.Join('\n', result.Diagnostics));
            Assert.Equal("SOLUTION-DESIGN-DRAFT", result.Run!.Manifest.TaskId);
            Assert.Single(result.Envelope!.ImplementationEvidence!);
            Assert.Contains("cis:architecture-views", provider.LastRequest!.Prompt, StringComparison.Ordinal);
            Assert.Contains("schemaVersion 2", provider.LastRequest.Prompt, StringComparison.Ordinal);
            Assert.Contains("C3 scopeId is ONE container ID from C2", provider.LastRequest.Prompt, StringComparison.Ordinal);
            Assert.Contains("deployment configuration", provider.LastRequest.Prompt, StringComparison.Ordinal);
            Assert.Contains("every required", provider.LastRequest.Prompt.ToLowerInvariant(), StringComparison.Ordinal);
            Assert.Contains("Observed component responsibility", repository.Read(sheet), StringComparison.Ordinal);
            Assert.Contains("status: Review Required", repository.Read(target), StringComparison.Ordinal);
            Assert.Equal(1, owner.ApplyCalls);
        }
        else
        {
            Assert.False(result.Applied);
            Assert.Equal(original, repository.Read(target));
            Assert.Equal(scenario == "sheet-drift" ? beforeSheet + "\nHuman edit.\n" : beforeSheet, repository.Read(sheet));
            Assert.Contains(result.Diagnostics, item => item.StartsWith("ERROR:", StringComparison.Ordinal));
        }
        Assert.Equal(beforeBrd, repository.Read("docs/cis/specs/business-requirements.md"));
    }

    [Fact]
    public void ArchitectureCommands_AreRegisteredAndDiscoveryDoesNotContactProvider()
    {
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new AgentModule()).Build();
        Assert.Equal(0, application.Invoke(["agent", "author", "solution-design", "--help"]));
        Assert.Equal(0, application.Invoke(["agent", "discover", "solution-design", "--help"]));
        using var repository = AgentRepository.Create(git: false);
        repository.AddBrd();
        repository.Write(".cis/local/test-source/content.md", "# Implementation projection\nObserved API.\n");
        repository.Write("src/application.ts", "export const observed = true;\n");
        repository.Write("docs/cis/architecture/overall-solution-design.md", "# Architecture draft\n");
        repository.Write("docs/cis/references/component-sheet.md", "# Component sheet\n");
        var provider = new FakeProvider();
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], clock: Clock,
            sourceEvidenceRegistrars: [new ProjectedRepositoryEvidence()], solutionDesignDrafts: new ArchitectureDraftOwner());
        var result = service.DiscoverBrdImplementation(repository.Path, [repository.Path], "Andrew", TestContext.Current.CancellationToken, solutionDesign: true);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, provider.ExecuteCalls);
        Assert.Equal("SOLUTION-DESIGN-EVIDENCE", result.Envelope!.TaskId);
        Assert.Contains("docs/cis/references/component-sheet.md", result.Envelope.ContextArtifacts);
    }

    private sealed class ArchitectureDraftOwner(bool reject = false) : ICisSolutionDesignDrafts
    {
        public int ApplyCalls { get; private set; }
        public CisSolutionDesignDraftResult PrepareExistingDraft(string workspacePath) => new([]);
        public CisSolutionDesignDraftResult ApplyExistingDraft(string workspacePath, string originalDesign, string originalComponents, string proposedDesign, string proposedComponents)
        {
            ApplyCalls++;
            if (reject) return new(["Malformed architecture diagrams."]);
            File.WriteAllText(Path.Combine(workspacePath, "docs/cis/architecture/overall-solution-design.md"), proposedDesign);
            File.WriteAllText(Path.Combine(workspacePath, "docs/cis/references/component-sheet.md"), proposedComponents);
            return new([], true);
        }
    }
}
