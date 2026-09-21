using System.Text;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    private sealed record DeliverySymbol(string Name, string[] Words, IReadOnlyList<string> Pairs, int Line, bool Declaration, int EndLine);
    private sealed record DeliveryCode(DeliveryFile File, string[] Lines, HashSet<string> NameWords, string Hash, IReadOnlyList<DeliverySymbol> Symbols);
    private sealed record DeliveryPair(string First, string Second, int Requirement);
    private sealed record DeliveryQuery(HashSet<string> Title, IReadOnlyDictionary<string, DeliveryPair[]> Pairs, IReadOnlyList<int> Maintenance);
    private sealed record DeliveryHit(DeliverySymbol Symbol, IReadOnlyList<int> Requirements, bool Capability, int Score);
    private sealed record DeliveryMatch(DeliveryCode Code, IReadOnlyList<DeliveryHit> Hits, int Score);

    private static readonly Regex DeliveryNonCode = new(@"//[^\r\n]*|/\*[\s\S]*?\*/|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`", RegexOptions.Compiled);
    private static readonly Regex DeliveryIdentifiers = new(@"\b[a-zA-Z_]\w{2,}\b", RegexOptions.Compiled);
    private static readonly Regex DeliveryDeclarations = new(@"\b(?<name>[a-zA-Z_]\w*)\s*(?:<[^>]+>)?\s*\([^;{}\n]*\)\s*(?::[^;{}\n]+)?\s*(?:\{|=>)", RegexOptions.Compiled);
    private static readonly HashSet<string> DeliveryActions = new("select track record retain store retrieve fetch get read send receive submit redirect navigate publish create update save delete maintain manage validate verify acknowledge capture register generate return set open close handle on".Split(' '), StringComparer.Ordinal);
    private static readonly HashSet<string> DeliveryGenericConcepts = new("click track select record event reference identifier unique timestamp state status test live code application customer user administrator operator request response process operation flow journey available relevant eligible approved authorised authorized able associated related information origin originating access instance list field item document evidence".Split(' '), StringComparer.Ordinal);

    private static string DeliveryConcept(string word) => word switch
    {
        "tracking" or "tracked" => "track", "selection" or "selected" or "selecting" => "select",
        "recorded" or "recording" => "record", "retained" or "retention" => "retain",
        "storage" or "stored" or "storing" => "store", "versioned" => "version",
        "retrieval" or "retrieved" => "retrieve", "acknowledgement" or "acknowledgment" => "acknowledge",
        _ => word
    };

    private static string[] DeliveryConceptWords(string value)
    {
        value = Regex.Replace(value, @"([a-z])([A-Z])", "$1 $2");
        return Regex.Matches(value.ToLowerInvariant(), @"[a-z]{3,}").Select(m => m.Value)
            .Select(w => w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal) ? w[..^1] : w)
            .Select(DeliveryConcept).Where(w => !DeliveryStopWords.Contains(w) || DeliveryActions.Contains(w)).ToArray();
    }

    private static string DeliveryPairKey(string first, string second) => string.CompareOrdinal(first, second) < 0 ? first + "/" + second : second + "/" + first;

    private static DeliveryQuery DeliveryQueryFor(DeliveryDraft draft)
    {
        var pairs = new List<DeliveryPair>(); var maintenance = new List<int>();
        for (var i = 0; i < draft.Story.Acceptance.Count; i++)
        {
            var text = draft.Story.Acceptance[i];
            if (StoryMatch(text, @"\b(?:user|administrator|operator)\b.{0,120}\b(?:create|maintain|manage|edit|delete|update|publish)\b")) maintenance.Add(i + 1);
            // Preserve clause/list boundaries: 'Bank, Product' is not the concept
            // 'Product bank'. Isolated IDs, timestamps and click handlers cannot
            // qualify a whole file through unrelated words elsewhere in it.
            foreach (var clause in Regex.Split(text, @"[,.;:()\n]"))
            {
                var words = DeliveryConceptWords(clause).Where(w => w is not ("eligible" or "approved" or "authorised" or "authorized" or "able" or "available")).ToArray();
                for (var j = 0; j + 1 < words.Length; j++)
                    if (words[j] != words[j + 1] && !(DeliveryGenericConcepts.Contains(words[j]) && DeliveryGenericConcepts.Contains(words[j + 1]))
                        && !(DeliveryActions.Contains(words[j]) && DeliveryActions.Contains(words[j + 1])))
                        pairs.Add(new(words[j], words[j + 1], i + 1));
            }
        }
        return new(DeliveryConceptWords(draft.Story.Title).ToHashSet(StringComparer.Ordinal),
            pairs.GroupBy(p => DeliveryPairKey(p.First, p.Second)).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal), maintenance);
    }

    private static DeliveryCode ReadDeliverySymbols(DeliveryFile file, string content)
    {
        // Preserve offsets while removing prose and literals from routing signals.
        // Original source remains available for the selected, ordered excerpt.
        var searchable = DeliveryNonCode.Replace(content, m => new string(m.Value.Select(c => c is '\r' or '\n' ? c : ' ').ToArray()));
        searchable = Regex.Replace(searchable, @"(?m)^\s*(?:import|using)\s+[^;\n]*[;\n]", m => new string(m.Value.Select(c => c is '\r' or '\n' ? c : ' ').ToArray()));
        var starts = new List<int> { 0 };
        for (var i = 0; i < content.Length; i++) if (content[i] == '\n') starts.Add(i + 1);
        int Line(int offset) { var index = starts.BinarySearch(offset); return index >= 0 ? index : ~index - 1; }
        var declarations = new Dictionary<int, int>();
        foreach (Match method in DeliveryDeclarations.Matches(searchable))
        {
            if (method.Groups["name"].Value is "if" or "for" or "while" or "switch" or "catch") continue;
            var start = method.Index + method.Length - 1; var end = start;
            if (searchable[start] == '{')
            {
                var depth = 1;
                while (++end < searchable.Length && end - start < 8000 && depth > 0)
                { if (searchable[end] == '{') depth++; else if (searchable[end] == '}') depth--; }
            }
            declarations[method.Groups["name"].Index] = Math.Min(Line(Math.Min(end, content.Length)), Line(method.Index) + 60);
        }
        var symbols = DeliveryIdentifiers.Matches(searchable).Select(m =>
        {
            var line = Line(m.Index); var declaration = declarations.TryGetValue(m.Index, out var end);
            var words = DeliveryConceptWords(m.Value);
            return new DeliverySymbol(m.Value, words, words.Zip(words.Skip(1)).Select(p => DeliveryPairKey(p.First, p.Second)).Distinct().ToArray(), line, declaration, declaration ? end : line);
        }).Where(s => s.Words.Length > 1 || s.Declaration).DistinctBy(s => (s.Name, s.Declaration)).ToArray();
        return new(file, content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'),
            DeliveryConceptWords(Path.GetFileName(file.Path)).ToHashSet(StringComparer.Ordinal), Hash(Encoding.UTF8.GetBytes(content)), symbols);
    }

    private static DeliveryMatch? MatchDeliveryCode(DeliveryCode code, DeliveryQuery query)
    {
        var hits = new List<DeliveryHit>();
        foreach (var symbol in code.Symbols)
        {
            var titleMatch = query.Title.Count > 1 && query.Title.All(symbol.Words.Contains);
            var maintenance = symbol.Declaration && query.Maintenance.Count > 0 && code.NameWords.Overlaps(query.Title)
                && symbol.Words.FirstOrDefault() is "create" or "update" or "save" or "delete" or "publish";
            var requirements = symbol.Pairs.SelectMany(pair => query.Pairs.GetValueOrDefault(pair) ?? []).Where(p =>
                // setSelectedProduct is a state setter, not the selectProduct
                // operation. Compound names must preserve the requested action.
                !DeliveryActions.Contains(p.First) || symbol.Declaration && symbol.Words.FirstOrDefault(w => w is not ("on" or "handle")) == p.First)
                .Select(p => p.Requirement).Concat(maintenance ? query.Maintenance : []).Distinct().Order().ToArray();
            if (!titleMatch && !maintenance && requirements.Length == 0) continue;
            var capability = titleMatch || maintenance;
            hits.Add(new(symbol, requirements, capability, (capability ? 100 : 0) + (symbol.Declaration ? 30 : 0) + requirements.Length * 10));
        }
        if (hits.Count == 0) return null;
        var selected = hits.OrderByDescending(h => h.Score).ThenBy(h => h.Symbol.Line).DistinctBy(h => h.Symbol.Name).Take(3).ToArray();
        return new(code, selected, selected.Max(h => h.Score) + selected.SelectMany(h => h.Requirements).Distinct().Count());
    }

    private static bool DeliveryUnsupportedHookClaim(DeliveryAssessment? assessment, IReadOnlyList<CisFeatureDeliveryEvidence> candidates, string title)
    {
        if (assessment?.EvidenceIds is null || assessment.ExistingCapability is null) return false;
        var cited = candidates.Where(e => assessment.EvidenceIds.Contains(e.Id)).ToArray();
        if (cited.Length == 0 || cited.Any(e => e.Kind != "integration-point")) return false;
        var text = assessment.ExistingCapability;
        var namesAnOperation = cited.SelectMany(e => e.Symbols).Any(name => Regex.IsMatch(text, @"\b" + Regex.Escape(name) + @"\b", RegexOptions.IgnoreCase));
        var claimsWholeStory = DeliveryConceptWords(title).All(DeliveryConceptWords(text).Contains)
            && StoryMatch(text, @"\b(?:implements|provides|supports)\b|\b(?:already|is|are)\s+(?:implemented|available|supported|provided|present)\b");
        return claimsWholeStory || assessment.Treatment is "reuse" or "extend" && !namesAnOperation;
    }

    private static CisFeatureDeliveryEvidence DeliveryEvidence(DeliveryMatch match, string storyId, string id)
    {
        var selected = new SortedSet<int>();
        foreach (var hit in match.Hits)
        {
            var symbol = hit.Symbol;
            // Show one coherent operation, never a bag of scattered high-scoring
            // lines. Non-declarations get a short local context only.
            var end = Math.Min(match.Code.Lines.Length - 1, symbol.Declaration ? symbol.EndLine : symbol.Line + 2);
            for (var line = symbol.Declaration ? symbol.Line : Math.Max(0, symbol.Line - 1); line <= end; line++) selected.Add(line);
        }
        var excerpt = new StringBuilder(); var previous = -2;
        foreach (var index in selected)
        {
            var line = $"{index + 1}: {match.Code.Lines[index].Trim()}";
            if (line.Length > 500) line = line[..500] + "…";
            if (excerpt.Length + line.Length > 2200) { excerpt.AppendLine("… (open the file to continue)"); break; }
            if (previous >= 0 && index != previous + 1) excerpt.AppendLine("…");
            excerpt.AppendLine(line); previous = index;
        }
        var capability = match.Hits.Any(h => h.Capability);
        var names = string.Join(", ", match.Hits.Select(h => h.Symbol.Name));
        var requirements = match.Hits.SelectMany(h => h.Requirements).Distinct().Order().ToArray();
        return new(id, match.Code.File.RepositoryId, match.Code.File.Path, match.Code.Hash, excerpt.ToString().Trim())
        {
            StoryIds = [storyId], Kind = capability ? "capability-candidate" : "integration-point", RequirementNumbers = requirements, Symbols = match.Hits.Select(h => h.Symbol.Name).ToArray(),
            Summary = capability ? $"{names} names the capability or its maintenance operation. Check its behaviour against the requirements before proposing reuse."
                : $"{names} matches a related operation or data concept in requirement{(requirements.Length == 1 ? "" : "s")} {string.Join(", ", requirements)}. Inspect it as a possible integration point; this does not establish the story's implementation."
        };
    }
}
