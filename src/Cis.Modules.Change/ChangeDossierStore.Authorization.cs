using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Cis.Abstractions;

namespace Cis.Modules.Change;

public sealed partial class ChangeDossierStore
{
    public CisOutcomeAuthorization? ReadAuthorization(string repositoryPath, string changeId)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is not { } context)
            throw new InvalidDataException("The outcome authority repository could not be resolved.");
        return CisOutcomeAuthority.Read(context, changeId);
    }

    public ChangeResult Authorize(ChangeOutcomeAuthorizationRequest request)
    {
        ChangeDossier? change = null;
        CisOutcomeAuthorization? authorization = null;
        var applied = false;
        try
        {
            change = Read(request.RepositoryPath, request.ChangeId)
                ?? throw new InvalidDataException("The canonical change was not found.");
            if (change.Status.Equals("Closed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A closed change retains its historical authority; prepare new work instead of replacing it.");
            var resolution = _resolver.Resolve(request.RepositoryPath);
            if (!resolution.IsSuccess || resolution.Context is not { } context)
                throw new InvalidDataException("The outcome authority repository could not be resolved.");
            var normalized = CisOutcomeAuthority.Normalize(request.Authorization);
            var path = CisOutcomeAuthority.Path(context, change.Id);
            var lockPath = path + ".lock";
            if (CisPathSafety.ContainsReparsePoint(context.RepositoryPath, lockPath))
                throw new InvalidDataException("The authorization transaction path must not be linked.");
            using var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                1, FileOptions.DeleteOnClose);
            var previous = CisOutcomeAuthority.Read(context, change.Id);
            var applicability = DocumentApplicability(change, normalized.VisualUi, normalized.ManualTestCatalogue,
                supersedesAuthorization: true);
            if (applicability.Errors.Count > 0) return new("blocked", change, [change], applicability.Errors, false);
            var derivedVisualUi = !normalized.VisualUi.HasValue && applicability.VisualUi.HasValue;
            normalized = normalized with { VisualUi = normalized.VisualUi ?? applicability.VisualUi };
            var digest = CisOutcomeAuthority.Digest(normalized);
            if (previous is not null && string.Equals(previous.Digest, digest, StringComparison.OrdinalIgnoreCase))
            {
                var viewPaths = new[] { "proposal.md", "wireframes.md", "design.md", "test-cases.md", "test-cases.csv" }
                    .Select(file => DossierFile(change, file)).Append(context.CatalogPath).ToArray();
                var before = viewPaths.Select(ViewDigest).ToArray();
                WriteOutcomeView(change, previous);
                var viewErrors = EnsureApplicableDocuments(change, normalized.VisualUi, normalized.ManualTestCatalogue);
                var viewsChanged = !before.SequenceEqual(viewPaths.Select(ViewDigest), StringComparer.Ordinal);
                return new("authorization-reused", change, [change], viewErrors, viewsChanged) { Authorization = previous };
            }
            if (string.IsNullOrWhiteSpace(request.Reviewer) || string.IsNullOrWhiteSpace(request.Reason))
                throw new InvalidDataException("Outcome authorization requires the actual human reviewer and reason.");
            if (previous is not null && previous.Reviewer != request.Reviewer.Trim())
                throw new InvalidDataException("A material outcome amendment requires its original human authority; preserve the existing authorization.");
            if (previous is not null && previous.Request.DecisionReference == normalized.DecisionReference)
                throw new InvalidDataException("A material outcome or limit amendment needs its specific new decision reference; the previous decision cannot approve changed content.");
            authorization = new(previous?.Id ?? $"{context.RepositoryId}:{change.Id}:outcome:{Guid.NewGuid():N}",
                change.Id, normalized, previous?.Reviewer ?? request.Reviewer.Trim(), request.Reason.Trim(),
                _clock().ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), digest)
            {
                VisualUiDerivedFromAcceptedSource = derivedVisualUi,
                History = previous is null ? [] : previous.History.Append(new CisOutcomeAuthorizationRevision(previous.Request, previous.Reviewer,
                    previous.Reason, previous.AuthorizedAtUtc, previous.Digest)
                { VisualUiDerivedFromAcceptedSource = previous.VisualUiDerivedFromAcceptedSource }).ToArray()
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(authorization, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (bytes.Length > CisOutcomeAuthority.MaximumBytes) throw new InvalidDataException("Preserve the existing bounded authorization record before adding more amendment history.");
            var temporary = path + ".tmp";
            if (CisPathSafety.ContainsReparsePoint(context.RepositoryPath, temporary))
                throw new InvalidDataException("The authorization publication path must not be linked.");
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
            applied = true;
            WriteOutcomeView(change, authorization);
            AppendEvent(change, previous is null ? "outcome-authorized" : "outcome-authorization-amended", new Dictionary<string, string>
            {
                ["authorizationId"] = authorization.Id, ["digest"] = authorization.Digest,
                ["reviewer"] = authorization.Reviewer, ["reason"] = authorization.Reason,
                ["decisionReference"] = normalized.DecisionReference,
                ["previousDigest"] = previous?.Digest ?? "none"
            });
            var errors = EnsureApplicableDocuments(change, normalized.VisualUi, normalized.ManualTestCatalogue);
            return new(errors.Count == 0 ? "outcome-authorized" : "authorization-recorded", change, [change], errors, true)
            { Authorization = authorization };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            return new(applied ? "authorization-recorded" : "invalid", change, change is null ? [] : [change], [error.Message], applied)
            { Authorization = authorization };
        }
    }

    private static string ViewDigest(string path)
    {
        if (!File.Exists(path)) return "missing";
        using var input = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }

    private void WriteOutcomeView(ChangeDossier change, CisOutcomeAuthorization authorization)
    {
        var path = DossierFile(change, "proposal.md");
        var text = File.ReadAllText(path);
        const string begin = "<!-- cis:outcome-view -->";
        const string end = "<!-- /cis:outcome-view -->";
        var request = authorization.Request;
        var view = new StringBuilder(begin + "\n\n## Authorized outcome and limits\n\n");
        view.AppendLine($"Derived from [the canonical authorization](authorization.json), decision `{request.DecisionReference}`, recorded for {authorization.Reviewer}. Tasks reference this shared context; their organization is not a separate human review.\n");
        view.AppendLine(request.Outcome + "\n");
        foreach (var scope in request.Scope)
            view.AppendLine($"- `{scope.Id}`: {scope.Description} Paths: {string.Join(", ", scope.Paths.Select(value => "`" + value + "`"))}.");
        view.AppendLine();
        foreach (var constraint in request.Constraints) view.AppendLine("- " + constraint);
        view.AppendLine($"\nThe total is {request.Seconds:N0} active seconds and {request.Attempts} logical attempts, including {request.RestorationSeconds:N0} protected restoration seconds. Storage is {request.Storage.MaximumBytes:N0} bytes including existing files and {request.Storage.RestorationBytes:N0} protected reserve bytes.");
        view.AppendLine("Owned roots: " + string.Join(", ", request.Storage.Roots.Select(root => "`" + root.Path + "`" + (root.Scratch ? " (count only)" : ""))) + ".");
        view.AppendLine($"Controllers: {string.Join(", ", request.Controllers)}. Task organization: {(request.OrganizeTasks ? "delegated" : "not delegated")}; resource distribution within the total: {(request.DistributeResources ? "delegated" : "not delegated")}.");
        view.AppendLine($"Visual UI: {(request.VisualUi.HasValue ? request.VisualUi.Value ? "applicable" : "not applicable" : "requires clarification")}; manual-test catalogue: {(request.ManualTestCatalogue ? "required" : "no obligation recorded")}.");
        view.Append(end);
        var from = text.IndexOf(begin, StringComparison.Ordinal);
        var to = from < 0 ? -1 : text.IndexOf(end, from, StringComparison.Ordinal);
        if (from >= 0 && to < 0) throw new InvalidDataException("Restore the complete generated outcome view before refreshing it.");
        var updated = from < 0 ? text.TrimEnd() + "\n\n" + view + "\n" : text[..from] + view + text[(to + end.Length)..];
        if (IsProportionate(change)) updated = CisFrontMatter.Set(updated, "authority", "derived-outcome-authorization");
        if (IsProportionate(change)) updated = updated.Replace("- TODO: define outcome-level acceptance criteria before plan approval.",
            "- Deliver the authorized outcome and its task-specific acceptance criteria within the shared constraints and limits.", StringComparison.Ordinal);
        if (text != updated) File.WriteAllText(path, updated);
    }
}
