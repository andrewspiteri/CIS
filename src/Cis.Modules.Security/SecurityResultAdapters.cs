using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Security;

internal static partial class SecurityEvidence
{
    public static SecuritySuiteExecution Build(SecurityResultAdapterContext context, IReadOnlyList<SecurityFinding> findings)
    {
        var fail = context.Suite.FailSeverities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blocking = findings.Any(item => fail.Contains(item.Severity));
        return new(context.Suite.Id, context.Suite.Category, context.Suite.Tool,
            blocking ? "findings" : findings.Count > 0 ? "passed-with-findings" : "passed",
            blocking ? SecurityFailureKind.Finding : SecurityFailureKind.None,
            findings.Count(item => item.Severity == "critical"), findings.Count(item => item.Severity == "high"),
            findings.Count(item => item.Severity == "medium"), findings.Count(item => item.Severity is "low" or "info"),
            0, findings, [Artifact(context.RepositoryPath, "scanner-result", context.ResultPath)], []);
    }

    public static SecurityFinding Finding(SecurityResultAdapterContext context, string rule, string severity,
        string message, string path, int? line, string? fingerprint = null)
    {
        rule = string.IsNullOrWhiteSpace(rule) ? "unknown-rule" : rule.Trim();
        path = NormalizePath(context.RepositoryPath, path);
        fingerprint = string.IsNullOrWhiteSpace(fingerprint)
            ? Hash($"{context.Suite.Tool}\n{rule}\n{path}\n{line}\n{message}") : fingerprint.Trim();
        var id = "SEC-" + Hash($"{context.Suite.Tool}\n{rule}\n{path}\n{line}\n{fingerprint}")[..16].ToUpperInvariant();
        return new(id, rule, context.Suite.Tool, context.Suite.Category, NormalizeSeverity(severity),
            Redact(message), path, line, fingerprint);
    }

