using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Host;
using Cis.Modules.Repository;

namespace Cis.Modules.Brd.Tests;

public sealed partial class BrdWorkflowTests
{
    [Fact]
    public void Layout_ExcludesManagedProvenanceFromMappedBusinessContent()
    {
        using var environment = WorkspaceEnvironment.Create(0);
        var original = Cis.Abstractions.CisBrdPresentation.RestoreManagedEvidence(PrepareLayoutBrd(environment));
        File.WriteAllText(environment.CanonicalPath, original);
        var input = Path.Combine(environment.Authority.Path, ".cis/local/layout.json");
        File.WriteAllText(input, """{"schemaVersion":1,"sections":{"Scope":[{"heading":"Source assessment"}]}}""");
        Assert.NotEmpty(environment.Service.MapLayout(environment.Authority.Path, input).Errors);
        Assert.Equal(original, File.ReadAllText(environment.CanonicalPath));
        var managed = "\n<!-- cis:sources:start -->\n| PROVENANCE-01 | Not business content. |\n<!-- cis:sources:end -->\n";
        File.WriteAllText(environment.CanonicalPath, original.Replace("### 7.1 Limits", managed + "### 7.1 Limits"));
        LayoutInput(environment, new BrdSectionSelection("7 Capture policy"));
        Assert.Equal(new[] { "CAPTURE-01" }, environment.Service.MapLayout(environment.Authority.Path, input).RequirementIds);
    }

    [Theory]
    [InlineData("--- \t")]
    [InlineData("---\t")]
    public void Layout_PreservesMappingWithSupportedFrontmatterWhitespace(string delimiter)
    {
        using var environment = WorkspaceEnvironment.Create(0);
        PrepareLayoutBrd(environment);
        var input = LayoutInput(environment, new BrdSectionSelection("7 Capture policy"));
        var preview = environment.Service.MapLayout(environment.Authority.Path, input);
        Assert.True(environment.Service.MapLayout(environment.Authority.Path, input, true, preview.ReviewHash).Applied);
        var mapped = Regex.Replace(File.ReadAllText(environment.CanonicalPath), @"(?m)^---\r?$", delimiter);
        File.WriteAllText(environment.CanonicalPath, mapped);
        Assert.True(environment.Service.Validate(environment.Authority.Path).Validation!.Valid);
        Assert.Equal(new[] { "CAPTURE-01" }, environment.Service.MapLayout(environment.Authority.Path, input).RequirementIds);
        File.WriteAllText(environment.CanonicalPath, mapped.Replace("cis_brd_layout:", "  cis_brd_layout:"));
        Assert.Contains(environment.Service.Validate(environment.Authority.Path).Validation!.Errors, error => error.Contains("Invalid BRD layout"));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Layout_PreservesDistributedNarrativeAndUsesSameRequirementsForBacklog(string newline)
    {
        using var environment = WorkspaceEnvironment.Create(1);
        var original = PrepareLayoutBrd(environment).Replace("\r\n", "\n").Replace("\n", newline);
        File.WriteAllText(environment.CanonicalPath, original);
        var input = LayoutInput(environment, new BrdSectionSelection("7 Capture policy"), new("8 Replay policy", false));
        var preview = environment.Service.MapLayout(environment.Authority.Path, input);
        Assert.Equal(0, preview.ExitCode);
        Assert.Equal(new[] { "CAPTURE-01", "REPLAY-01" }, preview.RequirementIds);
        Assert.Equal(original, File.ReadAllText(environment.CanonicalPath));
        Assert.False(environment.Service.MapLayout(environment.Authority.Path, input, true, "wrong").Applied);
        var applied = environment.Service.MapLayout(environment.Authority.Path, input, true, preview.ReviewHash);
        Assert.True(applied.Applied, string.Join("; ", applied.Errors));
        var mapped = File.ReadAllText(environment.CanonicalPath);
        Assert.Equal(Regex.Match(original, @"(?s)\r?\n---\r?\n(.*)").Groups[1].Value,
            Regex.Match(mapped, @"(?s)\r?\n---\r?\n(.*)").Groups[1].Value);
        Assert.Equal("unchanged", environment.Service.MapLayout(environment.Authority.Path, input).Status);
        Assert.True(environment.Service.Validate(environment.Authority.Path).Validation!.Valid);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Reviewed synthetic scope.").ExitCode);
        environment.Authority.Write("docs/design/ui-direction.md", "---\ncis:\n  visual_ui: false\n---\nNo visual interface.\n");
        var backlog = new BrdBacklogService(environment.Service, environment.Registry,
            new CisRepositoryContextResolver(), new DocumentationCatalogMerger(), [new ReadyCheck()]);
        var built = backlog.Build(environment.Authority.Path);
        Assert.Equal(0, built.ExitCode);
        Assert.Equal(preview.RequirementIds, built.Items.Select(item => item.RequirementId));
        Assert.Equal("Capture observations", built.Items[0].Outcome);
        Assert.Equal(0, backlog.Approve(environment.Authority.Path, "Fixture owner", "Reviewed synthetic backlog.").ExitCode);
        var started = backlog.StartFeature(environment.Authority.Path, "HLT-CAPTURE-01");
        Assert.Equal(0, started.ExitCode);
        var specification = File.ReadAllText(Path.Combine(environment.Authority.Path, started.RelativePath!));
        Assert.Contains("LIMIT-01", specification);
        Assert.Contains("Never invent missing readings.", specification);
        Assert.DoesNotContain("Replay introduction", specification);
        Assert.DoesNotContain("AC-01", specification);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("overlap")]
    [InlineData("comment")]
    [InlineData("fence")]
    [InlineData("ambiguous")]
    public void Layout_RejectsUnavailableAmbiguousOrOverlappingHeadingsWithoutWrites(string kind)
    {
        using var environment = WorkspaceEnvironment.Create(0);
        var original = PrepareLayoutBrd(environment);
        var selections = kind switch
        {
            "overlap" => new[] { new BrdSectionSelection("7 Capture policy"), new BrdSectionSelection("7.1 Limits") },
            "comment" => [new BrdSectionSelection("Hidden heading")],
            "fence" => [new BrdSectionSelection("Example heading")],
            "ambiguous" => [new BrdSectionSelection("Repeated heading")],
            _ => [new BrdSectionSelection("Missing heading")],
        };
        var input = LayoutInput(environment, selections);
        Assert.NotEmpty(environment.Service.MapLayout(environment.Authority.Path, input).Errors);
        Assert.Equal(original, File.ReadAllText(environment.CanonicalPath));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"sections\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"sections\":{\"Open questions\":[{\"heading\":\"Activation decisions\"}]}}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"sections\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"sections\":{\"Scope\":[null]}}")]
    [InlineData("{\"schemaVersion\":1,\"sections\":{\"Scope\":[]}}")]
    [InlineData("{\"schemaVersion\":1,\"sections\":{\"Scope\":[{\"heading\":\"7 Capture policy\",\"unexpected\":true}]}}")]
    public void Layout_RejectsMalformedOrDecisionMappingInput(string json)
    {
        using var environment = WorkspaceEnvironment.Create(0);
        var original = PrepareLayoutBrd(environment);
        var input = Path.Combine(environment.Authority.Path, ".cis/local/layout.json");
        File.WriteAllText(input, json);
        Assert.NotEmpty(environment.Service.MapLayout(environment.Authority.Path, input).Errors);
        Assert.Equal(original, File.ReadAllText(environment.CanonicalPath));
    }

