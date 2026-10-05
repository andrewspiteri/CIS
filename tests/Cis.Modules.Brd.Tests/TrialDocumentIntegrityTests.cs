using System.Text.RegularExpressions;
using Cis.Modules.Repository;
using Xunit;

namespace Cis.Modules.Brd.Tests;

public sealed partial class BrdWorkflowTests
{
    [Fact]
    public void ApprovalPreservesHistoricalYamlAndBindsItToTheDigest()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        environment.Service.Initialize(environment.Authority.Path, "Imported BRD");
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        const string history = "\n## Historical metadata\n\n```yaml\nstatus: Draft\n  approved_by: old-reviewer\n```\n";
        var content = File.ReadAllText(environment.CanonicalPath);
        content = Regex.Replace(content, @"(?m)^  approved_by:.*\r?\n", "");
        File.WriteAllText(environment.CanonicalPath, content + history);
        var approved = environment.Service.Approve(environment.Authority.Path, "Owner", "Reviewed imported requirements.");
        Assert.True(approved.ExitCode == 0, string.Join("; ", approved.Validation?.Errors ?? approved.Errors));
        var after = File.ReadAllText(environment.CanonicalPath);
        Assert.EndsWith(history, after);
        Assert.NotEqual(BrdDocumentDigest.Compute(after), BrdDocumentDigest.Compute(after.Replace("old-reviewer", "edited-history")));
    }

    [Fact]
    public void NarrativeRequirementsPreserveReviewedCouldPriorityOnRepeatedBuilds()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        environment.Service.Initialize(environment.Authority.Path, "Imported BRD");
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        var content = Regex.Replace(File.ReadAllText(environment.CanonicalPath), @"(?ms)^## Functional requirements\s*$.*?(?=^## )",
            "## Functional requirements\n\n**FL-01 — Future work.** Separately reviewed future scope.\n\n");
        File.WriteAllText(environment.CanonicalPath, content);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Owner", "Reviewed requirements.").ExitCode);
        var backlog = new BrdBacklogService(environment.Service, environment.Registry,
            new CisRepositoryContextResolver(), new DocumentationCatalogMerger(), [new ReadyCheck()]);
        var built = backlog.Build(environment.Authority.Path);
        Assert.Equal(0, built.ExitCode);
        var path = Path.Combine(environment.Authority.Path, built.RelativePath!);
        File.WriteAllText(path, File.ReadAllText(path).Replace("| Must |", "| Could |"));
        Assert.Equal(0, backlog.Approve(environment.Authority.Path, "Owner", "Reviewed deferred priority.").ExitCode);
        Assert.Equal("Could", Assert.Single(backlog.Build(environment.Authority.Path).Items).Priority);
        Assert.False(backlog.Build(environment.Authority.Path).Applied);
        var saved = File.ReadAllText(path);
        var catalog = File.ReadAllText(Path.Combine(environment.Authority.Path, "docs", "catalog.yml"));
        File.WriteAllText(environment.CanonicalPath, File.ReadAllText(environment.CanonicalPath).Replace("Future work.", "Revised future work."));
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Owner", "Reviewed revised outcome.").ExitCode);
        var candidate = backlog.Build(environment.Authority.Path);
        Assert.Equal("blocked", candidate.Status);
        Assert.False(candidate.Applied);
        Assert.Equal(saved, File.ReadAllText(path));
        Assert.Equal(catalog, File.ReadAllText(Path.Combine(environment.Authority.Path, "docs", "catalog.yml")));
        Assert.Contains("Revised", Assert.Single(candidate.Items).Outcome);
        Assert.True(backlog.Build(environment.Authority.Path, null, null, applyReviewedChanges: true).Applied);
    }
}
