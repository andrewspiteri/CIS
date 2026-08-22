using System.Globalization;
using System.Text;
using Cis.Abstractions;

namespace Cis.Modules.Plan;

public sealed class TaskTypeCapabilityStore
{
    public const string RelativePath = "references/task-type-capability-selections.md";
    private const string StartMarker = "<!-- cis:task-type-capability-selections:start -->";
    private const string EndMarker = "<!-- cis:task-type-capability-selections:end -->";
    private readonly ICisRepositoryContextResolver _resolver;
    private readonly Func<DateTimeOffset> _clock;

    public TaskTypeCapabilityStore(
        ICisRepositoryContextResolver resolver,
        Func<DateTimeOffset>? clock = null)
    {
        _resolver = resolver;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public TaskTypeCapabilityStatus Read(string repositoryPath)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return new([], [], resolution.Errors);
        var path = Path.Combine(resolution.Context.DocumentationPath,
            RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            return new([], [], [$"Task-type capability selection reference was not found: {path}"]);

        var content = File.ReadAllText(path);
        var start = content.IndexOf(StartMarker, StringComparison.Ordinal);
        var end = content.IndexOf(EndMarker, StringComparison.Ordinal);
        if (start < 0 || end <= start)
            return new([], [], ["Task-type capability selection reference is missing its managed table markers."]);

        var errors = new List<string>();
        var selections = new List<TaskTypeCapabilitySelection>();
        foreach (var line in content[(start + StartMarker.Length)..end].Split('\n'))
        {
            if (!line.TrimStart().StartsWith('|') || line.Contains("|---", StringComparison.Ordinal)) continue;
            var cells = line.Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length < 8 || cells[1].Equals("Capability", StringComparison.OrdinalIgnoreCase)
                || cells[1].Equals("None", StringComparison.OrdinalIgnoreCase)) continue;
            if (!DateTimeOffset.TryParse(cells[5], CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
            {
                errors.Add($"Capability selection '{cells[1]}' has an invalid UTC timestamp.");
                continue;
            }
            selections.Add(new TaskTypeCapabilitySelection(
                cells[1], cells[2], Split(cells[3]), cells[4], timestamp, cells[6]));
        }

        foreach (var duplicate in selections.GroupBy(selection => selection.CapabilityKey, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
            errors.Add($"Capability selection is recorded more than once: {duplicate.Key}");
        return new(selections, [], errors);
    }

    public TaskTypeCapabilityResult Select(
        TaskTypeCapabilitySelectRequest request,
        TaskTypeRegistry registry)
    {
        if (string.IsNullOrWhiteSpace(request.Reviewer) || string.IsNullOrWhiteSpace(request.Rationale))
            return Error("Capability selection requires human reviewer identity and rationale.");
        var definition = registry.Find(request.SelectedTypeKey);
        if (definition is null)
            return Error($"Selected task type is not registered: {request.SelectedTypeKey}");
        var capability = string.IsNullOrWhiteSpace(request.CapabilityKey)
            ? definition.CapabilityKey
            : request.CapabilityKey.Trim();
        if (!capability.Equals(definition.CapabilityKey, StringComparison.OrdinalIgnoreCase))
            return Error($"Task type '{definition.Key}' belongs to capability '{definition.CapabilityKey}', not '{capability}'.");
        var replacements = request.ReplacesTypeKeys
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (var replaced in replacements)
        {
            if (replaced.StartsWith("core.", StringComparison.OrdinalIgnoreCase))
                return Error($"Core task types are non-replaceable: {replaced}");
            if (!(definition.ReplacesTypeKeys ?? []).Contains(replaced, StringComparer.OrdinalIgnoreCase))
                return Error($"Task type '{definition.Key}' does not declare replacement compatibility with '{replaced}'.");
        }

        var read = Read(request.RepositoryPath);
        if (read.Errors.Count > 0) return new("invalid", read.Selections, [], read.Errors, false);
        var next = new TaskTypeCapabilitySelection(
            capability,
            definition.Key,
            replacements,
            request.Reviewer.Trim(),
            _clock().ToUniversalTime(),
            Clean(request.Rationale));
        var existing = read.Selections.FirstOrDefault(selection =>
            selection.CapabilityKey.Equals(capability, StringComparison.OrdinalIgnoreCase));
        if (existing is not null
            && existing.SelectedTypeKey.Equals(next.SelectedTypeKey, StringComparison.OrdinalIgnoreCase)
            && existing.ReplacesTypeKeys.SequenceEqual(next.ReplacesTypeKeys, StringComparer.OrdinalIgnoreCase)
            && existing.Reviewer.Equals(next.Reviewer, StringComparison.Ordinal)
            && existing.Rationale.Equals(next.Rationale, StringComparison.Ordinal))
            return new("unchanged", read.Selections, [], [], false);

        var selections = read.Selections
            .Where(selection => !selection.CapabilityKey.Equals(capability, StringComparison.OrdinalIgnoreCase))
            .Append(next)
            .OrderBy(selection => selection.CapabilityKey, StringComparer.Ordinal)
            .ToArray();
        var context = _resolver.Resolve(request.RepositoryPath).Context!;
        var path = Path.Combine(context.DocumentationPath, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var content = File.ReadAllText(path);
        var rendered = RenderTable(selections);
        var start = content.IndexOf(StartMarker, StringComparison.Ordinal);
        var end = content.IndexOf(EndMarker, StringComparison.Ordinal);
        var updated = content[..(start + StartMarker.Length)] + Environment.NewLine + rendered
            + content[end..];
        File.WriteAllText(path, updated);
        return new("selected", selections, [], [], true);
    }

    private static string RenderTable(IReadOnlyList<TaskTypeCapabilitySelection> selections)
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Capability | Selected type | Replaces | Reviewer | Timestamp UTC | Rationale |");
        builder.AppendLine("|---|---|---|---|---|---|");
        if (selections.Count == 0)
            builder.AppendLine("| None | None | None | Pending | Pending | No explicit selections. |");
        foreach (var selection in selections)
            builder.AppendLine($"| {Cell(selection.CapabilityKey)} | {Cell(selection.SelectedTypeKey)} | " +
                $"{Cell(selection.ReplacesTypeKeys.Count == 0 ? "None" : string.Join(", ", selection.ReplacesTypeKeys))} | " +
                $"{Cell(selection.Reviewer)} | {selection.TimestampUtc:O} | {Cell(selection.Rationale)} |");
        return builder.ToString();
    }

    private static IReadOnlyList<string> Split(string value)
        => value.Equals("None", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Cell(string value)
        => value.Replace('|', '/').Replace("\r", " ").Replace("\n", " ").Trim();

    private static string Clean(string value) => Cell(value);

    private static TaskTypeCapabilityResult Error(string error)
        => new("invalid", [], [], [error], false);

    public static string CreateDocument(string repositoryId) => $"""
        ---
        title: "{repositoryId} Task-Type Capability Selections"
        type: task-type-capability-selections
        status: Active
        owner: Repository maintainer
        review_cadence: on task-provider or capability change
        cis:
          stable_id: {repositoryId}:reference:task-type-capability-selections
        ---

        # {repositoryId} Task-Type Capability Selections

        This canonical record resolves mutually exclusive extension providers and
        explicitly authorizes compatible extension task-type replacement. Core task
        types are non-replaceable. Use `cis plan capability select`; do not hand-edit
        the managed table.

        {StartMarker}
        {RenderTable([])}{EndMarker}

        Selection does not migrate an existing task automatically. Use
        `cis plan task migrate-type` for each affected task instance so its evidence,
        approvals, deferrals, external links, and migration history remain visible.
        """;
}
