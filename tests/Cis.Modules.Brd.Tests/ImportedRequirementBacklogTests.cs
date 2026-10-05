using System.Text.RegularExpressions;
using Cis.Modules.Repository;
using Xunit;

namespace Cis.Modules.Brd.Tests;

public sealed partial class BrdWorkflowTests
{
    [Fact]
    public void ImportedRequirements_NormalizeNumberedSectionsAndDomainIdsWithoutChangingBrd()
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        Assert.Equal(0, environment.Service.Initialize(environment.Authority.Path, "Imported BRD").ExitCode);
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        var content = Regex.Replace(File.ReadAllText(environment.CanonicalPath), "(?ms)^## Functional requirements\\s*$.*?(?=^## )", """
            ## 7. Functional requirements

            ### 7.1 Market capture

            **MD-01 — Provider connection.** The platform must retain public source evidence.

            - Preserve the market identifier.
            - Do not invent missing observations.

            **MD-02 — Event capture.** Retain original events and normalized records.

            #### 7.1.1 Fee evidence

            This paragraph introduces the next group; it does not extend MD-02.

            **FE-01 — Fee sourcing.** Capture applicable fees with their source.

            ```markdown
            **EXAMPLE-01 — Not a requirement.** Ignore examples.
            ```
            <!-- **HIDDEN-01 — Not a requirement.** Ignore comments. -->

            """ + "\n");
        content = Regex.Replace(content, "(?ms)^## Quality, regulatory, and operational requirements\\s*$.*?(?=^## )",
            "## 10. Non-functional requirements\n\n| ID | Requirement |\n| --- | --- |\n| NF-01 | Keep deterministic precision. |\n\n");
        File.WriteAllText(environment.CanonicalPath, content);
        File.WriteAllText(Path.Combine(environment.Authority.Path, ".cis/product-documents.json"),
            System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["business"] = Path.GetRelativePath(environment.Authority.Path, environment.CanonicalPath).Replace('\\', '/') }));
        var approved = environment.Service.Approve(environment.Authority.Path, "Owner", "Reviewed imported requirements.");
        Assert.True(approved.ExitCode == 0, string.Join("; ", approved.Validation?.Errors ?? approved.Errors));
        var original = File.ReadAllBytes(environment.CanonicalPath);
        environment.Authority.Write("docs/design/ui-direction.md", "---\ncis:\n  visual_ui: false\n---\nNo visual interface.\n");
        var backlog = new BrdBacklogService(environment.Service, environment.Registry,
            new CisRepositoryContextResolver(), new DocumentationCatalogMerger(), [new ReadyCheck()]);
        var built = backlog.Build(environment.Authority.Path);
        Assert.True(built.ExitCode == 0, string.Join("; ", built.Validation?.Errors ?? built.Errors));
        Assert.Equal(new[] { "MD-01", "MD-02", "FE-01" }, built.Items.Select(item => item.RequirementId));
        Assert.Equal("Provider connection", built.Items[0].Outcome);
        Assert.All(built.Items, item => Assert.Empty(item.FrontendTypes));
        Assert.Contains("NF-01", File.ReadAllText(Path.Combine(environment.Authority.Path, built.RelativePath!)));
        Assert.Equal(original, File.ReadAllBytes(environment.CanonicalPath));
        Assert.False(backlog.Build(environment.Authority.Path).Applied);
        Assert.Equal(0, backlog.Approve(environment.Authority.Path, "Owner", "Reviewed plan.").ExitCode);
        var started = backlog.StartFeature(environment.Authority.Path, "HLT-MD-01");
        Assert.Equal(0, started.ExitCode);
        var feature = File.ReadAllText(Path.Combine(environment.Authority.Path, started.RelativePath!));
        Assert.Contains("Preserve the market identifier.", feature);
        Assert.Contains("Do not invent missing observations.", feature);
        Assert.DoesNotContain("This paragraph introduces", feature);
    }

    [Theory]
    [InlineData("Unidentified requirements in free prose.", "no readable functional requirements")]
    [InlineData("**MD-01 — First.** Retain events.\n\n**MD-01 — Duplicate.** Retain other events.", "identity occurs more than once")]
    public void ImportedRequirements_FailApprovalPreflightForUnreadableOrDuplicateEntries(string requirements, string finding)
    {
        using var environment = WorkspaceEnvironment.Create(participants: 1);
        environment.Service.Initialize(environment.Authority.Path, "Imported BRD");
        CompleteHumanReview(environment.CanonicalPath, hasCandidate: false);
        var content = Regex.Replace(File.ReadAllText(environment.CanonicalPath), "(?ms)^## Functional requirements\\s*$.*?(?=^## )",
            "## 7. Functional requirements\n\n" + requirements + "\n\n");
        File.WriteAllText(environment.CanonicalPath, content);
        File.WriteAllText(Path.Combine(environment.Authority.Path, ".cis/product-documents.json"),
            System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["business"] = Path.GetRelativePath(environment.Authority.Path, environment.CanonicalPath).Replace('\\', '/') }));
        var approval = environment.Service.Approve(environment.Authority.Path, "Owner", "Review request.");
        Assert.NotEqual(0, approval.ExitCode);
        Assert.Contains(approval.Validation!.Errors, error => error.Contains(finding));
        Assert.Equal(content, File.ReadAllText(environment.CanonicalPath));
    }
}
