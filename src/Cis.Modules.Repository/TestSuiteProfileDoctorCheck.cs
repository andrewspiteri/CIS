using Cis.Abstractions;

namespace Cis.Modules.Repository;

public sealed class TestSuiteProfileDoctorCheck : ICisRepositoryDoctorCheck
{
    private static readonly HashSet<string> Formats = new(StringComparer.OrdinalIgnoreCase)
    {
        "junit", "cucumber-junit", "vitest-json", "playwright-json", "trx", "stryker-json",
    };

    public string Name => "test-suite-profile";

    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
    {
        var path = Path.Combine(context.DocumentationPath, "references", "test-suite-profile.md");
        if (!File.Exists(path))
            return [Finding("CIS-TEST-DOCTOR-001", "error", "The canonical test-suite profile is missing.", [Relative(context, path)], "Rerun repository initialization and review the classification-selected suite profile.")];

        var findings = new List<CisRepositoryDoctorFinding>();
        var rows = ReadRows(path);
        if (rows.Count == 0)
            findings.Add(Finding("CIS-TEST-DOCTOR-002", "error", "The test-suite profile contains no readable suite rows.", [Relative(context, path)], "Correct the Markdown table or rerun repository initialization."));
        foreach (var row in rows)
        {
            if (!Formats.Contains(row.Format))
                findings.Add(Finding("CIS-TEST-DOCTOR-003", "error", $"Suite '{row.Id}' declares unsupported result format '{row.Format}'.", [row.Id, row.Format], "Select a registered CIS result adapter or install a module that provides one."));
            if (!InsideLocal(row.ResultPath) || (!Dash(row.CoveragePath) && !InsideLocal(row.CoveragePath)) || (!Dash(row.MutationPath) && !InsideLocal(row.MutationPath)))
                findings.Add(Finding("CIS-TEST-DOCTOR-004", "error", $"Suite '{row.Id}' writes derived evidence outside .cis/local/.", [row.ResultPath, row.CoveragePath, row.MutationPath], "Move all generated test evidence under .cis/local/testing/."));
            if (row.Framework.Equals("dotnet-test", StringComparison.OrdinalIgnoreCase) && !IsDotnetTestCommand(row.Command))
                findings.Add(Finding("CIS-TEST-DOCTOR-005", "warning", $"Suite '{row.Id}' framework and command disagree.", [row.Framework, row.Command], "Review the command and repository classification; preserve an intentional override with rationale."));
            var working = Resolve(context.RepositoryPath, row.WorkingDirectory);
            if (working is null || !Directory.Exists(working))
                findings.Add(Finding("CIS-TEST-DOCTOR-006", "error", $"Suite '{row.Id}' has an invalid working directory.", [row.WorkingDirectory], "Use a repository-relative existing directory."));
        }

        var classification = new RepositoryClassifier().Classify(context.RepositoryPath);
        var components = classification.Components
            .SelectMany(item => new[] { item.Id, item.Root })
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in rows.Where(row => !row.Component.Equals("repository", StringComparison.OrdinalIgnoreCase) && !components.Contains(row.Component)))
            findings.Add(Finding("CIS-TEST-DOCTOR-007", "warning", $"Suite '{stale.Id}' references component '{stale.Component}', which is absent from the current classification.", [stale.Component], "Rerun initialization, then reconcile the reviewed suite profile without discarding human-owned commands."));
        return findings;
    }

    private static List<Row> ReadRows(string path)
    {
        var rows = new List<Row>(); Dictionary<string, int>? columns = null;
        foreach (var line in File.ReadLines(path).Where(item => item.TrimStart().StartsWith('|')))
        {
            var cells = line.Trim().Trim('|').Split('|').Select(item => item.Trim()).ToArray();
            if (cells.FirstOrDefault()?.Equals("Suite ID", StringComparison.OrdinalIgnoreCase) == true)
            {
                columns = cells.Select((name, index) => (name, index)).ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
                continue;
            }
            if (columns is null || cells.All(item => item.All(character => character is '-' or ':' or ' '))) continue;
            string Get(string name, string fallback = "-") => columns.TryGetValue(name, out var index) && index < cells.Length ? cells[index] : fallback;
            rows.Add(new(Get("Suite ID"), Get("Component"), Get("Framework"), Get("Command"), Get("Working directory", "."), Get("Result format"), Get("Result path"), Get("Coverage path"), Get("Mutation path")));
        }
        return rows;
    }

    private static string? Resolve(string repository, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return null;
        return CisPathSafety.TryResolveUnderRoot(repository, relative, out var full, allowRoot: relative.Trim() == ".")
               && !CisPathSafety.ContainsReparsePoint(repository, full) ? full : null;
    }
    private static bool InsideLocal(string path) => path.Replace('\\', '/').StartsWith(".cis/local/", StringComparison.OrdinalIgnoreCase);
    private static bool IsDotnetTestCommand(string command)
    {
        var normalized = command.Replace('\\', '/').Trim();
        return normalized.StartsWith("dotnet test", StringComparison.OrdinalIgnoreCase)
               || ((normalized.StartsWith("pwsh ", StringComparison.OrdinalIgnoreCase)
                    || normalized.StartsWith("powershell ", StringComparison.OrdinalIgnoreCase))
                   && normalized.Contains("run-dotnet-tests.ps1", StringComparison.OrdinalIgnoreCase));
    }
    private static bool Dash(string path) => string.IsNullOrWhiteSpace(path) || path == "-";
    private static string Relative(CisRepositoryContext context, string path) => Path.GetRelativePath(context.RepositoryPath, path).Replace('\\', '/');
    private static CisRepositoryDoctorFinding Finding(string code, string severity, string message, IReadOnlyList<string> evidence, string fix)
        => new(code, severity, "testing", message, evidence, fix, "cis test validate --strict", "review-required");
    private sealed record Row(string Id, string Component, string Framework, string Command, string WorkingDirectory, string Format, string ResultPath, string CoveragePath, string MutationPath);
}