    [Fact]
    public void Layout_BindsBothInputsAndInvalidatesApprovalWhenMappingChanges()
    {
        using var environment = WorkspaceEnvironment.Create(0);
        PrepareLayoutBrd(environment);
        var input = LayoutInput(environment, new BrdSectionSelection("7 Capture policy"));
        var preview = environment.Service.MapLayout(environment.Authority.Path, input);
        LayoutInput(environment, new BrdSectionSelection("8 Replay policy", false));
        Assert.False(environment.Service.MapLayout(environment.Authority.Path, input, true, preview.ReviewHash).Applied);
        LayoutInput(environment, new BrdSectionSelection("7 Capture policy"));
        File.AppendAllText(environment.CanonicalPath, "\nAn additional human clarification.\n");
        Assert.False(environment.Service.MapLayout(environment.Authority.Path, input, true, preview.ReviewHash).Applied);
        preview = environment.Service.MapLayout(environment.Authority.Path, input);
        Assert.True(environment.Service.MapLayout(environment.Authority.Path, input, true, preview.ReviewHash).Applied);
        Assert.Equal(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Reviewed.").ExitCode);
        var approved = File.ReadAllText(environment.CanonicalPath);
        LayoutInput(environment, new BrdSectionSelection("7 Capture policy"), new("8 Replay policy", false));
        preview = environment.Service.MapLayout(environment.Authority.Path, input);
        Assert.True(environment.Service.MapLayout(environment.Authority.Path, input, true, preview.ReviewHash).Applied);
        var changed = File.ReadAllText(environment.CanonicalPath);
        Assert.Contains("status: Review Required", changed);
        Assert.Contains("approved_content_hash: null", changed);
        Assert.NotEqual(BrdDocumentDigest.Compute(approved), BrdDocumentDigest.Compute(changed));
        Assert.NotEqual("Active", environment.Service.Status(environment.Authority.Path).Validation!.EffectiveStatus);
    }

    [Fact]
    public void Layout_DetectsDuplicateDefinitionsAndFailsClosedAfterHeadingDrift()
    {
        using var environment = WorkspaceEnvironment.Create(0);
        var original = PrepareLayoutBrd(environment);
        var input = LayoutInput(environment, new BrdSectionSelection("7 Capture policy"), new("8 Replay policy", false));
        File.WriteAllText(environment.CanonicalPath, original.Replace("REPLAY-01", "CAPTURE-01"));
        Assert.Contains(environment.Service.MapLayout(environment.Authority.Path, input).Errors, error => error.Contains("more than once"));
        File.WriteAllText(environment.CanonicalPath, original);
        var preview = environment.Service.MapLayout(environment.Authority.Path, input);
        Assert.True(environment.Service.MapLayout(environment.Authority.Path, input, true, preview.ReviewHash).Applied);
        File.WriteAllText(environment.CanonicalPath, File.ReadAllText(environment.CanonicalPath).Replace("## 7 Capture policy", "## Renamed policy"));
        Assert.Contains(environment.Service.Validate(environment.Authority.Path).Validation!.Errors, error => error.Contains("Invalid BRD layout"));
        Assert.NotEqual(0, environment.Service.Approve(environment.Authority.Path, "Fixture owner", "Cannot approve drift.").ExitCode);
    }

    [Fact]
    public void Layout_CommandSupportsPreviewAndRejectsInvalidOutputFormatBeforeMutation()
    {
        using var environment = WorkspaceEnvironment.Create(0);
        var original = PrepareLayoutBrd(environment);
        var input = LayoutInput(environment, new BrdSectionSelection("7 Capture policy"));
        using var host = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new WorkspaceModule()).AddModule(new Cis.Modules.Docs.DocsModule()).AddModule(new Cis.Modules.Graph.GraphModule()).AddModule(new BrdModule()).Build();
        Assert.Equal(0, host.Invoke(["brd", "layout", "--workspace", environment.Authority.Path, "--input", input, "--format", "json"]));
        Assert.Equal(2, host.Invoke(["brd", "layout", "--workspace", environment.Authority.Path, "--input", input, "--yes", "--format", "unsupported"]));
        Assert.Equal(original, File.ReadAllText(environment.CanonicalPath));
    }

