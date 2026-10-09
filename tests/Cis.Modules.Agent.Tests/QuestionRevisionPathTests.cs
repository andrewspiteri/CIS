using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Agent;
using Cis.Modules.Repository;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData("Sensor Replay BRD.md", false)]
    [InlineData("Résumé Sensor BRD.md", false)]
    [InlineData("Sensor Replay BRD.md", true)]
    public void IncorporateBrdQuestions_UsesLiteralImportedPathsAndRejectsExtraFiles(string name, bool extra)
    {
        using var repository = AgentRepository.Create(git: false);
        repository.AddBrd();
        var relative = "docs/cis/imports/" + name;
        var original = repository.Read("docs/cis/specs/business-requirements.md")
            + "\n## Open questions\n\n| ID | Question | Answer | Answered by | Answered at UTC |\n"
            + "| --- | --- | --- | --- | --- |\n"
            + "| BRD-Q-001 | Who owns it? | Synthetic owner. | Test actor | 2026-10-09T00:00:00Z |\n";
        repository.Write(relative, original);
        repository.Write(".cis/product-documents.json", JsonSerializer.Serialize(new { business = relative }));
        var provider = new FakeProvider(incorporateQuestions: true, questionBrdPath: relative, extraFile: extra);
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], clock: Clock);
        var result = service.IncorporateBrdQuestions(repository.Path, "fake", null, 60, false,
            "Test actor", TestContext.Current.CancellationToken);
        if (extra)
        {
            Assert.False(result.Applied);
            Assert.Equal(original, repository.Read(relative));
            Assert.Equal(CisAgentRunStates.InvalidEvidence, result.Run!.Manifest.Status);
        }
        else
        {
            Assert.True(result.Applied, string.Join("; ", result.Diagnostics));
            Assert.Equal([relative], result.Run!.Result!.ChangedFiles);
            var repeat = service.IncorporateBrdQuestions(repository.Path, "fake", null, 60, false,
                "Test actor", TestContext.Current.CancellationToken);
            Assert.Equal("already-incorporated", repeat.Status);
            Assert.Equal(1, provider.ExecuteCalls);
        }
    }
    [Fact]
    public void ReviewBrd_RejectsAnUnavailableChangedFileInventory()
    {
        using var repository = AgentRepository.Create(git: false);
        repository.AddBrd();
        repository.Write("reference.md", "# Product note\nUsers need a shared planning board.\n");
        var author = new FakeProvider(draftBrd: true, id: "author");
        var reviewer = new FakeProvider(reviewBrd: true, id: "reviewer", duringExecution: request =>
            File.WriteAllText(System.IO.Path.Combine(request.WorkingDirectory, ".git", "index"), "invalid index"));
        var service = new AgentService(new CisRepositoryContextResolver(), [author, reviewer], clock: Clock);
        Assert.Equal(0, service.AuthorBrd(repository.Path, ["reference.md"], "author", null, 60, false,
            "Test actor", TestContext.Current.CancellationToken).ExitCode);

        var result = service.ReviewBrd(repository.Path, "reviewer", null, 60, false,
            "Test actor", TestContext.Current.CancellationToken);

        Assert.Equal(4, result.ExitCode);
        Assert.Equal(CisAgentRunStates.InvalidEvidence, result.Run!.Manifest.Status);
        Assert.Contains(result.Diagnostics, message => message.Contains("inventory could not be verified", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Run.Artifacts, artifact => artifact.Path.EndsWith("brd-review.md", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncorporateBrdQuestions_RechecksRetainedWorkspaceBeforeCopyBack(bool extraFile)
    {
        using var repository = AgentRepository.Create(git: false);
        repository.AddBrd();
        const string relative = "docs/cis/specs/business-requirements.md";
        var original = repository.Read(relative)
            + "\n## Open questions\n\n| ID | Question | Answer | Answered by | Answered at UTC |\n"
            + "| --- | --- | --- | --- | --- |\n"
            + "| BRD-Q-001 | Who owns it? | The sponsor. | Test actor | 2026-10-09T00:00:00Z |\n";
        repository.Write(relative, original);
        var provider = new FakeProvider(incorporateQuestions: true, alterQuestions: true);
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], clock: Clock);
        var rejected = service.IncorporateBrdQuestions(repository.Path, "fake", null, 60, false,
            "Test actor", TestContext.Current.CancellationToken);
        Assert.Equal("rejected", rejected.Status);
        Assert.False(rejected.Applied);
        Assert.Equal(CisAgentRunStates.Succeeded, rejected.Run!.Manifest.Status);
        var scratch = rejected.Run.Manifest.WorkingDirectory;
        var candidate = System.IO.Path.Combine(scratch, relative);
        File.WriteAllText(candidate, File.ReadAllText(candidate).Replace("A different actor.", "The sponsor.", StringComparison.Ordinal));
        if (extraFile) File.WriteAllText(System.IO.Path.Combine(scratch, "unexpected.txt"), "Outside scope.");

        var resumed = service.IncorporateBrdQuestions(repository.Path, "fake", null, 60, false,
            "Test actor", TestContext.Current.CancellationToken);

        Assert.Equal(1, provider.ExecuteCalls);
        Assert.Equal(rejected.Run.Manifest.RunId, resumed.Run!.Manifest.RunId);
        Assert.Equal(!extraFile, resumed.Applied);
        if (extraFile)
        {
            Assert.Equal("rejected", resumed.Status);
            Assert.Equal(original, repository.Read(relative));
            Assert.Contains(resumed.Diagnostics, message => message.Contains("one-file scope", StringComparison.Ordinal));
        }
        else Assert.Equal("succeeded", resumed.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReviewWithoutGit_VerifiesSourceContentBeforeReportingSuccess(bool mutate)
    {
        using var repository = AgentRepository.Create(git: false);
        repository.AddEligibleChange();
        repository.Write("source.txt", "Original content.");
        var provider = new FakeProvider(duringExecution: mutate ? request =>
            File.WriteAllText(System.IO.Path.Combine(request.WorkingDirectory, "source.txt"), "Changed content.") : null);
        var service = new AgentService(new CisRepositoryContextResolver(), [provider], clock: Clock);

        var result = service.Run(repository.Path, "CIS-0001", "WORK-090", "fake", CisAgentRunModes.Review,
            CisAgentPermissions.ReadOnly, null, null, 60, false, "Test actor", TestContext.Current.CancellationToken);

        Assert.Equal(mutate ? 4 : 0, result.ExitCode);
        Assert.Equal(mutate ? CisAgentRunStates.InvalidEvidence : CisAgentRunStates.Succeeded, result.Run!.Manifest.Status);
        Assert.Equal(mutate ? new[] { "source.txt" } : [], result.Run.Result!.ChangedFiles);
    }

}
