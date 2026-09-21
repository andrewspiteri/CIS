using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    private sealed record DeliveryCode(DeliveryFile File, string Content, HashSet<string> Words, string Hash, IReadOnlyList<HashSet<string>> Methods);
    private sealed record DeliveryRoute(string StoryId, string RepositoryId, string Path, string Hash);
    private sealed record DeliverySearchCache(string Key, IReadOnlyList<DeliveryRoute> Routes, IReadOnlyList<string> Warnings);

    private static IReadOnlyList<CisFeatureDeliveryEvidence> ReadDeliveryCode(WizardState state, IReadOnlyList<DeliveryFile> files,
        IReadOnlyList<DeliveryDraft> drafts, List<string> warnings)
    {
        var key = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { DeliveryVersion, state.Record.Plan.SourceHash,
            files = files.Select(f => new { f.RepositoryId, f.Path, f.Length, f.Modified }) }, Json)));
        var cachePath = Path.Combine(state.Authority.RepositoryPath, ".cis/local/feature-delivery", state.Record.Plan.Slug, "code-search.json");
        if (!SafeAbsolutePath(cachePath)) throw new InvalidDataException("The code search cache uses an unsafe path.");
        try
        {
            if (File.Exists(cachePath) && new FileInfo(cachePath).Length <= 2_097_152)
            {
                var cached = JsonSerializer.Deserialize<DeliverySearchCache>(File.ReadAllText(cachePath), Json);
                if (cached?.Key == key && cached.Routes is { Count: <= 640 } && cached.Warnings is not null)
                {
                    var contents = new Dictionary<string, string>(StringComparer.Ordinal);
                    var evidence = new List<CisFeatureDeliveryEvidence>();
                    foreach (var route in cached.Routes)
                    {
                        var draft = drafts.Single(d => d.Id == route.StoryId);
                        var file = files.Single(f => f.RepositoryId == route.RepositoryId && f.Path == route.Path);
                        if (!SafeAbsolutePath(file.Absolute)) throw new InvalidDataException("Code evidence path changed.");
                        if (!contents.TryGetValue(file.Absolute, out var content)) contents[file.Absolute] = content = File.ReadAllText(file.Absolute);
                        if (Hash(Encoding.UTF8.GetBytes(content)) != route.Hash || DeliverySensitive(content)) throw new InvalidDataException("Code evidence changed or must be omitted.");
                        // The cache routes to files. Excerpts are reconstructed from
                        // the actual source, never trusted from cached model text.
                        evidence.Add(new("E" + (evidence.Count + 1), file.RepositoryId, file.Path, route.Hash,
                            DeliveryRequirementExcerpt(content, DeliveryWords(draft.Story.Title + "\n" + string.Join('\n', draft.Story.Acceptance)), DeliveryWords(draft.Story.Title))) { StoryIds = [draft.Id] });
                    }
                    warnings.AddRange(cached.Warnings);
                    return evidence;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException or ArgumentException) { /* Rebuild disposable routing state. */ }
        var searchWarnings = new List<string>();
        var result = SearchDeliveryCode(files, drafts, searchWarnings);
        warnings.AddRange(searchWarnings);
        var snapshot = new DeliverySearchCache(key, result.Select(e => new DeliveryRoute(e.StoryIds[0], e.RepositoryId, e.Path, e.ContentHash)).ToArray(), searchWarnings);
        var temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, Json));
            File.Move(temporary, cachePath, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Read-only authorities can still inspect evidence. */ }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return result;
    }

    private static IReadOnlyList<CisFeatureDeliveryEvidence> SearchDeliveryCode(IReadOnlyList<DeliveryFile> files,
        IReadOnlyList<DeliveryDraft> drafts, List<string> warnings)
    {
        var code = new List<DeliveryCode>();
        long bytes = 0; var oversized = 0; var sensitive = 0;
        foreach (var file in files.OrderBy(f => f.RepositoryId, StringComparer.Ordinal).ThenBy(f => f.Path, StringComparer.Ordinal))
        {
            if (file.Length > 262144) { oversized++; continue; }
            if (bytes + file.Length > 41_943_040) { warnings.Add("Code search reached its 40 MiB limit. Unsearched code may contain relevant implementation."); break; }
            bytes += file.Length;
            if (!SafeAbsolutePath(file.Absolute)) continue;
            var content = File.ReadAllText(file.Absolute);
            if (DeliverySensitive(content)) { sensitive++; continue; }
            // Import names and comments route poorly and can describe unimplemented
            // behaviour. Score executable declarations, properties and template text.
            var searchable = string.Join('\n', content.Split('\n').Where(line => !Regex.IsMatch(line.TrimStart(), @"^(?://|/\*|\*|import\s)")));
            var methods = Regex.Matches(content, @"\b(?<name>[a-zA-Z_]\w*)\s*(?:<[^>]+>)?\s*\([^;{}\n]*\)\s*(?::[^;{}\n]+)?\s*(?:\{|=>)")
                .Select(m => DeliveryWords(m.Groups["name"].Value)).ToArray();
            code.Add(new(file, content, DeliveryWords(file.Path + "\n" + searchable), Hash(Encoding.UTF8.GetBytes(content)), methods));
        }
        if (oversized > 0) warnings.Add($"Code search omitted {oversized} files larger than 256 KiB.");
        if (sensitive > 0) warnings.Add($"Code search omitted {sensitive} files with sensitive-looking content.");
        var frequency = code.SelectMany(c => c.Words).GroupBy(w => w, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var evidence = new List<CisFeatureDeliveryEvidence>();
        foreach (var draft in drafts)
        {
            var title = DeliveryWords(draft.Story.Title);
            var terms = DeliveryWords(draft.Story.Title + "\n" + string.Join('\n', draft.Story.Acceptance));
            var requirementWords = draft.Story.Acceptance.Select(DeliveryWords).ToArray();
            var weights = terms.ToDictionary(word => word, word => (1 + Math.Log(1 + (double)code.Count / frequency.GetValueOrDefault(word, 1)))
                * (1 + Math.Log(1 + requirementWords.Count(words => words.Contains(word)))), StringComparer.Ordinal);
            double Weight(string word) => weights[word];
            double Score(DeliveryCode candidate)
            {
                var matches = candidate.Words.Intersect(terms).ToArray();
                if (matches.Length < 2 && !candidate.Words.Overlaps(title)) return 0;
                var methods = candidate.Methods.Select(words => words.Intersect(terms).Sum(Weight)).DefaultIfEmpty(0).Max();
                return (matches.Sum(word => Weight(word) * (title.Contains(word) ? 2 : 1))
                    + DeliveryWords(candidate.File.Path).Intersect(terms).Sum(word => Weight(word) * 2) + methods * 3)
                    / Math.Sqrt(1 + candidate.Words.Count / 80d);
            }
            var scored = code.Select(c => (Code: c, Score: Score(c))).Where(c => c.Score > 0).ToArray();
            var threshold = scored.Select(c => c.Score).DefaultIfEmpty(0).Max() * 0.55;
            var ranked = scored.Where(c => c.Score >= threshold)
                .GroupBy(c => c.Code.File.RepositoryId).SelectMany(g => g.OrderByDescending(c => c.Score).ThenBy(c => c.Code.File.Path, StringComparer.Ordinal).Take(2))
                .OrderByDescending(c => c.Score).Take(8);
            // Preserve direct maintenance evidence independently of semantic ranking;
            // broad acceptance text must not hide an established ownership conflict.
            var maintenance = scored.Where(c => DeliveryScore(c.Code.File.Path, title) > 0 && StoryMatch(c.Code.Content, DeliveryMaintenanceDeclaration))
                .GroupBy(c => c.Code.File.RepositoryId).SelectMany(g => g.OrderByDescending(c => DeliveryScore(c.Code.File.Path, title)).Take(1));
            var matches = maintenance.Concat(ranked).DistinctBy(c => c.Code.File.Absolute).Take(8);
            foreach (var match in matches)
            {
                var file = match.Code.File;
                evidence.Add(new("E" + (evidence.Count + 1), file.RepositoryId, file.Path, match.Code.Hash,
                    DeliveryRequirementExcerpt(match.Code.Content, terms, title)) { StoryIds = [draft.Id] });
            }
        }
        return evidence;
    }

    private static string DeliveryRequirementExcerpt(string content, HashSet<string> terms, HashSet<string> title)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var ranked = lines.Select((line, index) => (Line: line, Index: index, Words: DeliveryWords(line)))
            .Where(l => !Regex.IsMatch(l.Line.TrimStart(), @"^(?://|/\*|\*|import\s)"))
            .Select(l => (l.Index, Score: l.Words.Intersect(terms).Count() + l.Words.Intersect(title).Count() * 2
                + (Regex.IsMatch(l.Line, DeliveryMaintenanceDeclaration, RegexOptions.IgnoreCase) ? 2 : 0)))
            .Where(l => l.Score > 0).OrderByDescending(l => l.Score).ThenBy(l => l.Index).Take(8);
        var chosen = new List<int>();
        var maintenance = Array.FindIndex(lines, line => StoryMatch(line, DeliveryMaintenanceDeclaration));
        if (maintenance >= 0)
            for (var i = maintenance; i < Math.Min(lines.Length, maintenance + 3); i++) chosen.Add(i);
        foreach (var match in ranked)
            for (var i = Math.Max(0, match.Index - 1); i < Math.Min(lines.Length, match.Index + 3); i++)
                if (!chosen.Contains(i)) chosen.Add(i);
        var excerpt = new StringBuilder();
        foreach (var index in chosen)
        {
            var line = $"{index + 1}: {lines[index].Trim()}";
            if (line.Length > 500) line = line[..500] + "…";
            if (excerpt.Length + line.Length > 1800) break;
            excerpt.AppendLine(line);
        }
        return excerpt.ToString().Trim();
    }

    private static IReadOnlyList<CisFeatureDeliveryEvidence> DeliveryCandidates(DeliveryInput input, DeliveryDraft draft)
        => input.Evidence.Where(e => e.StoryIds.Contains(draft.Id)).ToArray();
}