    private static string LayoutInput(WorkspaceEnvironment environment, params BrdSectionSelection[] selections)
    {
        var path = Path.Combine(environment.Authority.Path, ".cis/local/layout.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new BrdLayoutDefinition(1,
            new() { ["Functional requirements"] = selections }), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return path;
    }

    private static string PrepareLayoutBrd(WorkspaceEnvironment environment)
    {
        environment.Service.Initialize(environment.Authority.Path, "Synthetic sensor replay BRD");
        CompleteHumanReview(environment.CanonicalPath, false);
        var content = File.ReadAllText(environment.CanonicalPath).Replace("## Functional requirements", "## Unmapped legacy functions") + """

            ## 7 Capture policy
            **CAPTURE-01 Capture observations.** Preserve original event identifiers.

            | Constraint | Value |
            | --- | --- |
            | LIMIT-01 | 500 readings |

            Never invent missing readings.
            ### 7.1 Limits
            Keep resource constraints explicit.
            ## 8 Replay policy
            Replay introduction must not extend capture requirements.
            **REPLAY-01 Replay once.** A repeated observation has no duplicate effect.
            ### Acceptance examples
            **AC-01 Replay evidence.** Demonstrate the complete readback.
            <!--
            ## Hidden heading
            **HIDDEN-01 Ignore.** This is not source content.
            -->
            ```markdown
            ## Example heading
            **FAKE-01 Ignore.** This is a fenced example.
            ```
            ## Repeated heading
            First.
            ## Repeated heading
            Second.
            """;
        File.WriteAllText(environment.CanonicalPath, content);
        File.WriteAllText(Path.Combine(environment.Authority.Path, ".cis/product-documents.json"),
            JsonSerializer.Serialize(new Dictionary<string, string> { ["business"] = Path.GetRelativePath(environment.Authority.Path, environment.CanonicalPath).Replace('\\', '/') }));
        return content;
    }
}
