using Cis.Abstractions;
using System.Text.RegularExpressions;

namespace Cis.Modules.Repository;

public sealed class SecuritySuiteProfileDoctorCheck : ICisRepositoryDoctorCheck
{
    private static readonly HashSet<string> Formats = new(StringComparer.OrdinalIgnoreCase)
    {
        "sarif", "semgrep-json", "gitleaks-json", "trivy-json", "zap-json",
    };

    public string Name => "security-suite-profile";

    public IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context)
    {
        var profile = Path.Combine(context.DocumentationPath, "references", "security-suite-profile.md");
        var acceptances = Path.Combine(context.DocumentationPath, "references", "accepted-security-findings.md");
        var findings = new List<CisRepositoryDoctorFinding>();
        if (!File.Exists(profile))
            findings.Add(Finding("CIS-SEC-DOCTOR-001", "error", "The canonical security-suite profile is missing.", [Relative(context, profile)], "Rerun repository initialization and review the classification-selected scanners."));
        if (!File.Exists(acceptances))
            findings.Add(Finding("CIS-SEC-DOCTOR-002", "error", "The accepted-security-findings registry is missing.", [Relative(context, acceptances)], "Rerun repository initialization; preserve the empty canonical registry when no exceptions exist."));
        if (!File.Exists(profile)) return findings;

        var rows = ReadRows(profile);
        if (rows.Count == 0)
            findings.Add(Finding("CIS-SEC-DOCTOR-003", "error", "The security-suite profile contains no readable suites.", [Relative(context, profile)], "Correct the Markdown table or rerun repository initialization."));
        foreach (var row in rows)
        {
            if (!Formats.Contains(row.Format))
                findings.Add(Finding("CIS-SEC-DOCTOR-004", "error", $"Suite '{row.Id}' declares unsupported result format '{row.Format}'.", [row.Id, row.Format], "Select a registered CIS security adapter."));
            if (!row.ResultPath.Replace('\\', '/').StartsWith(".cis/local/security/", StringComparison.OrdinalIgnoreCase))
                findings.Add(Finding("CIS-SEC-DOCTOR-005", "error", $"Suite '{row.Id}' writes evidence outside .cis/local/security/.", [row.ResultPath], "Move generated security evidence under .cis/local/security/."));
            var working = Resolve(context.RepositoryPath, row.WorkingDirectory);
            if (working is null || !Directory.Exists(working))
                findings.Add(Finding("CIS-SEC-DOCTOR-006", "error", $"Suite '{row.Id}' has an invalid working directory.", [row.WorkingDirectory], "Use an existing repository-relative directory."));
            if (!CommandMatchesTool(row.Tool, row.Command))
                findings.Add(Finding("CIS-SEC-DOCTOR-007", "warning", $"Suite '{row.Id}' tool and command disagree.", [row.Tool, row.Command], "Review the intentional override or correct the command."));
        }
        findings.AddRange(InspectWorkflowActionPins(context));
        findings.AddRange(InspectContainerImagePins(context));
        return findings;
    }

    private static IReadOnlyList<CisRepositoryDoctorFinding> InspectWorkflowActionPins(CisRepositoryContext context)
    {
        var workflowRoot = Path.Combine(context.RepositoryPath, ".github", "workflows");
        if (!Directory.Exists(workflowRoot)) return [];
        var mutable = new List<string>();
        foreach (var path in Directory.EnumerateFiles(workflowRoot, "*.y*ml", SearchOption.TopDirectoryOnly))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                var match = Regex.Match(line, @"\buses:\s*(?<action>[^\s#]+)@(?<reference>[^\s#]+)", RegexOptions.IgnoreCase);
                if (!match.Success || match.Groups["action"].Value.StartsWith("./", StringComparison.Ordinal)) continue;
                if (!Regex.IsMatch(match.Groups["reference"].Value, "^[0-9a-f]{40}$", RegexOptions.IgnoreCase))
                    mutable.Add($"{Relative(context, path)}:{lineNumber} {match.Value.Trim()}");
            }
        }
        return mutable.Count == 0 ? [] :
        [Finding("CIS-SEC-DOCTOR-008", "error", "External workflow actions use mutable references.", mutable, "Resolve the intended release tag to its verified full commit SHA and retain the release tag as a comment.")];
    }

    private static IReadOnlyList<CisRepositoryDoctorFinding> InspectContainerImagePins(CisRepositoryContext context)
    {
        var script = Path.Combine(context.RepositoryPath, "scripts", "run-security-scan.mjs");
        var mutable = new List<string>();
        if (File.Exists(script))
            mutable.AddRange(Regex.Matches(File.ReadAllText(script), "(?:semgrep/semgrep|gitleaks/gitleaks|aquasec/trivy|aquasecurity/trivy|zaproxy/zap-[\\w-]+):[^\\s\\\"']+")
                .Select(match => match.Value));
        var workflowRoot = Path.Combine(context.RepositoryPath, ".github", "workflows");
        if (Directory.Exists(workflowRoot))
            foreach (var path in Directory.EnumerateFiles(workflowRoot, "*.y*ml", SearchOption.TopDirectoryOnly))
            {
                var lineNumber = 0;
                foreach (var line in File.ReadLines(path))
                {
                    lineNumber++;
                    mutable.AddRange(Regex.Matches(line, "(?:semgrep/semgrep|gitleaks/gitleaks|aquasec/trivy|aquasecurity/trivy|zaproxy/zap-[\\w-]+):[^\\s\\\"']+")
                        .Select(match => $"{Relative(context, path)}:{lineNumber} {match.Value}"));
                }
            }
        foreach (var path in Directory.EnumerateFiles(context.RepositoryPath, "Dockerfile*", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                         && !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                         && !path.Contains($"{Path.DirectorySeparatorChar}.stryker-tmp{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                         && !path.Contains($"{Path.DirectorySeparatorChar}.cis{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                var match = Regex.Match(line, @"^\s*FROM\s+(?<image>[^\s]+)", RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                var image = match.Groups["image"].Value;
                if (!image.Equals("scratch", StringComparison.OrdinalIgnoreCase) && !image.Contains("@sha256:", StringComparison.OrdinalIgnoreCase))
                    mutable.Add($"{Relative(context, path)}:{lineNumber} {image}");
            }
        }
        var evidence = mutable.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return evidence.Length == 0 ? [] :
        [Finding("CIS-SEC-DOCTOR-009", "error", "Container or security-scanner images use mutable tag references.", evidence, "Pin every base and scanner container image to its verified registry digest.")];
    }
    private static bool CommandMatchesTool(string tool, string command)
    {
        if (command.Contains(tool, StringComparison.OrdinalIgnoreCase)) return true;
        return tool.ToLowerInvariant() switch
        {
            "semgrep" => command.Contains("security:sast", StringComparison.OrdinalIgnoreCase),
            "gitleaks" => command.Contains("security:secrets", StringComparison.OrdinalIgnoreCase),
            "trivy" => command.Contains("security:filesystem", StringComparison.OrdinalIgnoreCase)
                || command.Contains("security:configuration", StringComparison.OrdinalIgnoreCase)
                || command.Contains("security:image", StringComparison.OrdinalIgnoreCase),
            "zap" => command.Contains("security:dast", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
    }

    private static List<Row> ReadRows(string path)
    {
        var rows = new List<Row>(); Dictionary<string, int>? columns = null;
        foreach (var line in File.ReadLines(path).Where(item => item.TrimStart().StartsWith('|')))
        {
            var cells = line.Trim().Trim('|').Split('|').Select(item => item.Trim()).ToArray();
            if (cells.FirstOrDefault()?.Equals("Suite ID", StringComparison.OrdinalIgnoreCase) == true)
            { columns = cells.Select((name, index) => (name, index)).ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase); continue; }
            if (columns is null || cells.All(item => item.All(character => character is '-' or ':' or ' '))) continue;
            string Get(string name, string fallback = "-") => columns.TryGetValue(name, out var index) && index < cells.Length ? cells[index] : fallback;
            rows.Add(new(Get("Suite ID"), Get("Tool"), Get("Command"), Get("Working directory", "."), Get("Result format"), Get("Result path")));
        }
        return rows;
    }

    private static string? Resolve(string repository, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return null;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository));
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
    private static string Relative(CisRepositoryContext context, string path) => Path.GetRelativePath(context.RepositoryPath, path).Replace('\\', '/');
    private static CisRepositoryDoctorFinding Finding(string code, string severity, string message, IReadOnlyList<string> evidence, string fix)
        => new(code, severity, "security", message, evidence, fix, "cis security validate --strict", "review-required");
    private sealed record Row(string Id, string Tool, string Command, string WorkingDirectory, string Format, string ResultPath);
}
