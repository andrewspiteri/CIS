namespace Cis.Modules.Brd;

public sealed partial class BrdBacklogService
{
    private static string? ReadNoWorkValue(string content, string key)
    {
        var raw = RawNestedFrontMatter(content, key);
        if (raw is null or "null") return null;
        try { return raw.StartsWith('"') ? System.Text.Json.JsonSerializer.Deserialize<string>(raw) : raw; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static string ReconcileNoWorkRecord(string existing, string generated, string mode)
    {
        // Preserve human notes while refreshing only managed scope, obligations and provenance.
        var next = existing;
        foreach (var (start, end) in new[] { (ItemsStart, ItemsEnd), (ObligationsStart, ObligationsEnd) })
        {
            var a = next.IndexOf(start, StringComparison.Ordinal);
            var b = next.IndexOf(end, StringComparison.Ordinal);
            if (a < 0 || b < a) throw new InvalidDataException("The no-planned-work record has missing managed markers.");
            next = next[..(a + start.Length)] + "\n" + Block(generated, start, end) + "\n" + next[b..];
        }
        next = ReplaceNestedFrontMatter(next, "backlog_mode", mode);
        foreach (var key in new[] { "brd_hash", "technical_intent_hash", "no_planned_work_by", "no_planned_work_at", "approved_by", "approved_at", "approval_reason", "approved_content_hash" })
            next = ReplaceNestedFrontMatter(next, key, RawNestedFrontMatter(generated, key) ?? "null");
        next = ReplaceFrontMatter(next, "status", "Review Required");
        return ReplaceFrontMatter(next, "last_reviewed", "null");
    }
}
