using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    private sealed record DeliveryRoute(string StoryId, string RepositoryId, string Path, string Hash);
    private sealed record DeliverySearchCache(string Key, IReadOnlyList<DeliveryRoute> Routes, IReadOnlyList<string> Warnings);
    private sealed record DeliveryEvidenceMatch(int Score, CisFeatureDeliveryEvidence Evidence);

    private static IReadOnlyList<CisFeatureDeliveryEvidence> ReadDeliveryCode(WizardState state, IReadOnlyList<DeliveryFile> files,
        IReadOnlyList<DeliveryDraft> drafts, List<string> warnings, bool persistSearch)
    {
        var key = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            DeliveryVersion,
            state.Record.Plan.SourceHash,
            files = files.Select(f => new { f.RepositoryId, f.Path, f.ContentHash })
        }, Json)));
        var cachePath = Path.Combine(state.Authority.RepositoryPath, ".cis/local/feature-delivery", state.Record.Plan.Slug, "code-search.json");
        if (!SafeAbsolutePath(cachePath)) throw new InvalidDataException("The code search cache uses an unsafe path.");
        try
        {
            if (File.Exists(cachePath) && new FileInfo(cachePath).Length <= 2_097_152)
            {
                var cached = JsonSerializer.Deserialize<DeliverySearchCache>(File.ReadAllText(cachePath), Json);
                if (cached?.Key == key && cached.Routes is { Count: <= 640 } && cached.Warnings is not null)
                {
                    var contents = new Dictionary<string, DeliveryCode>(StringComparer.Ordinal);
                    var evidence = new List<CisFeatureDeliveryEvidence>();
                    foreach (var route in cached.Routes)
                    {
                        var draft = drafts.Single(d => d.Id == route.StoryId);
                        var file = files.Single(f => f.RepositoryId == route.RepositoryId && f.Path == route.Path);
                        if (!SafeAbsolutePath(file.Absolute)) throw new InvalidDataException("Code evidence path changed.");
                        if (!contents.TryGetValue(file.Absolute, out var code))
                        {
                            var content = File.ReadAllText(file.Absolute);
                            if (DeliverySensitive(content)) throw new InvalidDataException("Code evidence must be omitted.");
                            contents[file.Absolute] = code = ReadDeliverySymbols(file, content);
                        }
                        if (code.Hash != route.Hash) throw new InvalidDataException("Code evidence changed.");
                        // The cache routes to files. Excerpts are reconstructed from
                        // the actual source, never trusted from cached model text.
                        var match = MatchDeliveryCode(code, DeliveryQueryFor(draft));
                        if (match is null) throw new InvalidDataException("Code evidence is no longer relevant.");
                        evidence.Add(DeliveryEvidence(match, draft.Id, "E" + (evidence.Count + 1)));
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
        if (!persistSearch) return result;
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
        var queries = drafts.ToDictionary(draft => draft.Id, DeliveryQueryFor, StringComparer.Ordinal);
        var selected = drafts.ToDictionary(draft => draft.Id, _ => new List<DeliveryEvidenceMatch>(), StringComparer.Ordinal);
        var oversized = 0; var sensitive = 0;
        foreach (var file in files.OrderBy(f => f.RepositoryId, StringComparer.Ordinal).ThenBy(f => f.Path, StringComparer.Ordinal))
        {
            if (file.Length > 262144) { oversized++; continue; }
            if (!SafeAbsolutePath(file.Absolute)) continue;
            var content = File.ReadAllText(file.Absolute);
            if (DeliverySensitive(content)) { sensitive++; continue; }
            var code = ReadDeliverySymbols(file, content);
            foreach (var draft in drafts)
            {
                var match = MatchDeliveryCode(code, queries[draft.Id]);
                if (match is null) continue;
                // Retain bounded excerpts, not the text/symbols of every searched
                // file. Later relevant files are classified independently of the
                // size or ordering of earlier, unrelated implementation.
                selected[draft.Id].Add(new(match.Score, DeliveryEvidence(match, draft.Id, "")));
                selected[draft.Id] = selected[draft.Id].GroupBy(item => item.Evidence.RepositoryId)
                    .SelectMany(group => group.OrderByDescending(item => item.Score).ThenBy(item => item.Evidence.Path, StringComparer.Ordinal).Take(2))
                    .OrderByDescending(item => item.Score).ThenBy(item => item.Evidence.RepositoryId, StringComparer.Ordinal)
                    .ThenBy(item => item.Evidence.Path, StringComparer.Ordinal).Take(8).ToList();
            }
        }
        if (oversized > 0) warnings.Add($"Code search omitted {oversized} files larger than 256 KiB.");
        if (sensitive > 0) warnings.Add($"Code search omitted {sensitive} files with sensitive-looking content.");
        var evidence = new List<CisFeatureDeliveryEvidence>();
        foreach (var draft in drafts)
        {
            foreach (var match in selected[draft.Id])
                evidence.Add(match.Evidence with { Id = "E" + (evidence.Count + 1) });
        }
        return evidence;
    }

    private static IReadOnlyList<CisFeatureDeliveryEvidence> DeliveryCandidates(DeliveryInput input, DeliveryDraft draft)
        => input.Evidence.Where(e => e.StoryIds.Contains(draft.Id)).ToArray();
}
