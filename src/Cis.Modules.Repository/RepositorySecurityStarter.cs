using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

internal sealed record RepositorySecurityStarterContent(string Profile, IReadOnlyList<RepositorySecurityStep> Steps);
internal sealed record RepositorySecurityStep(string Id, string Command, string WorkingDirectory, string SuiteId, string DependsOn, int TimeoutSeconds);

internal static class RepositorySecurityStarter
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".cis", ".codex-tmp", ".next", ".terraform", ".artifacts",
        "artifacts", "bin", "coverage", "dist", "node_modules", "obj", "out"
    };

    public static RepositorySecurityStarterContent Create(string repositoryPath, RepositoryClassification classification)
    {
        var suites = new List<Suite>();
        var steps = new List<RepositorySecurityStep>();
        var hasCode = classification.Components.Any(component => component.Languages.Any(language =>
            language is "csharp" or "typescript" or "javascript" or "swift" or "kotlin" or "gdscript" or "hcl"));
        var hasContainers = classification.Components.Any(component => component.Frameworks.Contains("docker-compose", StringComparer.Ordinal))
            || EnumerateDockerfiles(repositoryPath).Any();
        var hasConfiguration = classification.Components.Any(component => component.Frameworks.Any(framework => framework is "docker-compose" or "terraform"));

        if (hasCode)
            Add("repository-sast", "repository", "sast", "semgrep", "semgrep scan --config auto --json --output .cis/local/security/results/semgrep.json .",
                "semgrep-json", ".cis/local/security/results/semgrep.json", ".", "source code is present", "pr", "critical,high", 1800);

        Add("repository-secrets", "repository", "secret", "gitleaks", "gitleaks detect --source . --redact --report-format json --report-path .cis/local/security/results/gitleaks.json --exit-code 0",
            "gitleaks-json", ".cis/local/security/results/gitleaks.json", ".", "always", "pr", "critical,high,medium,low", 900);

        if (hasCode)
            Add("repository-filesystem", "repository", "filesystem", "trivy", "trivy fs --format json --output .cis/local/security/results/trivy-fs.json --scanners vuln .",
                "trivy-json", ".cis/local/security/results/trivy-fs.json", ".", "dependency manifests are present", "pr", "critical,high", 1800);

        if (hasConfiguration)
            Add("repository-configuration", "repository", "configuration", "trivy", "trivy config --format json --output .cis/local/security/results/trivy-config.json .",
                "trivy-json", ".cis/local/security/results/trivy-config.json", ".", "Compose or Terraform configuration is present", "pr", "critical,high", 1800);

        var scripts = PackageScripts(repositoryPath);
        if (hasContainers && scripts.TryGetValue("security:image", out var imageScript))
            Add("repository-image", "repository", "image", "trivy", PackageRunner(repositoryPath) + " run security:image",
                "trivy-json", ".cis/local/security/results/trivy-image.json", "built image digest", "security:image package script is declared", "release", "critical,high", 3600, "repository-filesystem");

        if (scripts.TryGetValue("security:dast", out var dastScript))
            Add("repository-dast", "repository", "dast", "zap", PackageRunner(repositoryPath) + " run security:dast",
                "zap-json", ".cis/local/security/results/zap.json", "repository-managed running target", "security:dast package script is declared", "release", "critical,high", 3600);

        return new(CreateProfile(suites), steps);

        void Add(string id, string component, string category, string tool, string command, string format,
            string result, string target, string applies, string tier, string failSeverities, int timeout, string dependsOn = "-")
        {
            suites.Add(new(id, component, category, tool, command, ".", format, result, target, applies, tier, failSeverities, "retain-on-failure"));
            steps.Add(new(id, command, ".", id, dependsOn, timeout));
        }
    }

    public static string AcceptedFindings() => """
        ---
        title: "Accepted Security Findings"
        type: accepted-security-findings
        status: Active
        owner: "Repository maintainers"
        last_reviewed: "2026-08-27"
        review_cadence: "on finding acceptance, expiry, ownership, or scanner change"
        ---

        # Accepted security findings

        Acceptances are exact, temporary, human-approved exceptions. Wildcards and model-authored approvals are invalid.

        | Finding ID | Scanner | Fingerprint | Classification | Reason | Accepted until | Owner | Approved by | Approval reference |
        |---|---|---|---|---|---|---|---|---|
        """;

    private static string CreateProfile(IEnumerable<Suite> suites)
    {
        var body = string.Join(Environment.NewLine, suites.Select(item =>
            $"| {item.Id} | {item.Component} | {item.Category} | {item.Tool} | {item.Command} | {item.WorkingDirectory} | {item.ResultFormat} | {item.ResultPath} | {item.Target} | {item.AppliesWhen} | {item.CiTier} | {item.FailSeverities} | {item.Artifacts} |"));
        return $$"""
        ---
        title: "Security Suite Profile"
        type: security-suite-profile
        status: Active
        owner: "Repository maintainers"
        last_reviewed: "2026-08-27"
        review_cadence: "on classification, scanner, command, policy, target, or CI-tier change"
        ---

        # Security suite profile

        Scanner results are derived evidence under `.cis/local/security/`. Deterministic scanner results and governed exceptions decide the verdict. Local AI summaries are advisory and receive only normalized, redacted findings.

        | Suite ID | Component | Category | Tool | Command | Working directory | Result format | Result path | Target | Applies when | CI tier | Fail severities | Artifacts |
        |---|---|---|---|---|---|---|---|---|---|---|---|---|
        {{body}}
        """;
    }

    private static IReadOnlyDictionary<string, string> PackageScripts(string repositoryPath)
    {
        var package = Path.Combine(repositoryPath, "package.json");
        if (!File.Exists(package)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(File.ReadAllText(package));
        return document.RootElement.TryGetProperty("scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Object
            ? scripts.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string PackageRunner(string repositoryPath) => File.Exists(Path.Combine(repositoryPath, "pnpm-lock.yaml")) ? "corepack pnpm"
        : File.Exists(Path.Combine(repositoryPath, "yarn.lock")) ? "corepack yarn" : "npm";
    internal static IEnumerable<string> EnumerateDockerfiles(string repositoryPath)
    {
        var pending = new Stack<string>();
        pending.Push(repositoryPath);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(directory, "Dockerfile*");
                directories = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
                yield return file;

            foreach (var child in directories)
            {
                if (ExcludedDirectories.Contains(Path.GetFileName(child)) || CisPathSafety.IsReparsePoint(child))
                    continue;
                pending.Push(child);
            }
        }
    }

    private sealed record Suite(string Id, string Component, string Category, string Tool, string Command,
        string WorkingDirectory, string ResultFormat, string ResultPath, string Target, string AppliesWhen,
        string CiTier, string FailSeverities, string Artifacts);
}
