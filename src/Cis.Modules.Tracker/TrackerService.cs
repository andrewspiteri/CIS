using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Tracker;

public sealed partial class TrackerService
{
    public const string StatePath = ".cis/local/trackers/state.json";
    public const string ConflictsPath = ".cis/local/trackers/conflicts.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ICisRepositoryContextResolver _resolver;
    private readonly TrackerProviderRegistry _providers;
    private readonly Func<DateTimeOffset> _clock;

    public TrackerService(ICisRepositoryContextResolver resolver, TrackerProviderRegistry providers, Func<DateTimeOffset>? clock = null)
    {
        _resolver = resolver;
        _providers = providers;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public TrackerSyncResult Plan(string repositoryPath, string changeId, string? providerKey = null)
        => Evaluate(repositoryPath, changeId, providerKey, persistConflicts: false);

    public TrackerSyncResult Pull(string repositoryPath, string changeId, string? providerKey = null)
        => Evaluate(repositoryPath, changeId, providerKey, persistConflicts: true);

    public TrackerSyncResult Push(string repositoryPath, string changeId, string? providerKey = null)
    {
        var plan = Evaluate(repositoryPath, changeId, providerKey, persistConflicts: true);
        if (!plan.RepositoryConfigurationValid || plan.Errors.Count > 0 || plan.RepositoryPath is null)
            return plan;

        var resolution = _resolver.Resolve(plan.RepositoryPath);
        var context = resolution.Context!;
        var profile = ReadProfile(context);
        var tasks = ReadTasks(context, changeId, out _).ToDictionary(item => item.TaskId, StringComparer.OrdinalIgnoreCase);
        var mappings = ReadMappings(context.RepositoryPath, tasks.Values).ToList();
        var output = new List<TrackerSyncItem>();
        var errors = new List<string>();
        var applied = false;

        foreach (var item in plan.Items)
        {
            if (item.Action is not ("create" or "update"))
            {
                output.Add(item);
                continue;
            }

            var configuration = profile.Providers.First(candidate => candidate.Key.Equals(item.Provider, StringComparison.OrdinalIgnoreCase));
            var provider = _providers.Find(configuration.Kind);
            if (provider is null || !tasks.TryGetValue(item.TaskId, out var task))
            {
                output.Add(item with { Action = "unavailable", Message = "Provider or canonical task is unavailable." });
                errors.Add($"Cannot apply {item.Provider}/{item.TaskId}: provider or task unavailable.");
                continue;
            }

            try
            {
                var remote = item.Action == "create"
                    ? provider.Create(configuration, task)
                    : provider.Update(configuration, item.RemoteId!, task);
                var remoteDigest = RemoteDigest(remote);
                var mapping = new TrackerMapping(configuration.Key, changeId, task.TaskId, remote.Id, remote.Url,
                    task.Digest, remoteDigest, _clock().ToUniversalTime().ToString("O"), "linked");
                Upsert(mappings, mapping);
                WriteExternalLink(Path.Combine(context.RepositoryPath, task.CanonicalPath.Replace('/', Path.DirectorySeparatorChar)), mapping);
                output.Add(new TrackerSyncItem(configuration.Key, changeId, task.TaskId, item.Action,
                    $"Remote issue {item.Action} applied.", remote.Id, remote.Url, true));
                applied = true;
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                errors.Add($"{configuration.Key}/{task.TaskId}: {exception.Message}");
                output.Add(item with { Action = "failed", Message = exception.Message });
            }
        }

        var stateFile = Path.Combine(context.RepositoryPath, StatePath.Replace('/', Path.DirectorySeparatorChar));
        if (applied || mappings.Count > 0 && !File.Exists(stateFile))
        {
            WriteState(context.RepositoryPath, mappings);
            applied = true;
        }
        return new TrackerSyncResult(errors.Count > 0 ? "failed" : plan.Conflicts.Any(conflict => conflict.Status == "open") ? "conflicts" : applied ? "synchronized" : "unchanged",
            context.RepositoryPath, changeId, output, plan.Conflicts, errors, applied, true);
    }

    public TrackerStatusResult Status(string repositoryPath)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return new("invalid-repository", null, [], [], [], resolution.Errors, false);
        var context = resolution.Context;
        var profile = ReadProfile(context);
        var profileErrors = ValidateProfile(profile);
        if (profileErrors.Count > 0)
            return new("invalid-profile", context.RepositoryPath, profile.Providers, [], [], profileErrors, true);
        try
        {
            var tasks = ReadAllTasks(context);
            return new("available", context.RepositoryPath, profile.Providers,
                ReadMappings(context.RepositoryPath, tasks), ReadConflicts(context.RepositoryPath), [], true);
        }
        catch (InvalidOperationException exception)
        {
            return new("invalid-state", context.RepositoryPath, profile.Providers, [], [], [exception.Message], true);
        }
    }

