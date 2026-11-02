using System.Diagnostics;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Host;
using Xunit;

namespace Cis.Modules.Definition.Tests;

public sealed partial class DefinitionWizardTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeadlessInput_SuppliesInspectsResolvesAndResumesAcrossFreshCliProcesses(bool missingPrimaryStore)
    {
        using var repository = TemporaryRepository.Create();
        using var sources = TemporaryRepository.Create();
        var businessPath = Path.Combine(sources.Path, "Supplied BRD.md");
        var technicalPath = Path.Combine(sources.Path, "Supplied Technical Intent.md");
        File.WriteAllText(businessPath, AuthoritativeBusinessSource);
        var technical = AuthoritativeTechnicalSource.Replace(
            "#### Decision closure\nNo open technical decisions remain. All technical choices are closed for the current phase.",
            """
            #### Open technical decisions
            | ID | Decision | Required before | Status | Resolution |
            | --- | --- | --- | --- | --- |
            | TI-DEC-900 | Select the current-phase rollback owner. | Before rollout | Open | An accountable owner has not been selected. |
            """, StringComparison.Ordinal);
        Assert.NotEqual(AuthoritativeTechnicalSource, technical);
        if (missingPrimaryStore)
            technical = technical.Replace("| Persistence | The primary data store is PostgreSQL. |\n", "", StringComparison.Ordinal);
        File.WriteAllText(technicalPath, technical);
        await Cli(0, "workspace", "init", "--repo", repository.Path, "--root", "docs/cis",
            "--ecosystem", "sample", "--product", "sample", "--yes");
        // A fresh agent starts at the repository entry file and can follow the
        // owning skills to the same commands exercised below.
        var entry = File.ReadAllText(Path.Combine(repository.Path, "AGENTS.md"));
        const string repositoryInstructions = ".github/instructions/cis-repository.instructions.md";
        const string businessSkill = ".github/skills/cis-govern-business-requirements/SKILL.md";
        const string technicalSkill = ".github/skills/cis-govern-technical-intent/SKILL.md";
        Assert.Contains(repositoryInstructions, entry);
        Assert.Contains(businessSkill, entry);
        var routes = File.ReadAllText(Path.Combine(repository.Path, repositoryInstructions));
        Assert.Contains(businessSkill, routes);
        Assert.Contains(technicalSkill, routes);
        var importGuidance = File.ReadAllText(Path.Combine(repository.Path, businessSkill));
        Assert.Contains("cis definition documents", importGuidance);
        Assert.Contains("cis definition prepare", importGuidance);
        var answerGuidance = File.ReadAllText(Path.Combine(repository.Path, technicalSkill));
        Assert.Contains("technical-intent decisions resolve", answerGuidance);
        Assert.Contains("reviewToken", answerGuidance);
        foreach (var (role, path) in new[] { ("business", businessPath), ("technical", technicalPath) })
        {
            var supplied = await Cli(0, "definition", "documents", "--workspace", repository.Path, "--role", role, "--source", path);
            Assert.True(supplied.GetProperty("applied").GetBoolean());
            Assert.NotNull(supplied.GetProperty("documents").EnumerateArray().Single(item => item.GetProperty("role").GetString() == role)
                .GetProperty("selectedPath").GetString());
        }
        await Cli(0, "graph", "build", "--workspace", repository.Path);
        var prepared = await Cli(0, "definition", "prepare", "--workspace", repository.Path, "--page", "technical");
        var questions = prepared.GetProperty("technicalQuestions");
        Assert.Equal(!missingPrimaryStore, questions.GetProperty("complete").GetBoolean());
        var backend = questions.GetProperty("questions").EnumerateArray().Single(question => question.GetProperty("id").GetString() == "TI-Q-003");
        Assert.Equal("selected-document", backend.GetProperty("resolutionSource").GetString());
        Assert.Contains(".NET", backend.GetProperty("answer").GetString());
        Assert.NotEmpty(backend.GetProperty("evidence").EnumerateArray());
        var constraints = questions.GetProperty("questions").EnumerateArray().Single(question => question.GetProperty("id").GetString() == "TI-Q-016");
        Assert.Contains("45 seconds", constraints.GetProperty("answer").GetString());
        Assert.Contains("2 seconds", constraints.GetProperty("answer").GetString());
        Assert.Contains("phase 2", constraints.GetProperty("answer").GetString());
        var inspected = await Cli(0, "definition", "status", "--workspace", repository.Path);
        if (missingPrimaryStore)
        {
            var remaining = inspected.GetProperty("technicalQuestions").GetProperty("questions").EnumerateArray()
                .Where(question => question.GetProperty("status").GetString() == "Unanswered").ToArray();
            Assert.Equal("TI-Q-006", Assert.Single(remaining).GetProperty("id").GetString());
            Assert.False(inspected.GetProperty("pages").EnumerateArray().Single(page => page.GetProperty("id").GetString() == "technical")
                .GetProperty("complete").GetBoolean());
            // The fixture authorises exactly this factual answer, independently of the TI-DEC resolution below.
            var answered = await Cli(0, "definition", "answer", "--workspace", repository.Path, "--page", "technical",
                "--id", "TI-Q-006", "--answer", "PostgreSQL", "--actor", "Fixture data owner");
            Assert.True(answered.GetProperty("technicalQuestions").GetProperty("complete").GetBoolean());
            inspected = await Cli(0, "definition", "status", "--workspace", repository.Path);
        }
        var decision = Decision(inspected);
        Assert.True(decision.GetProperty("needsReview").GetBoolean());
        var token = decision.GetProperty("reviewToken").GetString()!;
        var inputPath = Path.Combine(sources.Path, "Authorised decision.json");
        const string answer = "The product operations owner handles rollback for the current release.";
        File.WriteAllText(inputPath, JsonSerializer.Serialize(new { resolution = answer, reviewToken = token }));
        // This synthetic fixture grants exactly this answer; actor text is provenance, not permission.
        var saved = await Cli(0, "technical-intent", "decisions", "resolve", "TI-DEC-900", "--workspace", repository.Path,
            "--input", inputPath, "--actor", "Fixture decision owner");
        Assert.True(saved.GetProperty("applied").GetBoolean());
        await Cli(0, "definition", "prepare", "--workspace", repository.Path, "--page", "technical");
        var selected = CisProductDocumentPaths.Read(repository.Path);
        var selectedTechnical = Path.Combine(repository.Path, selected["technical"]);
        var beforeStale = File.ReadAllBytes(selectedTechnical);
        var stale = await Cli(2, "technical-intent", "decisions", "resolve", "TI-DEC-900", "--workspace", repository.Path,
            "--input", inputPath, "--actor", "Fixture decision owner");
        Assert.Equal("conflict", stale.GetProperty("status").GetString());
        Assert.False(stale.GetProperty("applied").GetBoolean());
        Assert.Equal(beforeStale, File.ReadAllBytes(selectedTechnical));
        var reopened = await Cli(0, "definition", "status", "--workspace", repository.Path);
        var resolved = Decision(reopened);
        Assert.False(resolved.GetProperty("needsReview").GetBoolean());
        Assert.Equal(answer, resolved.GetProperty("recordedResolution").GetString());
        Assert.Equal("Fixture decision owner", resolved.GetProperty("recordedBy").GetString());
        Assert.True(reopened.GetProperty("technicalQuestions").GetProperty("complete").GetBoolean());
        Assert.True(reopened.GetProperty("pages").EnumerateArray().Single(page => page.GetProperty("id").GetString() == "technical")
            .GetProperty("complete").GetBoolean());
        var retained = reopened.GetProperty("technicalQuestions").GetProperty("questions").EnumerateArray().ToArray();
        Assert.Equal(backend.GetProperty("answer").GetString(), retained.Single(question => question.GetProperty("id").GetString() == "TI-Q-003")
            .GetProperty("answer").GetString());
        if (missingPrimaryStore)
        {
            var store = retained.Single(question => question.GetProperty("id").GetString() == "TI-Q-006");
            Assert.Equal("PostgreSQL", store.GetProperty("answer").GetString());
            Assert.Equal("human", store.GetProperty("resolutionSource").GetString());
            Assert.Equal("Fixture data owner", store.GetProperty("answeredBy").GetString());
            Assert.False(string.IsNullOrEmpty(store.GetProperty("answeredAtUtc").GetString()));
        }
        Assert.False(reopened.GetProperty("readyToActivate").GetBoolean());
        foreach (var role in new[] { "business", "technical" })
            Assert.Contains("approved_by: null", File.ReadAllText(Path.Combine(repository.Path, selected[role])));
        var domainPaths = new[] { selected["business"], selected["technical"],
            "docs/cis/specs/technical-intent-questionnaire.md", ".cis/local/definition-wizard/session.json" };
        var canonicalBeforeRead = domainPaths.ToDictionary(path => path,
            path => File.ReadAllBytes(Path.Combine(repository.Path, path)));
        await Cli(0, "definition", "status", "--workspace", repository.Path);
        foreach (var pair in canonicalBeforeRead)
            Assert.Equal(pair.Value, File.ReadAllBytes(Path.Combine(repository.Path, pair.Key)));
        Assert.Equal(AuthoritativeBusinessSource, File.ReadAllText(businessPath));
        Assert.Equal(technical, File.ReadAllText(technicalPath));

        static JsonElement Decision(JsonElement state) => state.GetProperty("pages").EnumerateArray()
            .Single(page => page.GetProperty("id").GetString() == "technical").GetProperty("guidance")
            .GetProperty("technicalDecisions").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "TI-DEC-900");
    }

    private static async Task<JsonElement> Cli(int expectedExit, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(CisApplication).Assembly.Location);
        foreach (var argument in args) start.ArgumentList.Add(argument);
        start.ArgumentList.Add("--format"); start.ArgumentList.Add("json");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            Assert.Fail($"CIS command exceeded the fixture limit. Partial output:\n{await stdout}\n{await stderr}");
        }
        var output = await stdout;
        var error = await stderr;
        Assert.True(process.ExitCode == expectedExit, $"cis {string.Join(' ', args.Take(3))}: exit {process.ExitCode}, expected {expectedExit}\n{output}\n{error}");
        using var result = JsonDocument.Parse(output);
        return result.RootElement.Clone();
    }
}