    public static SecurityArtifact Artifact(string repository, string kind, string path)
    {
        using var stream = File.OpenRead(path);
        return new(kind, Path.GetRelativePath(repository, path).Replace('\\', '/'),
            "sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream)), stream.Length);
    }

    public static string NormalizeSeverity(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (double.TryParse(normalized, System.Globalization.CultureInfo.InvariantCulture, out var score))
            return score >= 9 ? "critical" : score >= 7 ? "high" : score >= 4 ? "medium" : "low";
        return normalized switch
        {
            "critical" => "critical",
            "high" or "error" => "high",
            "medium" or "moderate" or "warning" or "warn" => "medium",
            "low" or "note" or "info" or "informational" => "low",
            _ => "medium",
        };
    }

    public static string Redact(string? value)
    {
        var text = (value ?? "Security scanner finding.").Replace('\r', ' ').Replace('\n', ' ').Trim();
        text = SecretAssignment().Replace(text, "$1[REDACTED]");
        text = Bearer().Replace(text, "Bearer [REDACTED]");
        text = UriCredential().Replace(text, "$1[REDACTED]@$2");
        return text.Length <= 500 ? text : text[..497] + "...";
    }

    private static string NormalizePath(string repository, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        try
        {
            var full = Path.IsPathRooted(value) ? Path.GetFullPath(value) : Path.GetFullPath(Path.Combine(repository, value));
            var root = Path.GetFullPath(repository);
            return CisPathSafety.IsUnderRoot(root, full)
                ? Path.GetRelativePath(root, full).Replace('\\', '/') : Path.GetFileName(value);
        }
        catch { return value.Replace('\\', '/'); }
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [GeneratedRegex(@"(?i)\b(password|secret|token|api[_-]?key|authorization|cookie|private[_-]?key)\b\s*[:=]\s*([^\s,;]+)")]
    private static partial Regex SecretAssignment();
    [GeneratedRegex(@"(?i)Bearer\s+[A-Za-z0-9._~+/-]+=*")]
    private static partial Regex Bearer();
    [GeneratedRegex(@"(?i)(https?://[^:/\s]+:)[^@\s]+@([^/\s]+)")]
    private static partial Regex UriCredential();
}

public sealed class SarifSecurityResultAdapter : ICisSecurityResultAdapter
{
    public string Format => "sarif";
    public SecuritySuiteExecution Read(SecurityResultAdapterContext context)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(context.ResultPath));
        var findings = new List<SecurityFinding>();
        foreach (var run in document.RootElement.GetProperty("runs").EnumerateArray())
        {
            var ruleSeverities = new Dictionary<string, string>(StringComparer.Ordinal);
            if (run.TryGetProperty("tool", out var tool) && tool.TryGetProperty("driver", out var driver)
                && driver.TryGetProperty("rules", out var rules) && rules.ValueKind == JsonValueKind.Array)
                foreach (var descriptor in rules.EnumerateArray())
                {
                    var id = Text(descriptor, "id");
                    if (id is not null && descriptor.TryGetProperty("properties", out var ruleProperties))
                    {
                        var value = Text(ruleProperties, "security-severity") ?? Text(ruleProperties, "problem.severity");
                        if (value is not null) ruleSeverities[id] = value;
                    }
                }
            foreach (var result in run.TryGetProperty("results", out var results) ? results.EnumerateArray() : [])
            {
            var rule = Text(result, "ruleId") ?? "unknown-rule";
            var severity = ruleSeverities.GetValueOrDefault(rule) ?? Text(result, "level") ?? "warning";
            if (result.TryGetProperty("properties", out var properties))
                severity = Text(properties, "security-severity") ?? Text(properties, "problem.severity") ?? severity;
            var message = result.TryGetProperty("message", out var messageNode) ? Text(messageNode, "text") ?? rule : rule;
            var path = "unknown"; int? line = null;
            if (result.TryGetProperty("locations", out var locations) && locations.GetArrayLength() > 0)
            {
                var physical = locations[0].GetProperty("physicalLocation");
                if (physical.TryGetProperty("artifactLocation", out var artifact)) path = Text(artifact, "uri") ?? path;
                if (physical.TryGetProperty("region", out var region) && region.TryGetProperty("startLine", out var start)) line = start.GetInt32();
            }
            findings.Add(SecurityEvidence.Finding(context, rule, severity, message, path, line,
                result.TryGetProperty("partialFingerprints", out var fps) ? fps.EnumerateObject().Select(item => item.Value.ToString()).FirstOrDefault() : null));
            }
        }
        return SecurityEvidence.Build(context, findings);
    }
    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

public sealed class SemgrepJsonSecurityResultAdapter : ICisSecurityResultAdapter
{
    public string Format => "semgrep-json";
    public SecuritySuiteExecution Read(SecurityResultAdapterContext context)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(context.ResultPath));
        var findings = new List<SecurityFinding>();
        foreach (var result in document.RootElement.TryGetProperty("results", out var results) ? results.EnumerateArray() : [])
        {
            var extra = result.GetProperty("extra");
            findings.Add(SecurityEvidence.Finding(context, result.GetProperty("check_id").GetString() ?? "unknown-rule",
                extra.TryGetProperty("severity", out var severity) ? severity.GetString() ?? "warning" : "warning",
                extra.TryGetProperty("message", out var message) ? message.GetString() ?? "Semgrep finding." : "Semgrep finding.",
                result.TryGetProperty("path", out var path) ? path.GetString() ?? "unknown" : "unknown",
                result.TryGetProperty("start", out var start) && start.TryGetProperty("line", out var line) ? line.GetInt32() : null));
        }
        return SecurityEvidence.Build(context, findings);
    }
}

