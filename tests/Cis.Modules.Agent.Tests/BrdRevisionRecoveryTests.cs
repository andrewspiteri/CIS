using System.Text;
using Cis.Abstractions;
using Cis.Modules.Repository;
using Cis.Providers.Agent.Claude;
using Cis.Providers.Agent.Codex;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("drift")]
    [InlineData("ambiguous")]
    [InlineData("protected")]
    [InlineData("no-anchor")]
    public void RecoverBrdRevision_AppliesExactApprovedDiffsOrPreservesCanonicalState(string scenario)
    {
        using var repository = AgentRepository.Create(git: false);
        repository.AddBrd();
        const string anchor = "Approved context – customer’s outcome.";
        var brdPath = "docs/cis/specs/business-requirements.md";
        repository.Write(brdPath, repository.Read(brdPath) + "\r\n" + anchor + "\nUntouched mixed ending.\r\n");
        if (scenario == "ambiguous") repository.Write(brdPath, repository.Read(brdPath) + anchor + "\n");
        var original = repository.Read(brdPath);
        var reviewer = new FakeProvider(reviewBrd: true, id: "reviewer");
        var reviser = new FakeProvider(reviseBrd: true, omitRevisionIds: true, id: "reviser");
        var service = new AgentService(new CisRepositoryContextResolver(), [reviewer, reviser], clock: Clock);
        var review = service.ReviewBrd(repository.Path, "reviewer", null, 60, false, "Andrew",
            TestContext.Current.CancellationToken);
        Assert.Equal(0, review.ExitCode);
        var id = review.Run!.Manifest.RunId;
        var finding = review.Run.Result!.Review!.Findings.Single();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var corrupted = Encoding.GetEncoding(437).GetString(Encoding.UTF8.GetBytes(anchor));
        var diff = scenario switch
        {
            "protected" => "```diff\n-status: Review Required\n+status: Active\n```",
            "no-anchor" => "```diff\n+Unanchored new wording.\n```",
            _ => $"```diff\n {corrupted}\n+Agreed addition – same approved meaning.\n```",
        };
        var disposition = new CisBrdReviewDispositionDocument(2, id, review.Run.Manifest.ResultDigest!,
            review.Envelope!.CanonicalTaskDigest, "reviewer", Clock().ToString("O"),
            [new(finding.Id, finding.Severity, finding.Category, finding.Location, finding.Observation,
                finding.Recommendation, "accepted", "Andrew", Clock().ToString("O"), ApprovedRecommendation: diff)],
            "Andrew", Clock().ToString("O"));
        disposition = disposition with { ApprovedDecisionSha256 = CisBrdReviewDispositionCodec.ComputeDecisionDigest(disposition) };
        var dispositionPath = $"docs/cis/reviews/brd/{id}.md";
        repository.Write(dispositionPath, CisBrdReviewDispositionCodec.Render(disposition));
        var failed = service.ReviseBrd(repository.Path, id, "reviser", null, 60, false, "Andrew", TestContext.Current.CancellationToken);
        Assert.Equal(CisAgentRunStates.InvalidEvidence, failed.Run!.Manifest.Status);
        Assert.Contains(failed.Diagnostics, item => item.Contains("Approved findings were not applied: BRD-REV-001", StringComparison.Ordinal));
        Assert.Contains(failed.Diagnostics, item => item.Contains("Revision agent reported: Completed bounded fake work.", StringComparison.Ordinal));
        Assert.Equal(original, repository.Read(brdPath));
        var evidencePath = $".cis/local/agents/runs/{failed.Run.Manifest.RunId}/result.json";
        var evidence = repository.Read(evidencePath);
        var lastRequest = reviser.LastRequest;
        if (scenario == "drift") repository.Write(brdPath, original + "Changed since review.\n");
        var baseline = repository.Read(brdPath);

        var recovered = service.RecoverBrdRevision(repository.Path, failed.Run.Manifest.RunId, "Andrew", "Recover encoding failure.");

        Assert.Same(lastRequest, reviser.LastRequest);
        Assert.Equal(evidence, repository.Read(evidencePath));
        if (scenario != "success")
        {
            Assert.NotEqual(0, recovered.ExitCode);
            Assert.False(recovered.Applied);
            Assert.Equal(baseline, repository.Read(brdPath));
            if (scenario == "protected") Assert.Contains(recovered.Diagnostics, item => item.Contains("protected BRD frontmatter", StringComparison.Ordinal));
            return;
        }
        Assert.Equal(0, recovered.ExitCode);
        Assert.True(recovered.Applied);
        Assert.NotEqual(failed.Run.Manifest.RunId, recovered.Run!.Manifest.RunId);
        Assert.Equal("approved-diff-recovery", recovered.Run.Manifest.Transport);
        Assert.Equal(original.Replace(anchor + "\n", anchor + "\nAgreed addition – same approved meaning.\n", StringComparison.Ordinal), repository.Read(brdPath));
        Assert.Contains(recovered.Diagnostics, item => item.Contains("secondary independent review", StringComparison.Ordinal));
        Assert.True(CisBrdReviewDispositionCodec.TryParse(repository.Read(dispositionPath), out var applied, out _));
        Assert.Equal(disposition.ApprovedDecisionSha256, applied!.ApprovedDecisionSha256);
        Assert.Equal(diff, applied.Findings.Single().ApprovedRecommendation);
        Assert.Equal(recovered.Run.Manifest.RunId, applied.AppliedByRunId);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void AgentProviderStreams_PreserveUtf8UnderAnOemConsole(string provider)
    {
        if (!OperatingSystem.IsWindows()) return;
        var previous = Console.OutputEncoding;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Console.OutputEncoding = Encoding.GetEncoding(437);
            var start = provider == "codex" ? CodexAgentProvider.CreateStartInfo("powershell.exe", Environment.CurrentDirectory)
                : ClaudeAgentProvider.CreateStartInfo("powershell.exe", Environment.CurrentDirectory);
            start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
            start.ArgumentList.Add("$reader=[IO.StreamReader]::new([Console]::OpenStandardInput(),[Text.UTF8Encoding]::new($false)); $text=$reader.ReadToEnd(); $bytes=[Text.Encoding]::UTF8.GetBytes($text); [Console]::OpenStandardOutput().Write($bytes,0,$bytes.Length); [Console]::OpenStandardError().Write($bytes,0,$bytes.Length)");
            const string sample = "BRD – trader’s €10,000 → café 漢字";
            var output = new List<string>(); var error = new List<string>();
            var result = CisAgentProcessRunner.RunLines(start, sample, TimeSpan.FromSeconds(15), output.Add, error.Add,
                TestContext.Current.CancellationToken);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(sample, string.Join("\n", output).TrimEnd());
            Assert.Equal(sample, string.Join("\n", error).TrimEnd());
        }
        finally { Console.OutputEncoding = previous; }
    }
}
