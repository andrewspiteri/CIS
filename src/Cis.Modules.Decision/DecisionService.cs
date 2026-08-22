using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Change;

namespace Cis.Modules.Decision;

public sealed partial class DecisionService
{
    private static readonly string[] Categories =
    [
        "architecture", "scope", "contract", "data", "security", "operations", "delivery", "implementation",
    ];

    private readonly ChangeDossierStore _changes;
    private readonly ICisRepositoryContextResolver _resolver;

    public DecisionService(ChangeDossierStore changes, ICisRepositoryContextResolver resolver)
    {
        _changes = changes;
        _resolver = resolver;
    }

    public DecisionResult List(string repositoryPath, string changeId, string? status = null)
    {
        var change = _changes.Read(repositoryPath, changeId);
        if (change is null)
        {
            return Error(changeId, $"Change dossier was not found: {changeId}");
        }

        var read = Read(change);
        if (read.Errors.Count > 0)
        {
            return new DecisionResult("invalid", change.Id, null, [], read.Errors, false);
        }

        var decisions = string.IsNullOrWhiteSpace(status)
            ? read.Decisions
            : read.Decisions.Where(item => string.Equals(item.Status, status, StringComparison.OrdinalIgnoreCase)).ToArray();
        return new DecisionResult("listed", change.Id, null, decisions, [], false);
    }

