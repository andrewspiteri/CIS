using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Host;
using Xunit;

namespace Cis.Modules.Definition.Tests;

public sealed partial class DefinitionWizardTests
{
    [Fact]
    public void HeadlessImpactReview_RetainsArchitectureForUnrelatedAnswerAndNamesRelevantReassessment()
    {
        using var repository = TemporaryRepository.Create();
        using var application = CreateApplication();
        Assert.Equal(0, Invoke(application, ["workspace", "init", "--repo", repository.Path, "--root", "docs/cis",
            "--ecosystem", "sample", "--product", "sample", "--yes", "--format", "json"]).ExitCode);
        string Run(CisApplication app, params string[] args)
        {
            var result = Invoke(app, [.. args, "--workspace", repository.Path, "--format", "json"]);
            Assert.True(result.ExitCode == 0, result.Output + result.Error);
            return result.Output;
        }
        Run(application, "graph", "build");
        Run(application, "brd", "init", "--title", "Reviewed product");
        var brdPath = Path.Combine(repository.Path, "docs/cis/specs/business-requirements.md");
        var brd = Regex.Replace(File.ReadAllText(brdPath), "(?m)^TODO: Complete .+$", "Stakeholders recorded complete and testable requirements.");
        File.WriteAllText(brdPath, Regex.Replace(brd, "(?ms)^## Functional requirements\\s*$.*?(?=^## )",
            "## Functional requirements\n\n| ID | Requirement | Priority | Acceptance intent |\n| --- | --- | --- | --- |\n| BRD-FR-001 | A user shall view the product. | Must | The product view is available. |\n\n"));
        Run(application, "graph", "build");
        Run(application, "brd", "approve", "--reviewer", "Owner", "--reason", "Product baseline reviewed.");
        Run(application, "definition", "prepare", "--page", "technical");
        using (var status = JsonDocument.Parse(Run(application, "definition", "status")))
            foreach (var question in status.RootElement.GetProperty("technicalQuestions").GetProperty("questions").EnumerateArray())
                Run(application, "definition", "answer", "--page", "technical", "--id", question.GetProperty("id").GetString()!,
                    "--answer", question.GetProperty("suggestedAnswer").GetString()!, "--actor", "Owner");
        JsonElement Decision(CisApplication app)
        {
            using var status = JsonDocument.Parse(Run(app, "technical-intent", "status"));
            return status.RootElement.GetProperty("validation").GetProperty("decisions").EnumerateArray()
                .Single(item => item.GetProperty("id").GetString() == "TI-DEC-001").Clone();
        }
        var decision = Decision(application);
        var input = Path.Combine(repository.Path, "review-answer.json");
        void Resolve(CisApplication app, JsonElement current)
        {
            File.WriteAllText(input, JsonSerializer.Serialize(new
            {
                resolution = "Reviewed component ownership and inward dependencies.",
                reason = "Architecture accepted.",
                reviewToken = current.GetProperty("reviewToken").GetString()
            }));
            Run(app, "technical-intent", "decisions", "resolve", "TI-DEC-001", "--input", input, "--actor", "Architect");
        }
        Resolve(application, decision);
        var recorded = Decision(application);
        Run(application, "definition", "answer", "--page", "technical", "--id", "TI-Q-015", "--answer", "No AI in this product.", "--actor", "Owner");
        using var restarted = CreateApplication();
        var unaffected = Decision(restarted);
        Assert.False(unaffected.GetProperty("needsReview").GetBoolean());
        Assert.Equal(recorded.GetProperty("recordedAtUtc").GetString(), unaffected.GetProperty("recordedAtUtc").GetString());
        Run(restarted, "definition", "answer", "--page", "technical", "--id", "TI-Q-004", "--answer", "Independent event-driven services.", "--actor", "Owner");
        var affected = Decision(restarted);
        Assert.True(affected.GetProperty("needsReview").GetBoolean());
        Assert.Contains("TI-Q-004", affected.GetProperty("assessmentReason").GetString(), StringComparison.Ordinal);
        Assert.Equal("Architect", affected.GetProperty("recordedBy").GetString());
        Resolve(restarted, affected);
        Assert.False(Decision(restarted).GetProperty("needsReview").GetBoolean());
        var document = Path.Combine(repository.Path, "docs/cis/specs/technical-intent-spec.md");
        var beforeReads = File.ReadAllBytes(document);
        Run(restarted, "definition", "status");
        Run(restarted, "technical-intent", "status");
        Assert.Equal(beforeReads, File.ReadAllBytes(document));

        // Renewing one answer succeeds while another local reassessment remains;
        // command success is distinct from the aggregate questionnaire gate.
        File.AppendAllText(brdPath, "\n## Runtime architecture\n\nBRD-FR-016: The product shall preserve explicit module boundaries across separate repositories.\n");
        Run(restarted, "brd", "approve", "--reviewer", "Owner", "--reason", "Reviewed changed architecture requirement.");
        using var beforeRenewal = JsonDocument.Parse(Run(restarted, "definition", "status"));
        var beforeQuestions = beforeRenewal.RootElement.GetProperty("technicalQuestions").GetProperty("questions").EnumerateArray().ToArray();
        Assert.Equal("NeedsReview", beforeQuestions.Single(question => question.GetProperty("id").GetString() == "TI-Q-004").GetProperty("assessmentStatus").GetString());
        var other = beforeQuestions.Single(question => question.GetProperty("id").GetString() == "TI-Q-005");
        Assert.Equal("NeedsReview", other.GetProperty("assessmentStatus").GetString());
        using var renewed = JsonDocument.Parse(Run(restarted, "definition", "answer", "--page", "technical", "--id", "TI-Q-004",
            "--answer", "Independent event-driven services.", "--actor", "Owner"));
        Assert.Empty(renewed.RootElement.GetProperty("errors").EnumerateArray());
        Assert.True(renewed.RootElement.GetProperty("applied").GetBoolean());
        var questionnaire = renewed.RootElement.GetProperty("technicalQuestions");
        Assert.False(questionnaire.GetProperty("current").GetBoolean());
        Assert.False(questionnaire.GetProperty("complete").GetBoolean());
        var remaining = questionnaire.GetProperty("questions").EnumerateArray().Single(question => question.GetProperty("id").GetString() == "TI-Q-005");
        Assert.Equal("NeedsReview", remaining.GetProperty("assessmentStatus").GetString());
        Assert.Equal(other.GetProperty("answer").GetString(), remaining.GetProperty("answer").GetString());
        Assert.Equal(other.GetProperty("answeredBy").GetString(), remaining.GetProperty("answeredBy").GetString());
        Assert.Equal(other.GetProperty("answeredAtUtc").GetString(), remaining.GetProperty("answeredAtUtc").GetString());
        var questionsPath = Path.Combine(repository.Path, "docs/cis/specs/technical-intent-questionnaire.md");
        var sessionPath = Path.Combine(repository.Path, ".cis/local/definition-wizard/session.json");
        var beforeNoop = File.ReadAllBytes(questionsPath);
        var beforeSession = File.ReadAllBytes(sessionPath);
        using var noop = JsonDocument.Parse(Run(restarted, "definition", "answer", "--page", "technical", "--id", "TI-Q-004",
            "--answer", "Independent event-driven services.", "--actor", "Owner"));
        Assert.Equal("unchanged", noop.RootElement.GetProperty("status").GetString());
        Assert.False(noop.RootElement.GetProperty("applied").GetBoolean());
        Assert.False(noop.RootElement.GetProperty("technicalQuestions").GetProperty("current").GetBoolean());
        Assert.Equal(beforeNoop, File.ReadAllBytes(questionsPath));
        Assert.Equal(beforeSession, File.ReadAllBytes(sessionPath));
    }
}
