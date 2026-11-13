using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed partial class AgentService
{
    private static bool HasOutcomeAuthorization(CisRepositoryContext context)
    {
        var changes = Path.Combine(context.DocumentationPath, "changes");
        return Directory.Exists(changes) && !CisPathSafety.ContainsReparsePoint(context.RepositoryPath, changes)
            && Directory.EnumerateDirectories(changes).Any(change => File.Exists(Path.Combine(change, "authorization.json")));
    }

    private static JsonElement ReadOutcomeTask(CisRepositoryContext context, CisOutcomeAuthorization authorization, string taskId)
    {
        var path = Path.Combine(context.DocumentationPath, "changes", authorization.ChangeId, "plan.json");
        if (!CisPathSafety.TryResolveUnderRoot(context.RepositoryPath, Relative(context.RepositoryPath, path), out var resolved)
            || CisPathSafety.ContainsReparsePoint(context.RepositoryPath, resolved) || !File.Exists(resolved)
            || new FileInfo(resolved).Length > CisOutcomeAuthority.MaximumBytes)
            throw new InvalidDataException("The canonical outcome plan is missing, unsafe or exceeds its existing metadata bound.");
        var bytes = File.ReadAllBytes(resolved);
        if (bytes.Length > CisOutcomeAuthority.MaximumBytes)
            throw new InvalidDataException("The canonical outcome plan exceeds its existing metadata bound.");
        using var document = JsonDocument.Parse(bytes);
        var plan = document.RootElement;
        if (plan.ValueKind != JsonValueKind.Object || !plan.TryGetProperty("authorizationId", out var authority) || authority.GetString() != authorization.Id
            || !plan.TryGetProperty("workItems", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The selected task needs a canonical plan derived from its actual outcome authorization.");
        var matches = items.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id)
            && string.Equals(id.GetString(), taskId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("The selected canonical outcome task is missing or ambiguous: " + taskId);
        return matches[0].Clone();
    }

    private static string[] OutcomeTaskScopes(JsonElement task)
        => task.TryGetProperty("authorizationScopeIds", out var scopes) && scopes.ValueKind == JsonValueKind.Array
            ? scopes.EnumerateArray().Select(scope => scope.GetString() ?? "").ToArray() : [];

    private static void ValidateOutcomeTaskScope(CisOutcomeAuthorization authorization, JsonElement task)
    {
        var scopes = OutcomeTaskScopes(task);
        if (scopes.Length == 0 || !task.TryGetProperty("authorizationScopeBasis", out var basis)
            || basis.GetString() != CisOutcomeAuthority.ScopeBasis(authorization, scopes))
            throw new InvalidDataException("The selected task's scope is missing, changed or outside the authorized outcome. Present that specific difference; other in-scope tasks retain their authority.");
        if (task.TryGetProperty("workPaths", out var paths) && paths.ValueKind == JsonValueKind.Array)
        {
            var allowed = authorization.Request.Scope.Where(scope => scopes.Contains(scope.Id, StringComparer.Ordinal))
                .SelectMany(scope => scope.Paths).ToArray();
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            foreach (var path in paths.EnumerateArray().Select(value => value.GetString() ?? ""))
                if (path.Length == 0 || path.Contains('\\') || path.Split('/').Any(segment => segment is ".." or ".git")
                    || !allowed.Any(prefix => prefix == "." || path.Equals(prefix, comparison) || path.StartsWith(prefix.TrimEnd('/') + "/", comparison)))
                    throw new InvalidDataException("The selected task names work outside its authorized paths. Present that specific scope difference.");
        }
    }

    private void ValidateOutcomeTaskEligibility(CisRepositoryContext context, CisOutcomeAuthorization authorization,
        string taskText, List<string> diagnostics, bool accountingOnly, bool nativeVerification)
    {
        if (!accountingOnly || nativeVerification)
            foreach (var readiness in _readinessChecks.Select(check => check.Evaluate(context.RepositoryPath))
                .Where(result => result.Applicable && !result.Ready))
                diagnostics.AddRange(readiness.Errors.DefaultIfEmpty("The canonical product readiness requirement is unresolved.")
                    .Select(error => $"ERROR: [{readiness.Check}] {error}"));
        static string? Header(string text, string key)
        {
            var value = CisFrontMatter.Read(text, key);
            if (value is null) return null;
            value = value.StartsWith('"') ? JsonSerializer.Deserialize<string>(value) : value.Trim('\'');
            return value is "null" or "none" ? null : value;
        }
        var taskId = Header(taskText, "task_id");
        if (taskId is null) { diagnostics.Add("ERROR: A derived task needs its canonical task identity."); return; }
        var task = ReadOutcomeTask(context, authorization, taskId);
        ValidateOutcomeTaskScope(authorization, task);
        foreach (var (header, field) in new[] { ("task_type", "taskTypeKey"), ("task_type_version", "taskTypeVersion"),
            ("parent_id", "parentId"), ("work_owner_task_id", "workOwnerTaskId"), ("instance_key", "instanceKey") })
        {
            var expected = task.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (Header(taskText, header) != expected)
                throw new InvalidDataException("The derived task's identity or ownership conflicts with its canonical contract.");
        }
        var recordedScopes = JsonSerializer.Deserialize<string[]>(CisFrontMatter.Read(taskText, "authorization_scope_ids") ?? "[]") ?? [];
        if (Header(taskText, "outcome_authorization_id") != authorization.Id
            || Header(taskText, "authorization_scope_basis") != task.GetProperty("authorizationScopeBasis").GetString()
            || !recordedScopes.Order(StringComparer.Ordinal).SequenceEqual(OutcomeTaskScopes(task).Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("The derived task's outcome/scope references conflict with its canonical contract.");
        if (!accountingOnly || nativeVerification)
            _taskContracts?.ReadTaskDigest(context.RepositoryPath, authorization.ChangeId, taskId);
        var status = task.TryGetProperty("status", out var state) ? state.GetString() : null;
        if (!accountingOnly && status is not ("Ready" or "Approved" or "InProgress"))
            diagnostics.Add($"ERROR: Direct execution requires an Approved or InProgress task; current status is '{status}'.");
        if (nativeVerification && status is not ("Ready" or "Approved" or "InProgress" or "ReadyForReview" or "Complete"))
            diagnostics.Add($"ERROR: Native verification requires an eligible derived task; current status is '{status}'.");
        if ((!accountingOnly || nativeVerification) && task.TryGetProperty("dependsOn", out var dependencies))
            foreach (var dependency in dependencies.EnumerateArray())
            {
                var predecessor = ReadOutcomeTask(context, authorization, dependency.GetString()!);
                if (!predecessor.TryGetProperty("status", out var predecessorStatus) || predecessorStatus.GetString() != "Complete")
                    diagnostics.Add("ERROR: The selected task still depends on unfinished work: " + dependency.GetString());
            }
        if (authorization.Request.VisualUi is null)
            diagnostics.Add("ERROR: Visual scope is unspecified; clarify that specific outcome decision before execution.");
        else if (authorization.Request.VisualUi == true && (!accountingOnly || nativeVerification) && !IsDesignPreparationTask(taskText))
        {
            var design = Path.Combine(context.DocumentationPath, "changes", authorization.ChangeId, "design.md");
            var content = File.Exists(design) && !CisPathSafety.ContainsReparsePoint(context.RepositoryPath, design)
                ? File.ReadAllText(design) : "";
            if (!string.Equals(CisFrontMatter.Read(content, "approval_status"), "Approved", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(CisFrontMatter.Read(content, "gate_status"), "Approved", StringComparison.OrdinalIgnoreCase))
                diagnostics.Add("ERROR: The selected UI work still needs its applicable design approval.");
        }
    }

    private string CaptureSelectedTaskAuthority(CisRepositoryContext context, string changeId, string taskId)
    {
        var authorization = CisOutcomeAuthority.Read(context, changeId);
        if (authorization is null) return CisTaskAuthorityContext.Capture(context, changeId);
        var task = ReadOutcomeTask(context, authorization, taskId);
        ValidateOutcomeTaskScope(authorization, task);
        var digest = _taskContracts?.ReadTaskDigest(context.RepositoryPath, changeId, taskId);
        if (digest is null)
        {
            var contract = JsonNode.Parse(task.GetRawText())!.AsObject();
            foreach (var mutable in new[] { "status", "title", "complexity", "parentId", "dependsOn", "assignee", "contractErrors" }) contract.Remove(mutable);
            digest = Sha(contract.ToJsonString());
        }
        var gates = CisTaskAuthorityContext.Capture(context, changeId);
        return Sha(authorization.Id + "\n" + CisOutcomeAuthority.ScopeBasis(authorization, OutcomeTaskScopes(task)) + "\n" + digest + "\n" + gates);
    }
}
