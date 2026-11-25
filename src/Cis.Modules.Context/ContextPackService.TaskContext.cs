using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Context;

public sealed partial class ContextPackService
{
    public CisTaskContextDelivery Prepare(CisTaskContextRequest request)
    {
        if (request.MaxCharacters is < 1000 or > 1_000_000 || request.MaxUtf8Bytes is < 1000 or > 4_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Select a finite supported complete task-context delivery budget.");
        }
        using var phase = CisInvocationTelemetry.MeasurePhase("context.prepare");
        if (request.ContextRoots is null || request.ContextRoots.Count > 4
            || request.ContextRoots.Any(root => root is null || root.Length is < 1 or > 256
                || !root.All(character => char.IsAsciiLetterOrDigit(character) || character is ':' or '.' or '_' or '-' or '#')))
        {
            return BoundDelivery(new(1, "unavailable", "unknown", null,
                "Declared advisory roots must be at most four same-repository graph identifiers; context selection grants no authority.",
                null, request.MaxCharacters, request.MaxUtf8Bytes, 0, 0, 0, 1, ["CIS-TASK-CONTEXT-ROOTS-INVALID"]));
        }
        CisGraphSnapshotReadResult? current = _snapshots?.ReadCurrent(request.RepositoryPath);
        if (current?.Graph is null || current.ExitCode != 0 || current.Freshness != "fresh")
        {
            CisInvocationTelemetry.RecordGraph("context.task", current?.Graph?.Build.Id, "unavailable", "current-graph-unavailable");
            return BoundDelivery(new(1, "unavailable", current?.Freshness ?? "unknown", current?.Graph?.Build.Id,
                "Current graph context is unavailable. Canonical task authority remains separate; do not infer a complete implementation or verification scope.",
                null, request.MaxCharacters, request.MaxUtf8Bytes, 0, 0, 0, 0,
                ["CIS-TASK-CONTEXT-CURRENT-UNAVAILABLE"]));
        }
        string canonical = request.CanonicalTaskPath.Replace('\\', '/');
        CisGraphNode? root = current.Graph.Nodes.Where(node => node.Locations.Any(location => location.Path.Replace('\\', '/') == canonical))
            .OrderBy(node => node.Kind == "work-item" ? 0 : node.Kind == "document" ? 1 : 2)
            .ThenBy(node => node.Key, StringComparer.Ordinal).FirstOrDefault();
        if (root is null)
        {
            CisInvocationTelemetry.RecordGraph("context.task", current.Graph.Build.Id, "unavailable", "task-root-missing");
            return BoundDelivery(new(1, "unavailable", current.Freshness, current.Graph.Build.Id,
                "The current graph has no node for the canonical task. Accepted task text remains authoritative, but graph selection and evidence reachability are unresolved.",
                null, request.MaxCharacters, request.MaxUtf8Bytes, 0, 0, 0, 1, ["CIS-TASK-CONTEXT-ROOT-MISSING"]));
        }
        var additionalRoots = new List<ContextPackRoot>();
        foreach (var identity in request.ContextRoots.Distinct(StringComparer.Ordinal))
        {
            var matches = current.Graph.Nodes.Where(node => node.RepositoryId == current.Graph.Build.RepositoryId
                && (node.Key == identity || node.LocalId == identity)).ToArray();
            if (matches.Length != 1)
            {
                return BoundDelivery(new(1, "unavailable", "fresh", current.Graph.Build.Id,
                    "A declared advisory root did not resolve uniquely in the current repository graph. It does not authorize cross-repository selection or implementation.",
                    null, request.MaxCharacters, request.MaxUtf8Bytes, 0, 0, 0, 1, ["CIS-TASK-CONTEXT-ROOT-UNRESOLVED"]));
            }
            if (matches[0].Key != root.Key)
            {
                additionalRoots.Add(new(request.RepositoryPath, matches[0].Key, matches[0].Kind));
            }
        }
        ContextPackResult pack = Create(new(request.RepositoryPath, root.Key, root.Kind,
            "Implement " + request.ChangeId + "/" + request.TaskId + " under its accepted behaviour and constraints",
            "Applicable implementation, ownership, contracts, checks and recorded results; discovered links are not passed verification, and declared checks or recorded passes do not establish full criterion coverage or satisfaction",
            4, 80, Math.Max(1000, request.MaxCharacters - 1000), false, null, true, additionalRoots)
        {
            MaxUtf8Bytes = Math.Max(1000, request.MaxUtf8Bytes - 2000),
            TaskEvidenceMode = true
        });
        string content = RenderMarkdown(pack);
        int omitted = pack.Omissions.Count + pack.OmittedMetadataItems;
        if (request.TargetRepositoryPath is { } target
            && !Path.GetFullPath(target).Equals(Path.GetFullPath(request.RepositoryPath), CisPathSafety.PlatformComparison))
        {
            content += "\nParticipant implementation context is not embedded by this authority-root pack. Inspect the declared target's current graph; authority paths do not identify participant files.\n";
            omitted++;
        }
        CisInvocationTelemetry.RecordGraph("context.task", current.Graph.Build.Id, "reused", "current-task-context");
        return BoundDelivery(new(1, pack.ExitCode == 0 && pack.Freshness == "fresh" ? "prepared" : "incomplete", pack.Freshness,
            pack.Build?.Id ?? current.Graph.Build.Id, content, pack.DetailPath ?? pack.OutputPath,
            request.MaxCharacters, request.MaxUtf8Bytes, 0, 0, 0, omitted,
            pack.Diagnostics.Select(diagnostic => diagnostic.Code).Take(8).ToArray()));
    }

    private static CisTaskContextDelivery BoundDelivery(CisTaskContextDelivery result)
    {
        for (int attempt = 0; attempt < 32; attempt++)
        {
            string json = JsonSerializer.Serialize(result, ContextPackOutput.Json);
            int bytes = Encoding.UTF8.GetByteCount(json);
            if (result.DeliveredCharacters == json.Length && result.DeliveredUtf8Bytes == bytes
                && json.Length <= result.MaxCharacters && bytes <= result.MaxUtf8Bytes)
            {
                return result;
            }
            if (json.Length > result.MaxCharacters || bytes > result.MaxUtf8Bytes)
            {
                if (result.Content.Length > 128)
                {
                    result = result with
                    {
                        Content = ContextPackBudget.Cut(result.Content, result.Content.Length * 3 / 4),
                        Status = "incomplete",
                        OmittedItems = result.OmittedItems + 1
                    };
                }
                else
                {
                    result = result with
                    {
                        Status = "incomplete",
                        Content = "Context omitted by complete delivery budget; inspect the detail record.",
                        DiagnosticCodes = result.DiagnosticCodes.Take(1).ToArray(),
                        OmittedItems = result.OmittedItems + 1
                    };
                }
            }
            else
            {
                result = result with { DeliveredCharacters = json.Length, DeliveredUtf8Bytes = bytes, EstimatedTokens = (json.Length + 3) / 4 };
            }
        }
        throw new InvalidDataException("The complete task-context metadata cannot fit its declared budget.");
    }
}
