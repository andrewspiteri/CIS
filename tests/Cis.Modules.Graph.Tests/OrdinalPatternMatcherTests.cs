namespace Cis.Modules.Graph.Tests;

public sealed class OrdinalPatternMatcherTests
{
    [Fact]
    public void MatchesOrdinalContainsForOverlapsSuffixesUnicodeAndRepeatedOccurrences()
    {
        string[] patterns = ["API-ABC", "ABC", "API-AB", "BC", "aA", "aa", "あ/😀", "😀", "a\nb", "missing"];
        var matcher = new OrdinalPatternMatcher(patterns.Concat(patterns));
        foreach (var text in new[] { "prefixAPI-ABC/API-ABC aaAAA", "xあ/😀a\nb", "", "API-AX API-AB", "missingmissing" })
            Assert.Equal(patterns.Where(pattern => text.Contains(pattern, StringComparison.Ordinal)).Order(StringComparer.Ordinal),
                matcher.Find(text).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void MatchesOrdinalContainsAcrossManyDeterministicPatternSets()
    {
        var random = new Random(738);
        const string alphabet = "abcABC/-_\n123あ";
        string Generate(int length) => new(Enumerable.Range(0, length).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
        for (var run = 0; run < 100; run++)
        {
            var text = Generate(500);
            var patterns = Enumerable.Range(0, 60).Select(_ => Generate(random.Next(1, 12)))
                .Concat(Enumerable.Range(0, 20).Select(_ => text.Substring(random.Next(450), random.Next(1, 30))))
                .Distinct(StringComparer.Ordinal).ToArray();
            Assert.Equal(patterns.Where(pattern => text.Contains(pattern, StringComparison.Ordinal)).Order(StringComparer.Ordinal),
                new OrdinalPatternMatcher(patterns).Find(text).Order(StringComparer.Ordinal));
        }
    }
}
