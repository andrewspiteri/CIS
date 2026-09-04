using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Diagnostics;

public sealed record DiagnosticSource(string Id, string Kind, string Location, bool Enabled, bool Sensitive);
public sealed record DiagnosticEvent(string Source, string Severity, string Message, string Evidence, int Line,
    string? TimestampUtc = null, string? Category = null, string? CorrelationId = null, string? Fingerprint = null);
public sealed record DiagnosticAnalysis(string CapturedAtUtc, string InputDigest, IReadOnlyList<DiagnosticEvent> Events,
    IReadOnlyDictionary<string, int> Counts);
public sealed record DiagnosticsResult(string Status, string? RepositoryPath, IReadOnlyList<DiagnosticSource> Sources,
    IReadOnlyList<DiagnosticEvent> Events, DiagnosticAnalysis? Analysis, IReadOnlyList<string> Errors, bool Applied)
{
    public int ExitCode => Errors.Any(item => item.StartsWith("ERROR:", StringComparison.Ordinal)) ? 4 : 0;
}

public sealed class DiagnosticsService
{
    public const string AnalysisPath = ".cis/local/diagnostics/analysis.json";
    public const string ExportPath = ".cis/local/diagnostics/export/events.jsonl";
    private static readonly string[] SupportedKinds = ["text-log", "jsonl", "ndjson", "workflow-log", "browser-log", "container-log"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ICisRepositoryContextResolver _resolver;
    private readonly Func<DateTimeOffset> _clock;
    public DiagnosticsService(ICisRepositoryContextResolver resolver, Func<DateTimeOffset>? clock = null)
    { _resolver = resolver; _clock = clock ?? (() => DateTimeOffset.UtcNow); }

    public DiagnosticsResult Sources(string repo) => Read(repo, 0, null, null, null, null);
    public DiagnosticsResult Summary(string repo) => Read(repo, 500, null, null, null, null);
    public DiagnosticsResult Events(string repo, string? source, int limit) => Read(repo, Math.Clamp(limit, 1, 5000), source, null, null, null);
    public DiagnosticsResult Events(string repo, string? source, int limit, string? level, string? contains, int? sinceMinutes)
        => Read(repo, Math.Clamp(limit, 1, 5000), source, level, contains, sinceMinutes);
    public DiagnosticsResult Tail(string repo, string source, int lines) => Read(repo, Math.Clamp(lines, 1, 1000), source, null, null, null);

    public DiagnosticsResult Doctor(string repo)
    {
        var result = Read(repo, 0, null, null, null, null); if (result.RepositoryPath is null) return result;
        var findings = result.Errors.ToList();
        foreach (var duplicate in result.Sources.GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            findings.Add($"ERROR: Duplicate diagnostics source ID: {duplicate.Key}");
        foreach (var source in result.Sources)
        {
            if (!SupportedKinds.Contains(source.Kind, StringComparer.OrdinalIgnoreCase)) findings.Add($"ERROR: Unsupported diagnostics kind '{source.Kind}' for {source.Id}.");
            if (source.Enabled && source.Sensitive) findings.Add($"ERROR: Sensitive source '{source.Id}' cannot be enabled directly; configure a sanitized export.");
            if (source.Enabled && (!TrySourcePath(result.RepositoryPath, source.Location, out var sourcePath)
                                   || !File.Exists(sourcePath)))
                findings.Add($"WARNING: Enabled diagnostics source does not exist yet: {source.Id} ({source.Location})");
        }
        return result with { Status = findings.Count == 0 ? "healthy" : "findings", Errors = findings };
    }

    public DiagnosticsResult Analyse(string repo)
    {
        var result = Read(repo, 5000, null, null, null, null);
        if (result.RepositoryPath is null || result.ExitCode != 0) return result;
        var digest = Sha(string.Join("\n", result.Events.Select(item => $"{item.Source}|{item.Severity}|{item.Fingerprint}")));
        var counts = result.Events.GroupBy(item => item.Severity, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => "severity:" + group.Key.ToLowerInvariant(), group => group.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var group in result.Events.GroupBy(item => item.Fingerprint).Where(group => !string.IsNullOrWhiteSpace(group.Key))) counts["fingerprint:" + group.Key] = group.Count();
        var analysis = new DiagnosticAnalysis(_clock().ToUniversalTime().ToString("O"), digest, result.Events, counts);
        var path = Path.Combine(result.RepositoryPath, AnalysisPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); Write(path, JsonSerializer.Serialize(analysis, JsonOptions) + Environment.NewLine);
        return result with { Status = "analysed", Analysis = analysis, Applied = true };
    }

    public DiagnosticsResult Export(string repo)
    {
        var result = Read(repo, 5000, null, null, null, null); if (result.RepositoryPath is null || result.ExitCode != 0) return result;
        var path = Path.Combine(result.RepositoryPath, ExportPath.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = string.Join(Environment.NewLine, result.Events.Select(item => JsonSerializer.Serialize(item))) + (result.Events.Count > 0 ? Environment.NewLine : string.Empty);
        Write(path, content); return result with { Status = "exported", Applied = true };
    }

    private DiagnosticsResult Read(string repo, int limit, string? selected, string? level, string? contains, int? sinceMinutes)
    {
        var resolution = _resolver.Resolve(repo);
        if (!resolution.IsSuccess || resolution.Context is null) return new("invalid-repository", null, [], [], null, resolution.Errors.Select(item => "ERROR: " + item).ToArray(), false);
        var context = resolution.Context; var sources = Profile(context); var events = new List<DiagnosticEvent>(); var errors = new List<string>();
        var cutoff = sinceMinutes is > 0 ? _clock().ToUniversalTime().AddMinutes(-sinceMinutes.Value) : (DateTimeOffset?)null;
        foreach (var source in sources.Where(item => item.Enabled && (selected is null || item.Id.Equals(selected, StringComparison.OrdinalIgnoreCase))))
        {
            if (source.Sensitive) { errors.Add($"ERROR: Source '{source.Id}' is marked sensitive and requires a redacted export before CIS can read it."); continue; }
            if (!TrySourcePath(context.RepositoryPath, source.Location, out var path))
            { errors.Add($"ERROR: Source '{source.Id}' escapes the repository or traverses a linked directory."); continue; }
            if (!File.Exists(path)) continue;
            var all = File.ReadLines(path).Select((text, index) => (text, line: index + 1)).ToArray();
            foreach (var entry in (limit == 0 ? [] : all.TakeLast(limit)))
            {
                var parsed = Parse(source, entry.text, entry.line);
                if (level is not null && !parsed.Severity.Equals(level, StringComparison.OrdinalIgnoreCase)) continue;
                if (contains is not null && !parsed.Message.Contains(contains, StringComparison.OrdinalIgnoreCase)) continue;
                if (cutoff is not null && DateTimeOffset.TryParse(parsed.TimestampUtc, out var timestamp) && timestamp < cutoff) continue;
                events.Add(parsed);
            }
        }
        if (selected is not null && !sources.Any(item => item.Id.Equals(selected, StringComparison.OrdinalIgnoreCase))) errors.Add($"ERROR: Unknown diagnostics source: {selected}");
        DiagnosticAnalysis? analysis = null; var analysisPath = Path.Combine(context.RepositoryPath, AnalysisPath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(analysisPath)) try { analysis = JsonSerializer.Deserialize<DiagnosticAnalysis>(File.ReadAllText(analysisPath), JsonOptions); } catch (JsonException) { errors.Add("WARNING: Stored diagnostics analysis is invalid; rerun analyse."); }
        return new(errors.Count == 0 ? "available" : "findings", context.RepositoryPath, sources, events, analysis, errors, false);
    }

    private static DiagnosticEvent Parse(DiagnosticSource source, string raw, int line)
    {
        var message = raw; string? timestamp = null, category = null, correlation = null, severity = null;
        if (source.Kind is "jsonl" or "ndjson")
        {
            try
            {
                using var document = JsonDocument.Parse(raw); var root = document.RootElement;
                message = Get(root, "message", "msg", "event") ?? raw; timestamp = Get(root, "timestamp", "time", "timestampUtc", "@t");
                category = Get(root, "category", "eventName", "sourceContext", "logger"); correlation = Get(root, "correlationId", "traceId", "requestId"); severity = Get(root, "severity", "level", "logLevel", "@l");
            }
            catch (JsonException) { message = "Malformed JSONL: " + raw; severity = "error"; }
        }
        severity = NormalizeSeverity(severity ?? InferSeverity(message)); message = Redact(message);
        return new(source.Id, severity, message, $"{source.Location}:{line}", line, timestamp, category,
            correlation is null ? null : Redact(correlation), Sha(NormalizeForFingerprint(message))[..16]);
    }
    private static string? Get(JsonElement root, params string[] names)
    { foreach (var name in names) if (root.TryGetProperty(name, out var value)) return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString(); return null; }
    private static string InferSeverity(string line) => line.Contains("exception", StringComparison.OrdinalIgnoreCase) || line.Contains("error", StringComparison.OrdinalIgnoreCase) ? "error" : line.Contains("warn", StringComparison.OrdinalIgnoreCase) || line.Contains("timeout", StringComparison.OrdinalIgnoreCase) ? "warning" : "info";
    private static string NormalizeSeverity(string value) => value.ToLowerInvariant() switch { "err" or "error" or "fatal" or "critical" => "error", "warn" or "warning" => "warning", _ => "info" };
    private static IReadOnlyList<DiagnosticSource> Profile(CisRepositoryContext context)
    {
        var path = Path.Combine(context.DocumentationPath, "references", "diagnostics-profile.md"); if (!File.Exists(path)) return [];
        var output = new List<DiagnosticSource>(); foreach (var line in File.ReadLines(path))
        { if (!line.TrimStart().StartsWith('|')) continue; var values = line.Trim().Trim('|').Split('|').Select(value => value.Trim()).ToArray(); if (values.Length < 5 || values[0].Equals("Source", StringComparison.OrdinalIgnoreCase) || values.All(value => value.All(character => character is '-' or ':' or ' '))) continue; output.Add(new(values[0], values[1], values[2], Yes(values[3]), Yes(values[4]))); }
        return output;
    }
    private static bool Yes(string value) => value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    private static bool TrySourcePath(string root, string relative, out string path)
        => CisPathSafety.TryResolveUnderRoot(root, relative, out path)
           && !CisPathSafety.ContainsReparsePoint(root, path);
    private static string NormalizeForFingerprint(string value) => Regex.Replace(Regex.Replace(value.ToLowerInvariant(), @"\b[0-9a-f]{8,}\b", "<id>"), @"\b\d+\b", "<n>");
    private static string Redact(string value)
    {
        value = Regex.Replace(value, @"(?i)(token|password|secret|authorization|api[_-]?key)\s*[:=]\s*([^\s,;]+)", "$1=[REDACTED]");
        value = Regex.Replace(value, @"(?i)bearer\s+[A-Za-z0-9._~+/-]+=*", "Bearer [REDACTED]");
        return value.Length > 2000 ? value[..2000] : value;
    }
    private static string Sha(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void Write(string path, string content) { var temporary = path + ".tmp"; File.WriteAllText(temporary, content); File.Move(temporary, path, true); }
}