    public DecisionResult Create(DecisionCreateRequest request)
    {
        var change = _changes.Read(request.RepositoryPath, request.ChangeId);
        if (change is null)
        {
            return Error(request.ChangeId, $"Change dossier was not found: {request.ChangeId}");
        }

        var category = request.Category.Trim().ToLowerInvariant();
        if (!Categories.Contains(category, StringComparer.Ordinal))
        {
            return Error(change.Id, "Category must be architecture, scope, contract, data, security, operations, delivery, or implementation.");
        }

        var question = Clean(request.Question);
        var options = request.Options.Select(Clean).Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (question.Length == 0 || options.Length < 2 || string.IsNullOrWhiteSpace(request.Evidence))
        {
            return Error(change.Id, "A question, at least two distinct options, and evidence are required.");
        }

        var read = Read(change);
        if (read.Errors.Count > 0)
        {
            return new DecisionResult("invalid", change.Id, null, [], read.Errors, false);
        }

        var existing = read.Decisions.FirstOrDefault(item => string.Equals(item.Question, question, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return new DecisionResult("unchanged", change.Id, existing, read.Decisions, [], false);
        }

        var decision = new DecisionRecord(
            NextId(read.Decisions),
            category,
            question,
            request.Blocking,
            Clean(request.RequiredBefore.Length == 0 ? "plan approval" : request.RequiredBefore),
            "open",
            options,
            string.Empty,
            string.Empty,
            [Clean(request.Evidence)],
            string.Empty);
        var decisions = read.Decisions.Append(decision).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        Write(change, decisions);
        _changes.AppendEvent(change, "decision-created", new Dictionary<string, string>
        {
            ["decisionId"] = decision.Id,
            ["category"] = decision.Category,
            ["blocking"] = decision.Blocking.ToString().ToLowerInvariant(),
        });
        return new DecisionResult("created", change.Id, decision, decisions, [], true);
    }

    public DecisionResult Resolve(
        string repositoryPath,
        string changeId,
        string decisionId,
        string selectedOption,
        string rationale,
        string? evidence)
    {
        var change = _changes.Read(repositoryPath, changeId);
        if (change is null)
        {
            return Error(changeId, $"Change dossier was not found: {changeId}");
        }

        if (string.IsNullOrWhiteSpace(rationale))
        {
            return Error(change.Id, "A human resolution rationale is required.");
        }

        var read = Read(change);
        var decision = read.Decisions.FirstOrDefault(item => item.Id == decisionId);
        if (decision is null)
        {
            return Error(change.Id, $"Decision was not found: {decisionId}");
        }

        var option = decision.Options.FirstOrDefault(item => string.Equals(item, selectedOption.Trim(), StringComparison.OrdinalIgnoreCase));
        if (option is null)
        {
            return Error(change.Id, $"Resolution must select one recorded option: {string.Join(", ", decision.Options)}");
        }

        var cleanRationale = Clean(rationale);
        if (decision.Status == "resolved"
            && decision.Resolution == option
            && decision.Rationale == cleanRationale)
        {
            return new DecisionResult("unchanged", change.Id, decision, read.Decisions, [], false);
        }

        if (decision.Status == "resolved" && decision.Resolution != option)
        {
            return Error(change.Id, "A resolved decision cannot be silently changed; create a superseding decision.");
        }

        var updated = decision with
        {
            Status = "resolved",
            Resolution = option,
            Rationale = cleanRationale,
            Evidence = AppendEvidence(decision.Evidence, evidence),
        };
        var decisions = Replace(read.Decisions, updated);
        Write(change, decisions);
        _changes.AppendEvent(change, "decision-resolved", new Dictionary<string, string>
        {
            ["decisionId"] = decision.Id,
            ["resolution"] = option,
            ["rationale"] = cleanRationale,
        });
        return new DecisionResult("resolved", change.Id, updated, decisions, [], true);
    }

    public DecisionResult Defer(
        string repositoryPath,
        string changeId,
        string decisionId,
        string reason)
    {
        var change = _changes.Read(repositoryPath, changeId);
        if (change is null)
        {
            return Error(changeId, $"Change dossier was not found: {changeId}");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error(change.Id, "A deferral reason is required.");
        }

        var read = Read(change);
        var decision = read.Decisions.FirstOrDefault(item => item.Id == decisionId);
        if (decision is null)
        {
            return Error(change.Id, $"Decision was not found: {decisionId}");
        }

        if (decision.Status == "resolved")
        {
            return Error(change.Id, "A resolved decision cannot be deferred; create a superseding decision.");
        }

        var cleanReason = Clean(reason);
        if (decision.Status == "deferred" && decision.Rationale == cleanReason)
        {
            return new DecisionResult("unchanged", change.Id, decision, read.Decisions, [], false);
        }

        var updated = decision with { Status = "deferred", Rationale = cleanReason };
        var decisions = Replace(read.Decisions, updated);
        Write(change, decisions);
        _changes.AppendEvent(change, "decision-deferred", new Dictionary<string, string>
        {
            ["decisionId"] = decision.Id,
            ["reason"] = cleanReason,
            ["blocking"] = decision.Blocking.ToString().ToLowerInvariant(),
        });
        return new DecisionResult("deferred", change.Id, updated, decisions, [], true);
    }

    public DecisionResult Promote(string repositoryPath, string changeId, string decisionId, string? requestedTitle)
    {
        var change = _changes.Read(repositoryPath, changeId);
        if (change is null)
        {
            return Error(changeId, $"Change dossier was not found: {changeId}");
        }

        var read = Read(change);
        var decision = read.Decisions.FirstOrDefault(item => item.Id == decisionId);
        if (decision is null)
        {
            return Error(change.Id, $"Decision was not found: {decisionId}");
        }

        if (decision.Status != "resolved")
        {
            return Error(change.Id, "Only a resolved decision can be promoted into an ADR.");
        }

        if (decision.PromotedAdr.Length > 0)
        {
            return new DecisionResult("unchanged", change.Id, decision, read.Decisions, [], false);
        }

        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
        {
            return new DecisionResult("invalid", change.Id, decision, read.Decisions, resolution.Errors, false);
        }

        var adrDirectory = Path.Combine(resolution.Context.DocumentationPath, "architecture", "decisions");
        Directory.CreateDirectory(adrDirectory);
        var title = Clean(string.IsNullOrWhiteSpace(requestedTitle) ? decision.Question : requestedTitle);
        var adrId = NextAdrId(adrDirectory);
        var fileName = $"{adrId}-{Slug(title)}.md";
        var absolutePath = Path.Combine(adrDirectory, fileName);
        var relativePath = NormalizePath(Path.GetRelativePath(resolution.Context.RepositoryPath, absolutePath));
        var stableId = $"{resolution.Context.RepositoryId}:adr:{adrId.ToLowerInvariant()}";
        WriteNew(absolutePath, RenderAdr(change, decision, adrId, title, stableId));
        RegisterAdr(resolution.Context, stableId, relativePath);

        var updated = decision with { PromotedAdr = relativePath };
        var decisions = Replace(read.Decisions, updated);
        Write(change, decisions);
        _changes.AppendEvent(change, "decision-promoted", new Dictionary<string, string>
        {
            ["decisionId"] = decision.Id,
            ["adr"] = relativePath,
        });
        return new DecisionResult("promoted", change.Id, updated, decisions, [], true);
    }

    public DecisionReadResult Read(ChangeDossier change)
    {
        var path = _changes.DossierFile(change, "decisions.md");
        if (!File.Exists(path))
        {
            return new DecisionReadResult([], 0, 0, ["Decision document is missing."]);
        }

        var decisions = File.ReadAllLines(path)
            .Where(line => line.StartsWith("| DEC-", StringComparison.Ordinal))
            .Select(Parse)
            .ToArray();
        if (decisions.Any(item => item is null))
        {
            return new DecisionReadResult([], 0, 0, ["Decision document contains an invalid managed row."]);
        }

        var records = decisions.Cast<DecisionRecord>().ToArray();
        var duplicate = records.GroupBy(item => item.Id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            return new DecisionReadResult([], 0, 0, [$"Duplicate decision ID: {duplicate.Key}"]);
        }

        return new DecisionReadResult(
            records,
            records.Count(item => item.Blocking && item.Status is "open" or "deferred"),
            records.Count(item => !item.Blocking && item.Status is "open" or "deferred"),
            []);
    }

    private void Write(ChangeDossier change, IReadOnlyList<DecisionRecord> decisions)
    {
        var documentStatus = decisions.Count > 0 && decisions.All(item => !item.Blocking || item.Status == "resolved")
            ? "Reviewed"
            : "Draft";
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"title: {JsonSerializer.Serialize(change.Id + " decisions")}");
        builder.AppendLine("type: change-decisions");
        builder.AppendLine($"status: {documentStatus}");
        builder.AppendLine($"change_id: {change.Id}");
        builder.AppendLine("authority: human-reviewed");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("# Decisions");
        builder.AppendLine();
        builder.AppendLine("Only explicit human commands may resolve, defer, or promote a decision.");
        builder.AppendLine();
        builder.AppendLine("| ID | Category | Question | Blocking | Required before | Status | Options | Resolution | Rationale | Evidence | Promoted ADR |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var item in decisions.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            builder.AppendLine($"| {item.Id} | {item.Category} | {Clean(item.Question)} | {item.Blocking.ToString().ToLowerInvariant()} | {Clean(item.RequiredBefore)} | {item.Status} | {Join(item.Options)} | {Clean(item.Resolution)} | {Clean(item.Rationale)} | {Join(item.Evidence)} | {Clean(item.PromotedAdr)} |");
        }

        File.WriteAllText(_changes.DossierFile(change, "decisions.md"), builder.ToString());
    }

