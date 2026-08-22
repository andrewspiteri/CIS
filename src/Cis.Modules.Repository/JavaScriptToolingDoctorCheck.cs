using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

public sealed partial class JavaScriptToolingDoctorCheck : ICisRepositoryDoctorCheck
{
    private static readonly string[] FlatConfigNames =
        ["eslint.config.js", "eslint.config.mjs", "eslint.config.cjs", "eslint.config.ts", "eslint.config.mts", "eslint.config.cts"];

    public string Name => "javascript-tooling";

    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
    {
        var packagePath = Path.Combine(context.RepositoryPath, "package.json");
        if (!File.Exists(packagePath)) return [];

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packagePath));
            var root = document.RootElement;
            if (!UsesEslintLintScript(root) || ReadEslintMajor(root) is not >= 9) return [];
            if (FlatConfigNames.Any(name => File.Exists(Path.Combine(context.RepositoryPath, name)))) return [];

            return [new CisRepositoryDoctorFinding(
                "CIS-JS-DOCTOR-001",
                "warning",
                "javascript-tooling",
                "The root package declares an ESLint 9+ lint script but has no flat configuration file.",
                ["package.json", .. FlatConfigNames.Select(name => "missing: " + name)],
                "Add a reviewed ESLint flat configuration and the parser/config package required by the repository language, then run the lint script.",
                null,
                "manual")];
        }
        catch (JsonException exception)
        {
            return [new CisRepositoryDoctorFinding(
                "CIS-JS-DOCTOR-002",
                "warning",
                "javascript-tooling",
                $"The root package manifest could not be inspected: {exception.Message}",
                ["package.json"],
                "Correct package.json and rerun repository doctor.",
                null,
                "manual")];
        }
    }

    private static bool UsesEslintLintScript(JsonElement root)
        => root.TryGetProperty("scripts", out var scripts)
           && scripts.ValueKind == JsonValueKind.Object
           && scripts.TryGetProperty("lint", out var lint)
           && lint.ValueKind == JsonValueKind.String
           && EslintCommand().IsMatch(lint.GetString() ?? string.Empty);

    private static int? ReadEslintMajor(JsonElement root)
    {
        foreach (var groupName in new[] { "devDependencies", "dependencies" })
        {
            if (!root.TryGetProperty(groupName, out var group)
                || group.ValueKind != JsonValueKind.Object
                || !group.TryGetProperty("eslint", out var version)
                || version.ValueKind != JsonValueKind.String) continue;
            var match = VersionNumber().Match(version.GetString() ?? string.Empty);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var major)) return major;
        }
        return null;
    }

    [GeneratedRegex(@"(?:^|[\s;&|])(?:pnpm\s+exec\s+|npm\s+exec\s+|npx\s+)?eslint(?:\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex EslintCommand();

    [GeneratedRegex(@"(\d+)")]
    private static partial Regex VersionNumber();
}
