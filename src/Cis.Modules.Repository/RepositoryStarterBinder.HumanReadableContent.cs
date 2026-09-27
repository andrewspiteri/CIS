using System.Text.RegularExpressions;

namespace Cis.Modules.Repository;

internal sealed partial class RepositoryStarterBinder
{
    private const int ContentTemplateVersion = 2;
    private static readonly string[] ContentGuidanceSkills =
    [
        "documentation", "add-documentation", "govern-business-requirements", "govern-technical-intent",
        "govern-solution-design", "govern-ui-direction", "feature-specification-governance",
        "impact-review", "design-review", "frontend-context",
    ];

    private static void AddHumanReadableContent(string repositoryId, string documentationRoot,
        RepositoryClassification classification, List<RepositoryStarterSelection> selections,
        List<RepositoryStarterArtifact> artifacts)
    {
        string Load(string name) => LoadEmbeddedDesignDocument("Content." + name)
            .Replace("change-impact-studio:", repositoryId + ":", StringComparison.Ordinal)
            .Replace("owner: \"Andrew Spiteri\"", "owner: Repository maintainer", StringComparison.Ordinal)
            .Replace("docs/", documentationRoot + "/", StringComparison.Ordinal);

        void Add(string definition, string path, string content, CatalogArtifactEntry? catalog = null)
        {
            selections.Add(new(definition, "Human-readable content policy and its complete dependencies.", ["cis curated starter"]));
            artifacts.Add(new(definition, definition, path, content, catalog, ContentTemplateVersion));
        }

        var standardPath = $"{documentationRoot}/standards/human-readable-content-standard.md";
        var standard = Load("standard.md");
        Add("standard.human-readable-content", standardPath, standard,
            new($"{repositoryId}:standard:human-readable-content", standardPath, "standard", "draft", "canonical"));
        var termsPath = $"{documentationRoot}/references/human-readable-content-terms.md";
        Add("reference.human-readable-content-terms", termsPath, Load("terms.md"),
            new($"{repositoryId}:reference:human-readable-content-terms", termsPath, "reference", "draft", "canonical"));
        Add("guidance.content.fixtures", $"{documentationRoot}/references/human-readable-content-fixtures.json", Load("fixtures.json"));
        foreach (var name in new[] { "technical-writing", "ux-writing", "content-review" })
            Add($"guidance.skill.{name}", $".github/skills/cis-{name}/SKILL.md", Load(name + ".md"));
        Add("guidance.instruction.human-readable-content", ".github/instructions/cis-human-readable-content.instructions.md", Load("human-readable.instructions.md"));

        // Scope interface instructions to actual UI components. Backend-only products still
        // receive the portable skill because other governance guidance references it.
        var uiRoots = classification.Components.Where(component => component.Roles.Any(role =>
                role is "frontend-consumer" or "mobile-client" or "native-frontend")
                || component.Frameworks.Contains("vscode-extension", StringComparer.OrdinalIgnoreCase))
            .Select(component => component.Root == "." ? "**" : component.Root.TrimEnd('/') + "/**")
            .Distinct(StringComparer.Ordinal).ToArray();
        if (uiRoots.Length > 0)
            Add("guidance.instruction.ux-content", ".github/instructions/cis-ux-content.instructions.md",
                Load("ux.instructions.md").Replace("vscode-extension/**", string.Join(',', uiRoots), StringComparison.Ordinal));

        var route = $"\n\n## Human-readable content\n\nApply `{documentationRoot}/standards/human-readable-content-standard.md` to in-scope prose. "
            + "Use `.github/skills/cis-technical-writing/SKILL.md` for documents and reports, "
            + "`.github/skills/cis-ux-writing/SKILL.md` for interface wording, and `.github/skills/cis-content-review/SKILL.md` for source-aware review followed by a separate reader check. "
            + "Preserve domain authority, evidence, privacy, required sections and legitimate uncertainty. A readability review does not approve content.\n";
        for (var index = 0; index < artifacts.Count; index++)
        {
            var artifact = artifacts[index];
            if (ContentGuidanceSkills.Any(name => artifact.Definition == "guidance.skill." + name)
                || artifact.Definition is "guidance.instruction.repository" or "guidance.instruction.business-requirements"
                    or "guidance.instruction.technical-intent" or "guidance.instruction.solution-design"
                    or "guidance.instruction.ui-direction" or "guidance.instruction.feature-specifications"
                    or "guidance.instruction.frontend-context")
                artifacts[index] = artifact with { Content = artifact.Content.TrimEnd() + route, TemplateVersion = ContentTemplateVersion };
        }

        // Explicit host entry points; divergent existing files go through normal collision handling.
        Add("guidance.host.agents", "AGENTS.md", "# Repository agent guidance\n\nRead `.github/instructions/cis-repository.instructions.md` before repository work." + route);
        Add("guidance.host.claude", "CLAUDE.md", "# Repository agent guidance\n\nRead `AGENTS.md` and `.github/instructions/cis-repository.instructions.md` before repository work." + route);

        var matrixIndex = artifacts.FindIndex(item => item.Definition == "reference.standards-conformance-matrix");
        if (matrixIndex >= 0)
        {
            var matrix = artifacts[matrixIndex];
            var rows = Regex.Matches(standard, @"\*\*(HC-[A-Z]+-\d+)\*\*", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                .Select(match => $"| {repositoryId}:standard:human-readable-content | {match.Groups[1].Value} | human-facing content | manual-review | source-aware content review and separate reader check | Draft | Integration checks do not prove meaning equivalence. |");
            artifacts[matrixIndex] = matrix with { Content = matrix.Content.TrimEnd() + "\n" + string.Join('\n', rows) + "\n", TemplateVersion = ContentTemplateVersion };
        }
    }
}
