using System.Text.RegularExpressions;

namespace Cis.Modules.Brd.Tests;

public sealed partial class BrdWorkflowTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void OpenQuestions_IgnoreHiddenRowsAndPreserveTrailingManagedEvidence(string newline)
    {
        using var environment = WorkspaceEnvironment.Create(0);
        environment.Service.Initialize(environment.Authority.Path, "Synthetic reader fixture");
        CompleteHumanReview(environment.CanonicalPath, false);
        var content = Regex.Replace(File.ReadAllText(environment.CanonicalPath),
            @"(?ms)^## Open questions\s*$.*?(?=^## |\z)", "");
        const string table = "## Open questions\n\n| ID | Question | Answer | Answered by | Answered at UTC |\n| --- | --- | --- | --- | --- |\n| BRD-Q-001 | Who owns the fixture? | | | |\n";
        const string hidden = "<!--\n| BRD-Q-999 | Hidden question? | | | |\n-->\n```markdown\n| BRD-Q-998 | Example question? | | | |\n```\n";
        const string evidence = "<!-- cis:brd-evidence\n## Source assessment\n-->\n<!-- cis:sources:start -->\n<!-- cis:brd-evidence\n| ID | Type | Repository | Path | Hash | Assessment | Rationale |\n| none | none | none | No additional source | none | Not Applicable | Canonical source |\n-->\n<!-- cis:sources:end -->\n";
        var suffix = evidence.Replace("\n", newline);
        File.WriteAllText(environment.CanonicalPath, content + (table + hidden).Replace("\n", newline) + suffix);
        var listed = environment.Service.Questions(environment.Authority.Path);
        Assert.Empty(listed.Errors);
        Assert.Equal("BRD-Q-001", Assert.Single(listed.Questions).Id);
        var result = environment.Service.AnswerQuestion(environment.Authority.Path, "BRD-Q-001", "Synthetic fixture owner", "Test actor");
        Assert.Empty(result.Errors);
        Assert.True(result.Applied);
        Assert.EndsWith(suffix, File.ReadAllText(environment.CanonicalPath));
        var answered = environment.Service.Questions(environment.Authority.Path);
        Assert.Equal("Synthetic fixture owner", Assert.Single(answered.Questions).Answer);
        var beforeRepeat = File.ReadAllBytes(environment.CanonicalPath);
        Assert.False(environment.Service.AnswerQuestion(environment.Authority.Path, "BRD-Q-001", "Synthetic fixture owner", "Test actor").Applied);
        Assert.Equal(beforeRepeat, File.ReadAllBytes(environment.CanonicalPath));
    }

    [Fact]
    public void OpenQuestions_RejectInterleavedManagedEvidenceWithoutWriting()
    {
        using var environment = WorkspaceEnvironment.Create(0);
        environment.Service.Initialize(environment.Authority.Path, "Synthetic reader fixture");
        CompleteHumanReview(environment.CanonicalPath, false);
        var content = Regex.Replace(File.ReadAllText(environment.CanonicalPath),
            @"(?ms)^## Open questions\s*$.*?(?=^## |\z)", "");
        content += "\n## Open questions\n\n1. Who owns the fixture?\n<!-- cis:baseline:start -->\nHidden evidence\n<!-- cis:baseline:end -->\n2. Which boundary applies?\n";
        File.WriteAllText(environment.CanonicalPath, content);
        Assert.Equal(2, environment.Service.Questions(environment.Authority.Path).UnansweredCount);
        var before = File.ReadAllBytes(environment.CanonicalPath);
        var result = environment.Service.AnswerQuestion(environment.Authority.Path, "BRD-Q-001", "Synthetic owner", "Test actor");
        Assert.NotEmpty(result.Errors);
        Assert.False(result.Applied);
        Assert.Equal(before, File.ReadAllBytes(environment.CanonicalPath));
    }
}