    public TrackerSyncResult Resolve(string repositoryPath, string changeId, string taskId, string providerKey,
        string resolutionChoice, string reviewer, string rationale)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return Invalid(resolution.Errors);
        if (string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(rationale))
            return Failure(resolution.Context.RepositoryPath, changeId, ["Reviewer and rationale are required."]);
        if (resolutionChoice is not ("cis" or "remote" or "unlink"))
            return Failure(resolution.Context.RepositoryPath, changeId, ["Resolution must be cis, remote, or unlink."]);

        var context = resolution.Context;
        var tasks = ReadTasks(context, changeId, out var taskErrors);
        var task = tasks.FirstOrDefault(item => item.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase));
        if (task is null)
            return Failure(context.RepositoryPath, changeId, taskErrors.Count > 0 ? taskErrors : [$"Unknown task: {taskId}"]);
        List<TrackerConflict> conflicts;
        try { conflicts = ReadConflicts(context.RepositoryPath).ToList(); }
        catch (InvalidOperationException exception) { return Failure(context.RepositoryPath, changeId, [exception.Message]); }
        var conflictIndex = conflicts.FindIndex(item => item.Provider.Equals(providerKey, StringComparison.OrdinalIgnoreCase)
            && item.ChangeId.Equals(changeId, StringComparison.OrdinalIgnoreCase)
            && item.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase) && item.Status == "open");
        if (conflictIndex < 0)
            return Failure(context.RepositoryPath, changeId, ["No open conflict matches the provider and task."]);

        List<TrackerMapping> mappings;
        try { mappings = ReadMappings(context.RepositoryPath, tasks).ToList(); }
        catch (InvalidOperationException exception) { return Failure(context.RepositoryPath, changeId, [exception.Message]); }
        var mappingIndex = mappings.FindIndex(item => item.Provider.Equals(providerKey, StringComparison.OrdinalIgnoreCase)
            && item.ChangeId.Equals(changeId, StringComparison.OrdinalIgnoreCase)
            && item.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase));
        if (mappingIndex < 0)
            return Failure(context.RepositoryPath, changeId, ["The conflicted task has no durable tracker mapping."]);

        var now = _clock().ToUniversalTime().ToString("O");
        var mapping = mappings[mappingIndex];
        var conflict = conflicts[conflictIndex];
        mapping = resolutionChoice switch
        {
            "cis" => mapping with { CanonicalDigest = "force-cis", RemoteDigest = conflict.RemoteDigest, State = "linked", LastSynchronizedUtc = now },
            "remote" => mapping with { CanonicalDigest = task.Digest, RemoteDigest = conflict.RemoteDigest, State = "remote-divergence-accepted", LastSynchronizedUtc = now },
            _ => mapping with { State = "unlinked", LastSynchronizedUtc = now },
        };
        mappings[mappingIndex] = mapping;
        conflicts[conflictIndex] = conflicts[conflictIndex] with
        {
            Status = "resolved", Resolution = resolutionChoice, Reviewer = reviewer.Trim(), Rationale = rationale.Trim(), ResolvedAtUtc = now,
        };
        WriteState(context.RepositoryPath, mappings);
        WriteConflicts(context.RepositoryPath, conflicts);
        var absoluteTaskPath = Path.Combine(context.RepositoryPath, task.CanonicalPath.Replace('/', Path.DirectorySeparatorChar));
        WriteExternalLink(absoluteTaskPath, mapping);
        AppendDecision(absoluteTaskPath, providerKey, resolutionChoice, reviewer, rationale, now);
        return new("resolved", context.RepositoryPath, changeId,
            [new(providerKey, changeId, taskId, "resolved", $"Conflict resolved using {resolutionChoice} authority.", mapping.RemoteId, mapping.Url, true)],
            conflicts, [], true, true);
    }

    private TrackerSyncResult Evaluate(string repositoryPath, string changeId, string? providerKey, bool persistConflicts)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return Invalid(resolution.Errors);
        var context = resolution.Context;
        var tasks = ReadTasks(context, changeId, out var taskErrors);
        if (taskErrors.Count > 0)
            return Failure(context.RepositoryPath, changeId, taskErrors);
        var profile = ReadProfile(context);
        var profileErrors = ValidateProfile(profile);
        if (profileErrors.Count > 0) return Failure(context.RepositoryPath, changeId, profileErrors);
        var configurations = profile.Providers.Where(item => item.Enabled
            && (string.IsNullOrWhiteSpace(providerKey) || item.Key.Equals(providerKey, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (configurations.Length == 0)
            return Failure(context.RepositoryPath, changeId, [$"No enabled tracker provider matches '{providerKey ?? "all"}'."]);

        IReadOnlyList<TrackerMapping> mappings;
        List<TrackerConflict> conflicts;
        try
        {
            mappings = ReadMappings(context.RepositoryPath, tasks);
            conflicts = ReadConflicts(context.RepositoryPath).Where(item => item.Status != "open"
                || !item.ChangeId.Equals(changeId, StringComparison.OrdinalIgnoreCase)
                || (providerKey is not null && !item.Provider.Equals(providerKey, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        catch (InvalidOperationException exception)
        {
            return Failure(context.RepositoryPath, changeId, [exception.Message]);
        }
        var items = new List<TrackerSyncItem>();
        var errors = new List<string>();

        foreach (var configuration in configurations)
        {
            var provider = _providers.Find(configuration.Kind);
            if (provider is null)
            {
                errors.Add($"No loaded tracker provider supports kind '{configuration.Kind}' for '{configuration.Key}'.");
                foreach (var task in tasks)
                    items.Add(new(configuration.Key, changeId, task.TaskId, "unavailable", "Required tracker provider assembly is not loaded.", null, null, false));
                continue;
            }
            TrackerAvailability availability;
            try { availability = provider.Probe(configuration); }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                availability = new(false, exception.Message);
            }
            if (!availability.Available)
            {
                errors.Add($"{configuration.Key}: {availability.Message}");
                foreach (var task in tasks)
                    items.Add(new(configuration.Key, changeId, task.TaskId, "unavailable", availability.Message, null, null, false));
                continue;
            }

            foreach (var task in tasks)
            {
                var mapping = mappings.FirstOrDefault(item => item.Provider.Equals(configuration.Key, StringComparison.OrdinalIgnoreCase)
                    && item.ChangeId.Equals(changeId, StringComparison.OrdinalIgnoreCase)
                    && item.TaskId.Equals(task.TaskId, StringComparison.OrdinalIgnoreCase));
                if (mapping is null)
                {
                    items.Add(new(configuration.Key, changeId, task.TaskId, "create", "No remote issue is linked.", null, null, false));
                    continue;
                }
                if (mapping.State == "unlinked")
                {
                    items.Add(new(configuration.Key, changeId, task.TaskId, "unlinked", "Mapping was explicitly unlinked.", mapping.RemoteId, mapping.Url, false));
                    continue;
                }

                TrackerRemoteItem? remote;
                try { remote = provider.Get(configuration, mapping.RemoteId); }
                catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{configuration.Key}/{task.TaskId}: {exception.Message}");
                    items.Add(new(configuration.Key, changeId, task.TaskId, "failed", exception.Message, mapping.RemoteId, mapping.Url, false));
                    continue;
                }
                if (remote is null)
                {
                    AddConflict(conflicts, configuration.Key, changeId, task, mapping, "remote-deleted", "Remote issue is missing; CIS will not recreate it automatically.");
                    items.Add(new(configuration.Key, changeId, task.TaskId, "conflict", "Remote issue was deleted or is inaccessible.", mapping.RemoteId, mapping.Url, false));
                    continue;
                }
                var remoteDigest = RemoteDigest(remote);
                var canonicalChanged = !task.Digest.Equals(mapping.CanonicalDigest, StringComparison.Ordinal);
                var remoteChanged = !remoteDigest.Equals(mapping.RemoteDigest, StringComparison.Ordinal);
                if (mapping.CanonicalDigest == "force-cis") { canonicalChanged = true; remoteChanged = false; }
                if (canonicalChanged && remoteChanged)
                {
                    AddConflict(conflicts, configuration.Key, changeId, task, mapping with { RemoteDigest = remoteDigest }, "both-changed", "Canonical and remote projections changed after the last synchronization.");
                    items.Add(new(configuration.Key, changeId, task.TaskId, "conflict", "Both sides changed.", remote.Id, remote.Url, false));
                }
                else if (remoteChanged)
                {
                    AddConflict(conflicts, configuration.Key, changeId, task, mapping with { RemoteDigest = remoteDigest }, "remote-changed", "Remote fields changed; canonical task authority prevents an automatic pull.");
                    items.Add(new(configuration.Key, changeId, task.TaskId, "conflict", "Remote issue changed; human reconciliation is required.", remote.Id, remote.Url, false));
                }
                else if (canonicalChanged)
                    items.Add(new(configuration.Key, changeId, task.TaskId, "update", "Canonical task projection changed.", remote.Id, remote.Url, false));
                else
                    items.Add(new(configuration.Key, changeId, task.TaskId, "unchanged", "Canonical and remote projections match the last synchronized generation.", remote.Id, remote.Url, false));
            }
        }

        if (persistConflicts) WriteConflicts(context.RepositoryPath, conflicts);
        var open = conflicts.Where(item => item.Status == "open" && item.ChangeId.Equals(changeId, StringComparison.OrdinalIgnoreCase)).ToArray();
        return new(errors.Count > 0 ? "unavailable" : open.Length > 0 ? "conflicts" : "planned", context.RepositoryPath, changeId,
            items, open, errors, false, true);
    }

    private void AddConflict(ICollection<TrackerConflict> conflicts, string provider, string changeId,
        TrackerTaskProjection task, TrackerMapping mapping, string kind, string message)
    {
        var id = "TRACKER-" + Hash($"{provider}|{changeId}|{task.TaskId}|{kind}")[..12].ToUpperInvariant();
        if (conflicts.Any(item => item.Id == id && item.Status == "open")) return;
        conflicts.Add(new(id, provider, changeId, task.TaskId, kind, message, task.Digest, mapping.RemoteDigest,
            _clock().ToUniversalTime().ToString("O"), "open", null, null, null, null));
    }

    private static TrackerProfile ReadProfile(CisRepositoryContext context)
    {
        var path = Path.Combine(context.DocumentationPath, "references", "external-tracker-profile.md");
        if (!File.Exists(path)) return new([]);
        var table = ParseTables(File.ReadAllText(path)).FirstOrDefault(item => item.Headers.Contains("Provider", StringComparer.OrdinalIgnoreCase)
            && item.Headers.Contains("Kind", StringComparer.OrdinalIgnoreCase));
        if (table is null) return new([]);
        return new(table.Rows.Where(row => Value(row, "Provider") is not ("" or "TODO")).Select(row => new TrackerProviderConfiguration(
            Value(row, "Provider"), Value(row, "Kind"), Value(row, "Enabled").Equals("yes", StringComparison.OrdinalIgnoreCase),
            Value(row, "Target"), Value(row, "Base URL"), Value(row, "Direction"), Value(row, "Issue type"), Value(row, "Credential source"))).ToArray());
    }

    private static IReadOnlyList<TrackerTaskProjection> ReadTasks(CisRepositoryContext context, string changeId, out IReadOnlyList<string> errors)
    {
        var failures = new List<string>();
        if (!ChangeIdRegex().IsMatch(changeId)) failures.Add("Change ID must match CIS-0001.");
        var root = Path.Combine(context.DocumentationPath, "changes", changeId, "agent-tasks");
        if (failures.Count == 0 && !Directory.Exists(root)) failures.Add($"Change task directory does not exist: {changeId}/agent-tasks");
        var tasks = failures.Count > 0 ? [] : Directory.EnumerateFiles(root, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal).Select(path => ProjectTask(context, changeId, path)).ToArray();
        if (failures.Count == 0 && tasks.Length == 0) failures.Add($"Change has no durable task documents: {changeId}");
        foreach (var task in tasks)
        {
            if (string.IsNullOrWhiteSpace(task.TaskId) || string.IsNullOrWhiteSpace(task.TaskType) || string.IsNullOrWhiteSpace(task.Title))
                failures.Add($"Task projection requires title, task_id, and task_type: {task.CanonicalPath}");
        }
        foreach (var duplicate in tasks.Where(task => !string.IsNullOrWhiteSpace(task.TaskId)).GroupBy(task => task.TaskId, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            failures.Add($"Task identity is duplicated in the change dossier: {duplicate.Key}");
        errors = failures;
        return tasks;
    }

    private static IReadOnlyList<TrackerTaskProjection> ReadAllTasks(CisRepositoryContext context)
    {
        var root = Path.Combine(context.DocumentationPath, "changes");
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateDirectories(root, "CIS-*", SearchOption.TopDirectoryOnly)
            .SelectMany(change => ReadTasks(context, Path.GetFileName(change), out _)).ToArray();
    }

    private static TrackerTaskProjection ProjectTask(CisRepositoryContext context, string changeId, string path)
    {
        var content = File.ReadAllText(path);
        var taskId = FrontMatter(content, "task_id");
        var title = Unquote(FrontMatter(content, "title"));
        var body = new StringBuilder()
            .AppendLine($"<!-- cis-task: {context.RepositoryId}/{changeId}/{taskId} -->")
            .AppendLine($"Canonical: `{Path.GetRelativePath(context.RepositoryPath, path).Replace('\\', '/')}`")
            .AppendLine($"CIS task status: `{FrontMatter(content, "task_status")}`")
            .AppendLine()
            .Append(ProjectionSections(content)).ToString().TrimEnd() + Environment.NewLine;
        var labels = new[] { "cis", $"change:{changeId}", $"task:{taskId}", $"category:{FrontMatter(content, "category")}" };
        var canonicalPath = Path.GetRelativePath(context.RepositoryPath, path).Replace('\\', '/');
        var digest = Hash(title + "\n" + body + "\n" + string.Join('\n', labels));
        return new(context.RepositoryId, changeId, taskId, FrontMatter(content, "task_type"), FrontMatter(content, "category"),
            FrontMatter(content, "task_status"), title, body, labels, canonicalPath, digest);
    }

    private static string ProjectionSections(string content)
    {
        var headings = new[] { "## Objective", "## Required changes", "## Required outputs", "## Constraints and exclusions",
            "## Context and evidence", "## Dependencies and approval gates", "## Acceptance criteria", "## Targeted validation", "## Deferrals and residual risk" };
        var builder = new StringBuilder();
        foreach (var heading in headings)
        {
            var section = MarkdownSection(content, heading);
            if (section is null) continue;
            builder.AppendLine(heading).AppendLine().AppendLine(section.Trim()).AppendLine();
        }
        return builder.ToString();
    }

    private static IReadOnlyList<TrackerMapping> ReadMappings(string repositoryPath, IEnumerable<TrackerTaskProjection> tasks)
    {
        var path = Path.Combine(repositoryPath, StatePath.Replace('/', Path.DirectorySeparatorChar));
        var mappings = ReadJson<TrackerMapping[]>(path) ?? [];
        var output = mappings.ToList();
        foreach (var task in tasks)
        {
            var absolute = Path.Combine(repositoryPath, task.CanonicalPath.Replace('/', Path.DirectorySeparatorChar));
            foreach (var mapping in ReadTaskMappings(File.ReadAllText(absolute), task.ChangeId, task.TaskId)) Upsert(output, mapping);
        }
        return output;
    }

    private static IReadOnlyList<TrackerMapping> ReadTaskMappings(string content, string changeId, string taskId)
    {
        var table = ParseTables(MarkdownSection(content, "## External issue links") ?? string.Empty)
            .FirstOrDefault(item => item.Headers.Contains("Provider", StringComparer.OrdinalIgnoreCase));
        if (table is null) return [];
        return table.Rows.Where(row => Value(row, "Provider") is not ("" or "TODO")).Select(row => new TrackerMapping(
            Value(row, "Provider"), changeId, taskId, Value(row, "Remote ID"), Value(row, "URL"),
            Value(row, "Canonical digest"), Value(row, "Remote digest"), Value(row, "Last synchronized UTC"), Value(row, "State"))).ToArray();
    }

    private static IReadOnlyList<TrackerConflict> ReadConflicts(string repositoryPath)
        => ReadJson<TrackerConflict[]>(Path.Combine(repositoryPath, ConflictsPath.Replace('/', Path.DirectorySeparatorChar))) ?? [];

    private static void WriteState(string repositoryPath, IReadOnlyList<TrackerMapping> mappings)
        => WriteAtomic(Path.Combine(repositoryPath, StatePath.Replace('/', Path.DirectorySeparatorChar)), JsonSerializer.Serialize(mappings, JsonOptions) + Environment.NewLine);

    private static void WriteConflicts(string repositoryPath, IReadOnlyList<TrackerConflict> conflicts)
        => WriteAtomic(Path.Combine(repositoryPath, ConflictsPath.Replace('/', Path.DirectorySeparatorChar)), JsonSerializer.Serialize(conflicts, JsonOptions) + Environment.NewLine);

    private static void WriteExternalLink(string relativeTaskPath, TrackerMapping mapping)
    {
        var path = Path.GetFullPath(relativeTaskPath);
        if (!File.Exists(path)) return;
        var content = File.ReadAllText(path);
        const string heading = "## External issue links";
        var header = "| Provider | Remote ID | URL | Canonical digest | Remote digest | Last synchronized UTC | State |\n|---|---|---|---|---|---|---|";
        if (!content.Contains(heading, StringComparison.Ordinal)) content = content.TrimEnd() + $"\n\n{heading}\n\n{header}\n";
        var section = MarkdownSection(content, heading) ?? header;
        if (!section.Contains("| Provider |", StringComparison.Ordinal)) section = header + "\n" + section.Trim();
        var rows = section.Split('\n').Where(line => line.TrimStart().StartsWith('|')).ToList();
        var row = $"| {Cell(mapping.Provider)} | {Cell(mapping.RemoteId)} | {Cell(mapping.Url)} | {Cell(mapping.CanonicalDigest)} | {Cell(mapping.RemoteDigest)} | {Cell(mapping.LastSynchronizedUtc)} | {Cell(mapping.State)} |";
        var index = rows.FindIndex(line => line.Split('|', StringSplitOptions.TrimEntries).ElementAtOrDefault(1)?.Equals(mapping.Provider, StringComparison.OrdinalIgnoreCase) == true);
        if (index >= 0) rows[index] = row; else rows.Add(row);
        content = ReplaceSection(content, heading, string.Join(Environment.NewLine, rows));
        WriteAtomic(path, content);
    }

    private static void AppendDecision(string relativeTaskPath, string provider, string choice, string reviewer, string rationale, string timestamp)
    {
        var path = Path.GetFullPath(relativeTaskPath);
        if (!File.Exists(path)) return;
        var content = File.ReadAllText(path);
        const string heading = "## External synchronization decisions";
        var header = "| Provider | Decision | Reviewer | Timestamp UTC | Rationale |\n|---|---|---|---|---|";
        if (!content.Contains(heading, StringComparison.Ordinal)) content = content.TrimEnd() + $"\n\n{heading}\n\n{header}\n";
        var section = MarkdownSection(content, heading) ?? header;
        content = ReplaceSection(content, heading, section.TrimEnd() + Environment.NewLine
            + $"| {Cell(provider)} | {Cell(choice)} | {Cell(reviewer)} | {Cell(timestamp)} | {Cell(rationale)} |");
        WriteAtomic(path, content);
    }

    private static string RemoteDigest(TrackerRemoteItem item)
        => string.IsNullOrWhiteSpace(item.Digest) ? Hash(item.Title + "\n" + item.Body + "\n" + item.State + "\n" + string.Join('\n', item.Labels.Order())) : item.Digest;

    private static void Upsert(List<TrackerMapping> mappings, TrackerMapping mapping)
    {
        var index = mappings.FindIndex(item => item.Provider.Equals(mapping.Provider, StringComparison.OrdinalIgnoreCase)
            && item.ChangeId.Equals(mapping.ChangeId, StringComparison.OrdinalIgnoreCase)
            && item.TaskId.Equals(mapping.TaskId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) mappings[index] = mapping; else mappings.Add(mapping);
    }

    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path)) return default;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions); }
        catch (JsonException exception) { throw new InvalidOperationException($"Tracker state is invalid: {path}: {exception.Message}", exception); }
    }

    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, path, true);
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static IReadOnlyList<string> ValidateProfile(TrackerProfile profile)
    {
        var errors = new List<string>();
        foreach (var duplicate in profile.Providers.GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            errors.Add($"External tracker provider key is duplicated: {duplicate.Key}");
        foreach (var provider in profile.Providers.Where(item => item.Enabled))
        {
            if (string.IsNullOrWhiteSpace(provider.Kind) || string.IsNullOrWhiteSpace(provider.Target)
                || provider.Target.Equals("repository-defined", StringComparison.OrdinalIgnoreCase)
                || provider.Target.Contains("TODO", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Enabled tracker provider '{provider.Key}' requires a loaded kind and exact target.");
            if (!provider.Direction.Equals("cis-to-remote", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Enabled tracker provider '{provider.Key}' uses unsupported direction '{provider.Direction}'; stage 2 permits cis-to-remote only.");
            if (string.IsNullOrWhiteSpace(provider.CredentialSource))
                errors.Add($"Enabled tracker provider '{provider.Key}' requires a credential source declaration.");
        }
        return errors;
    }
    private static string FrontMatter(string content, string key)
        => Regex.Match(content, "(?m)^" + Regex.Escape(key) + @":\s*(?<value>.*)$").Groups["value"].Value.Trim();
    private static string Unquote(string value) => value.Length >= 2 && value[0] == '"' && value[^1] == '"'
        ? JsonSerializer.Deserialize<string>(value) ?? value : value;
    private static string? MarkdownSection(string content, string heading)
    {
        var start = content.IndexOf(heading, StringComparison.Ordinal);
        if (start < 0) return null;
        start += heading.Length;
        var next = content.IndexOf("\n## ", start, StringComparison.Ordinal);
        return content[start..(next < 0 ? content.Length : next)].Trim('\r', '\n');
    }
    private static string ReplaceSection(string content, string heading, string body)
    {
        var start = content.IndexOf(heading, StringComparison.Ordinal);
        var bodyStart = start + heading.Length;
        var next = content.IndexOf("\n## ", bodyStart, StringComparison.Ordinal);
        return content[..bodyStart] + Environment.NewLine + Environment.NewLine + body.Trim() + Environment.NewLine
            + (next < 0 ? string.Empty : content[next..]);
    }
    private static string Cell(string value) => value.Replace("|", "\\|").Replace('\r', ' ').Replace('\n', ' ').Trim();
    private static IReadOnlyList<MarkdownTable> ParseTables(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var output = new List<MarkdownTable>();
        for (var index = 0; index + 1 < lines.Length; index++)
        {
            if (!lines[index].TrimStart().StartsWith('|') || !Regex.IsMatch(lines[index + 1], @"^\s*\|?\s*:?-{3}")) continue;
            var headers = Cells(lines[index]); var rows = new List<IReadOnlyDictionary<string, string>>(); index += 2;
            while (index < lines.Length && lines[index].TrimStart().StartsWith('|'))
            {
                var cells = Cells(lines[index]); var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var cell = 0; cell < headers.Length; cell++) row[headers[cell]] = cell < cells.Length ? cells[cell] : string.Empty;
                rows.Add(row); index++;
            }
            output.Add(new(headers, rows)); index--;
        }
        return output;
    }
    private static string[] Cells(string line) => line.Trim().Trim('|').Split('|').Select(value => value.Trim().Replace("\\|", "|")).ToArray();
    private static string Value(IReadOnlyDictionary<string, string> row, string key) => row.GetValueOrDefault(key, string.Empty).Trim('`', ' ');
    private static TrackerSyncResult Invalid(IReadOnlyList<string> errors) => new("invalid-repository", null, null, [], [], errors, false, false);
    private static TrackerSyncResult Failure(string path, string changeId, IReadOnlyList<string> errors) => new("invalid", path, changeId, [], [], errors, false, true);
    private sealed record MarkdownTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

    [GeneratedRegex(@"^CIS-\d{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex ChangeIdRegex();
}
