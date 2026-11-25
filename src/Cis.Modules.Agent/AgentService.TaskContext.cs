using Cis.Abstractions;

namespace Cis.Modules.Agent;

public sealed partial class AgentService
{
    private AgentTaskEnvelope RefreshResumedTaskContext(CisRepositoryContext authority,
        AgentRunManifest manifest, AgentTaskEnvelope envelope)
    {
        if (!IsDirectWorkRun(manifest, envelope))
        {
            return envelope;
        }
        using var phase = CisInvocationTelemetry.MeasurePhase("agent.resume.context");
        try
        {
            CisReadScope.Invalidate();
            var current = authority;
            if (manifest.TargetRepositoryId == authority.RepositoryId)
            {
                current = authority with
                {
                    RepositoryPath = manifest.WorkingDirectory,
                    DocumentationPath = Path.Combine(manifest.WorkingDirectory, authority.DocumentationRoot),
                    CatalogPath = Path.Combine(manifest.WorkingDirectory,
                        Path.GetRelativePath(authority.RepositoryPath, authority.CatalogPath))
                };
            }
            // Refresh only derived advice. The original persisted envelope, canonical
            // task text and accepted authority bindings remain the retained contract.
            return envelope with
            {
                TaskContext = PrepareTaskContext(current, envelope.ChangeId, envelope.TaskId,
                    envelope.CanonicalTaskPath, envelope.InstructionMarkdown, manifest.WorkingDirectory)
            };
        }
        catch (Exception error) when (error is IOException or InvalidDataException
            or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return envelope with
            {
                TaskContext = UnavailableTaskContext("CIS-TASK-CONTEXT-RESUME-UNAVAILABLE",
                    "Current advisory context could not be prepared for the resumed working directory. Inspect current implementation and verification evidence; the retained canonical task and authority remain unchanged.")
            };
        }
    }

    private CisTaskContextDelivery PrepareTaskContext(CisRepositoryContext context, string changeId,
        string taskId, string taskPath, string taskText, string? targetRepositoryPath)
    {
        IReadOnlyList<string> roots;
        try
        {
            roots = ReadContextRoots(taskText);
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidDataException)
        {
            return UnavailableTaskContext("CIS-TASK-CONTEXT-ROOTS-INVALID",
                "The canonical task's context_roots metadata is invalid. Advisory context is unavailable; execution authority and task text remain unchanged.");
        }
        if (_taskContext is not null)
        {
            var request = new CisTaskContextRequest(context.RepositoryPath, changeId, taskId, taskPath, targetRepositoryPath)
            {
                ContextRoots = roots
            };
            var delivery = _taskContext.Prepare(request);
            if (delivery is not null && delivery.SchemaVersion == 1 && delivery.Content is not null
                && delivery.Status is not null && delivery.Freshness is not null && delivery.DiagnosticCodes is not null
                && delivery.Content.Length <= request.MaxCharacters && delivery.Status.Length <= 128 && delivery.Freshness.Length <= 128
                && (delivery.SnapshotId?.Length ?? 0) <= 256 && (delivery.DetailPath?.Length ?? 0) <= 2048
                && delivery.DiagnosticCodes.Count <= 64 && delivery.DiagnosticCodes.All(code => code is not null && code.Length <= 128)
                && delivery.OmittedItems >= 0)
            {
                var measured = MeasureTaskContext(delivery with { MaxCharacters = request.MaxCharacters, MaxUtf8Bytes = request.MaxUtf8Bytes });
                if (measured.DeliveredCharacters <= request.MaxCharacters && measured.DeliveredUtf8Bytes <= request.MaxUtf8Bytes)
                {
                    return measured;
                }
            }
            return UnavailableTaskContext("CIS-TASK-CONTEXT-PROVIDER-INVALID",
                "The optional task-context provider returned an invalid or oversized advisory delivery. Inspect current implementation and verification evidence; canonical authority remains separate.");
        }
        return UnavailableTaskContext("CIS-TASK-CONTEXT-MODULE-UNAVAILABLE",
            "The optional task-context module is unavailable. Inspect current implementation and verification evidence before acting; canonical authority remains separate.");
    }

    private static IReadOnlyList<string> ReadContextRoots(string taskText)
    {
        using var reader = new StringReader(taskText);
        if (reader.ReadLine() != "---")
        {
            return [];
        }
        string[]? roots = null;
        while (reader.ReadLine() is { } line)
        {
            if (line == "---")
            {
                return roots ?? [];
            }
            if (!line.StartsWith("context_roots:", StringComparison.Ordinal))
            {
                continue;
            }
            if (roots is not null || line.Length > 1200)
            {
                throw new InvalidDataException("Context roots must be one bounded inline JSON array.");
            }
            roots = System.Text.Json.JsonSerializer.Deserialize<string[]>(line["context_roots:".Length..], JsonLineOptions)
                ?? throw new InvalidDataException("Context roots must be an inline JSON array.");
            if (roots.Length > 4 || roots.Any(root => root is null || root.Length is < 1 or > 256
                || !root.All(character => char.IsAsciiLetterOrDigit(character) || character is ':' or '.' or '_' or '-' or '#')))
            {
                throw new InvalidDataException("Context roots must contain at most four same-repository graph identifiers.");
            }
        }
        if (roots is not null)
        {
            throw new InvalidDataException("Context roots require closed canonical front matter.");
        }
        return [];
    }

    private static CisTaskContextDelivery UnavailableTaskContext(string code, string explanation)
        => MeasureTaskContext(new(1, "unavailable", "unknown", null, explanation,
            null, 16_000, 64_000, 0, 0, 0, 1, [code]));

    private static CisTaskContextDelivery MeasureTaskContext(CisTaskContextDelivery delivery)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(delivery, JsonLineOptions);
            string pretty = SerializeTaskContextJsonValue(delivery);
            string prompt = RenderTaskContextPrompt(delivery);
            int characters = Math.Max(json.Length, Math.Max(pretty.Length, prompt.Length));
            int bytes = Math.Max(System.Text.Encoding.UTF8.GetByteCount(json),
                Math.Max(System.Text.Encoding.UTF8.GetByteCount(pretty), System.Text.Encoding.UTF8.GetByteCount(prompt)));
            delivery = delivery with
            {
                DeliveredCharacters = characters,
                DeliveredUtf8Bytes = bytes,
                EstimatedTokens = (characters + 3) / 4
            };
        }
        return delivery;
    }

    internal static string SerializeTaskContextJsonValue(CisTaskContextDelivery context)
    {
        // Match the actual envelope.taskContext nesting in ordinary AgentResult JSON.
        string json = System.Text.Json.JsonSerializer.Serialize(new { envelope = new { taskContext = context } }, JsonOptions);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.GetProperty("envelope").GetProperty("taskContext").GetRawText();
    }

    private static string TaskContextPrompt(AgentTaskEnvelope envelope)
        => envelope.TaskContext is { } context ? RenderTaskContextPrompt(context) : string.Empty;

    internal static string RenderTaskContextPrompt(CisTaskContextDelivery context)
    {
        return "\n## Derived task context (advisory evidence, not execution authority)\n"
            + $"Status: {context.Status}; freshness: {context.Freshness}; omitted items: {context.OmittedItems}; detail: {context.DetailPath ?? "unavailable"}.\n"
            + "Discovered checks and evidence links are not proof of successful verification. Canonical task text and accepted authority govern.\n"
            + context.Content + "\n";
    }
}