    private static DecisionRecord? Parse(string line)
    {
        var cells = line.Split('|').Select(cell => cell.Trim()).ToArray();
        if (cells.Length < 13 || !bool.TryParse(cells[4], out var blocking))
        {
            return null;
        }

        return new DecisionRecord(
            cells[1], cells[2], cells[3], blocking, cells[5], cells[6],
            Split(cells[7]), cells[8], cells[9], Split(cells[10]), cells[11]);
    }

    private static IReadOnlyList<DecisionRecord> Replace(IReadOnlyList<DecisionRecord> source, DecisionRecord replacement)
        => source.Select(item => item.Id == replacement.Id ? replacement : item).ToArray();

    private static IReadOnlyList<string> AppendEvidence(IReadOnlyList<string> evidence, string? addition)
        => string.IsNullOrWhiteSpace(addition)
            ? evidence
            : evidence.Append(Clean(addition)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string NextId(IReadOnlyList<DecisionRecord> decisions)
    {
        var maximum = decisions
            .Where(item => DecisionIdPattern().IsMatch(item.Id))
            .Select(item => int.Parse(item.Id[4..], CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max();
        return $"DEC-{maximum + 1:000}";
    }

    private static string NextAdrId(string directory)
    {
        var maximum = Directory.EnumerateFiles(directory, "ADR-*.md", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => name is null ? null : AdrIdPattern().Match(name))
            .Where(match => match is { Success: true })
            .Select(match => int.Parse(match!.Groups[1].Value, CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max();
        return $"ADR-{maximum + 1:0000}";
    }

    private static string RenderAdr(ChangeDossier change, DecisionRecord decision, string adrId, string title, string stableId)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"title: {JsonSerializer.Serialize(title)}");
        builder.AppendLine("type: architecture-decision");
        builder.AppendLine("status: Accepted");
        builder.AppendLine("owner: Repository maintainers");
        builder.AppendLine($"last_reviewed: {DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}");
        builder.AppendLine("review_cadence: on architectural change");
        builder.AppendLine("cis:");
        builder.AppendLine($"  stable_id: {stableId}");
        builder.AppendLine($"  change_id: {change.Id}");
        builder.AppendLine($"  decision_id: {decision.Id}");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine($"# {adrId}: {title}");
        builder.AppendLine();
        builder.AppendLine("## Context");
        builder.AppendLine();
        builder.AppendLine(decision.Question);
        builder.AppendLine();
        builder.AppendLine($"Evidence: {string.Join("; ", decision.Evidence)}");
        builder.AppendLine();
        builder.AppendLine("## Decision");
        builder.AppendLine();
        builder.AppendLine(decision.Resolution);
        builder.AppendLine();
        builder.AppendLine(decision.Rationale);
        builder.AppendLine();
        builder.AppendLine("## Alternatives considered");
        builder.AppendLine();
        foreach (var option in decision.Options.Where(option => option != decision.Resolution))
        {
            builder.AppendLine($"- {option}");
        }

        builder.AppendLine();
        builder.AppendLine("## Consequences");
        builder.AppendLine();
        builder.AppendLine($"The originating change `{change.Id}` and its accepted impacts and plan must implement and verify this decision.");
        builder.AppendLine();
        builder.AppendLine("## Provenance");
        builder.AppendLine();
        builder.AppendLine($"Promoted from `{change.RelativePath}/decisions.md`, decision `{decision.Id}`.");
        return builder.ToString();
    }

    private static void RegisterAdr(CisRepositoryContext context, string stableId, string relativePath)
    {
        var catalog = File.ReadAllText(context.CatalogPath);
        if (catalog.Contains($"id: {stableId}", StringComparison.Ordinal))
        {
            return;
        }

        var addition = $"""
  - id: {stableId}
    path: {relativePath}
    type: architecture-decision
    status: accepted
    authority: canonical
""";
        var separator = catalog.EndsWith('\n') ? string.Empty : Environment.NewLine;
        File.WriteAllText(context.CatalogPath, catalog + separator + addition);
    }

    private static void WriteNew(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string Join(IEnumerable<string> values) => string.Join("<br>", values.Select(Clean));

    private static string[] Split(string value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split("<br>", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Clean(string value)
        => value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string Slug(string value)
    {
        var slug = SlugPattern().Replace(value.ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "decision" : slug[..Math.Min(slug.Length, 72)].TrimEnd('-');
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static DecisionResult Error(string? changeId, string error)
        => new("invalid", changeId, null, [], [error], false);

    [GeneratedRegex("^DEC-[0-9]{3,}$", RegexOptions.CultureInvariant)]
    private static partial Regex DecisionIdPattern();

    [GeneratedRegex("^ADR-([0-9]{4,})", RegexOptions.CultureInvariant)]
    private static partial Regex AdrIdPattern();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
