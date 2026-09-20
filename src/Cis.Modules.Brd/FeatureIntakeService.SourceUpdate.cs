using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    public CisFeatureSourceUpdateResult UpdateSource(string workspacePath, string slug, string sourcePath,
        string actor, bool dryRun, bool confirmed, string? expectedPlan = null)
    {
        try { return UpdateSourceCore(workspacePath, slug, sourcePath, actor, dryRun, confirmed, expectedPlan); }
        catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { return new("blocked", null, [e.Message], false); }
    }

    private CisFeatureSourceUpdateResult UpdateSourceCore(string workspacePath, string slug, string sourcePath,
        string actor, bool dryRun, bool confirmed, string? expectedPlan)
    {
        if (!SingleLine(actor, 200)) throw new InvalidDataException("Provide the person reimporting the feature BRD.");
        var state = ReadWizard(workspacePath, slug);
        var incoming = Path.GetFullPath(sourcePath);
        if (!SafeAbsolutePath(incoming) || !File.Exists(incoming)
            || !string.Equals(Path.GetExtension(incoming), ".md", StringComparison.OrdinalIgnoreCase)
            || new FileInfo(incoming).Length is 0 or > 2_097_152)
            throw new InvalidDataException("Select a local Markdown BRD of up to 2 MiB. Links and empty files are not accepted.");
        var bytes = File.ReadAllBytes(incoming);
        var source = new UTF8Encoding(false, true).GetString(bytes);
        if (source.Contains('\0')) throw new InvalidDataException("The requirements document must be UTF-8 text without null characters.");
        var sourceHash = Hash(bytes);
        var previous = state.Record.Plan;
        if (sourceHash == previous.SourceHash) return new("unchanged", null, [], false);
        var decisions = OpenDecisions(source);
        // Ordinal answer IDs cannot be carried across reordered or deleted questions. Only
        // a unique, unchanged question is evidence that an answer belongs to a new ordinal.
        var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < previous.OpenDecisions.Count; i++)
        {
            var question = previous.OpenDecisions[i];
            if (previous.OpenDecisions.Count(item => item == question) != 1 || decisions.Count(item => item == question) != 1) continue;
            var next = decisions.ToList().IndexOf(question);
            mapping[$"decision-{i + 1:000}"] = $"decision-{next + 1:000}";
        }
        var pages = state.Review.Pages.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var retained = 0;
        if (pages.TryGetValue("business", out var business))
        {
            var answers = business.Answers.Where(pair => !pair.Key.StartsWith("decision-", StringComparison.Ordinal))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach (var pair in mapping)
                if (business.Answers.TryGetValue(pair.Key, out var answer))
                { answers[pair.Value] = answer; if (HasAnswer(answer)) retained++; }
            pages["business"] = business with { Answers = answers };
        }
        var review = state.Review with { Pages = pages };
        var requestBytes = File.ReadAllBytes(state.RequestPath);
        if (Encoding.UTF8.GetString(requestBytes).TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal) != state.Content)
            throw new InvalidDataException("The feature changed while preparing the update. Preview again.");
        var revision = ProjectWizard(state).Revision;
        var planHash = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { revision, incoming, sourceHash, actor = actor.Trim() }, Json)));
        var retainedSource = $".cis/inputs/features/{slug}/revisions/{sourceHash[7..]}/source.md";
        var history = $".cis/inputs/features/{slug}/history/{Hash(requestBytes)[7..]}.md";
        var plan = new CisFeatureSourceUpdatePlan(slug, previous.Title, incoming, previous.SourcePath, retainedSource,
            history, previous.SourceHash, sourceHash, planHash,
            new FileInfo(Path.Combine(state.Authority.RepositoryPath, previous.SourcePath)).Length, bytes.Length,
            decisions.Where(question => !previous.OpenDecisions.Contains(question, StringComparer.Ordinal)).ToArray(),
            previous.OpenDecisions.Where(question => !decisions.Contains(question, StringComparer.Ordinal)).ToArray(),
            retained, ReviewPages.Select(page => page.Title).Append("Final review").ToArray());
        var updatedPlan = previous with { SourcePath = retainedSource, SourceHash = sourceHash, OpenDecisions = decisions, PlanHash = planHash };
        var fingerprint = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            title = previous.Title, previous.Slug, sourceHash, repository = previous.RepositoryPath,
            previous.DocumentationRoot, integrations = previous.IntegrationRepositories, actor = state.Record.Actor
        }, Json)));
        var record = state.Record with { Plan = updatedPlan, Fingerprint = fingerprint,
            SourceRevisions = (state.Record.SourceRevisions ?? []).Append(new SourceRevision(previous.SourcePath,
                previous.SourceHash, history, actor.Trim(), DateTimeOffset.UtcNow.ToString("O"))).ToArray() };
        var content = ReplaceSourceInRequest(state, record, review);
        // Validate all destinations even on preview; the preview never writes files.
        var destination = SourceUpdatePath(state, retainedSource);
        var historyPath = SourceUpdatePath(state, history);
        CheckRevision(destination, bytes); CheckRevision(historyPath, requestBytes);
        if (dryRun) return new("preview", plan, [], false);
        if (!confirmed) return new("confirmation-required", plan, [], false, true);
        if (expectedPlan != planHash) return new("stale-preview", plan, ["The source, feature or product baseline changed. Preview the BRD update again."], false);

        var lockPath = SourceUpdatePath(state, ".cis/local/feature-intake.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        using var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        bool Unchanged() => File.ReadAllBytes(state.RequestPath).SequenceEqual(requestBytes)
            && state.Inputs.All(input => FileHash(input.Key) == input.Value) && FileHash(incoming) == sourceHash;
        if (!Unchanged()) return new("stale-preview", plan, ["The feature or source changed during reimport. Preview again."], false);
        // Source revisions are immutable. The single atomic request replacement is the
        // commit point: interruption beforehand leaves the old request and BRD usable.
        WriteRevision(destination, bytes);
        WriteRevision(historyPath, requestBytes);
        var temporary = state.RequestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (!Unchanged()) return new("stale-preview", plan, ["The feature changed during reimport. Its current request was preserved. Preview again."], false);
            File.Move(temporary, state.RequestPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return new("updated", plan, [], true);
    }

    private static string SourceUpdatePath(WizardState state, string relative)
    {
        if (!CisPathSafety.TryResolveUnderRoot(state.Authority.RepositoryPath, relative, out var path) || !SafeAbsolutePath(path))
            throw new InvalidDataException("The source revision uses an unsafe path.");
        return path;
    }

    private static void CheckRevision(string path, byte[] bytes)
    {
        if (Directory.Exists(path) || File.Exists(path) && !File.ReadAllBytes(path).SequenceEqual(bytes))
            throw new InvalidDataException("A retained revision differs from the expected content. Preserve and inspect it before reimporting.");
    }

    private static void WriteRevision(string path, byte[] bytes)
    {
        CheckRevision(path, bytes);
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string ReplaceSourceInRequest(WizardState state, IntakeRecord record, FeatureReview review)
    {
        var content = state.Content;
        var previous = state.Record.Plan;
        string Link(string relative) => Path.GetRelativePath(Path.GetDirectoryName(state.RequestPath)!,
            Path.Combine(state.Authority.RepositoryPath, relative)).Replace('\\', '/');
        string Decisions(CisFeatureIntakePlan p) => p.OpenDecisions.Count == 0
            ? "No numbered open-decision section was detected. Review the source for remaining assumptions."
            : string.Join('\n', p.OpenDecisions.Select((decision, i) => $"{i + 1}. {decision}"));
        // Only replace generated source fields, retaining all surrounding human prose.
        content = ReplaceOnce(content, "  source_hash: " + previous.SourceHash + "\n", "  source_hash: " + record.Plan.SourceHash + "\n");
        content = ReplaceOnce(content, "[Open the original BRD](" + Link(previous.SourcePath) + ")",
            "[Open the original BRD](" + Link(record.Plan.SourcePath) + ")");
        content = ReplaceOnce(content, "## Open decisions from the BRD\n\n" + Decisions(previous) + "\n",
            "## Open decisions from the BRD\n\n" + Decisions(record.Plan) + "\n");
        var start = content.IndexOf(RecordMarker, StringComparison.Ordinal);
        var end = content.IndexOf("\n-->", start, StringComparison.Ordinal);
        content = content[..start] + RecordMarker + JsonSerializer.Serialize(record, Json) + content[end..];
        start = content.IndexOf(ReviewStart, StringComparison.Ordinal);
        if (start >= 0)
        {
            end = content.IndexOf(ReviewEnd, start, StringComparison.Ordinal) + ReviewEnd.Length;
            content = content[..start] + RenderReview(review, record.Plan).Replace("\r\n", "\n", StringComparison.Ordinal) + content[end..];
        }
        return content;
    }

    private static string ReplaceOnce(string content, string previous, string updated)
    {
        var index = content.IndexOf(previous, StringComparison.Ordinal);
        if (index < 0 || content.IndexOf(previous, index + previous.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidDataException("A generated source field in the feature request was edited externally. Preserve those edits and restore that field before reimporting.");
        return content[..index] + updated + content[(index + previous.Length)..];
    }
}