public sealed class GitleaksJsonSecurityResultAdapter : ICisSecurityResultAdapter
{
    public string Format => "gitleaks-json";
    public SecuritySuiteExecution Read(SecurityResultAdapterContext context)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(context.ResultPath));
        var findings = new List<SecurityFinding>();
        foreach (var result in document.RootElement.EnumerateArray())
        {
            var rule = Property(result, "RuleID") ?? "secret-like-value";
            findings.Add(SecurityEvidence.Finding(context, rule, "high", $"Potential secret-like value detected by rule {rule}.",
                Property(result, "File") ?? "unknown", Number(result, "StartLine"), Property(result, "Fingerprint")));
        }
        return SecurityEvidence.Build(context, findings);
    }
    private static string? Property(JsonElement value, string name) => value.TryGetProperty(name, out var property) ? property.GetString() : null;
    private static int? Number(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.TryGetInt32(out var number) ? number : null;
}

public sealed class TrivyJsonSecurityResultAdapter : ICisSecurityResultAdapter
{
    public string Format => "trivy-json";
    public SecuritySuiteExecution Read(SecurityResultAdapterContext context)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(context.ResultPath));
        var findings = new List<SecurityFinding>();
        foreach (var result in document.RootElement.TryGetProperty("Results", out var results) ? results.EnumerateArray() : [])
        {
            var target = result.TryGetProperty("Target", out var targetNode) ? targetNode.GetString() ?? context.Suite.Target : context.Suite.Target;
            Add(result, "Vulnerabilities", "VulnerabilityID", "Title", "PkgName", target);
            Add(result, "Misconfigurations", "ID", "Title", "CauseMetadata", target);
            Add(result, "Secrets", "RuleID", "Title", "Category", target, secret: true);
        }
        return SecurityEvidence.Build(context, findings);

        void Add(JsonElement result, string collection, string id, string title, string pathProperty, string target, bool secret = false)
        {
            if (!result.TryGetProperty(collection, out var values) || values.ValueKind != JsonValueKind.Array) return;
            foreach (var item in values.EnumerateArray())
            {
                var rule = item.TryGetProperty(id, out var idNode) ? idNode.ToString() : collection;
                var message = secret ? $"Potential secret-like value detected by Trivy rule {rule}."
                    : item.TryGetProperty(title, out var titleNode) ? titleNode.ToString() : rule;
                var path = target;
                if (item.TryGetProperty(pathProperty, out var pathNode) && pathNode.ValueKind == JsonValueKind.String) path = pathNode.GetString() ?? target;
                var line = item.TryGetProperty("StartLine", out var lineNode) && lineNode.TryGetInt32(out var parsed) ? parsed : (int?)null;
                var severity = item.TryGetProperty("Severity", out var severityNode) ? severityNode.ToString() : secret ? "high" : "medium";
                findings.Add(SecurityEvidence.Finding(context, rule, severity, message, path, line));
            }
        }
    }
}

public sealed class ZapJsonSecurityResultAdapter : ICisSecurityResultAdapter
{
    public string Format => "zap-json";
    public SecuritySuiteExecution Read(SecurityResultAdapterContext context)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(context.ResultPath));
        var findings = new List<SecurityFinding>();
        foreach (var site in document.RootElement.TryGetProperty("site", out var sites) ? sites.EnumerateArray() : [])
        foreach (var alert in site.TryGetProperty("alerts", out var alerts) ? alerts.EnumerateArray() : [])
        {
            var rule = alert.TryGetProperty("pluginid", out var plugin) ? plugin.ToString() : "zap-alert";
            var severity = alert.TryGetProperty("riskcode", out var risk) ? risk.ToString() switch { "3" => "high", "2" => "medium", "1" => "low", _ => "info" } : "medium";
            var message = alert.TryGetProperty("name", out var name) ? name.ToString()
                : alert.TryGetProperty("alert", out var alertName) ? alertName.ToString() : "ZAP finding.";
            var path = context.Suite.Target;
            if (alert.TryGetProperty("instances", out var instances) && instances.ValueKind == JsonValueKind.Array && instances.GetArrayLength() > 0
                && instances[0].TryGetProperty("uri", out var uri)) path = uri.ToString();
            findings.Add(SecurityEvidence.Finding(context, rule, severity, message, path, null));
        }
        return SecurityEvidence.Build(context, findings);
    }
}
