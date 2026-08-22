using System.Text.RegularExpressions;

namespace Cis.Modules.Standards;

internal static partial class StandardRuleParser
{
    public static IReadOnlyList<DeclaredStandardRule> Parse(string body)
    {
        var rules = BoldRuleRegex().Matches(body)
            .Concat(HeadingRuleRegex().Matches(body))
            .OrderBy(match => match.Index)
            .Select(match => new DeclaredStandardRule(
                match.Groups["id"].Value,
                match.Groups["text"].Value.Trim()))
            .GroupBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        return rules;
    }

    [GeneratedRegex(@"(?m)^\s*(?:[-*]\s+)?\*\*\[?(?<id>[A-Z][A-Z0-9]*(?:-[A-Z][A-Z0-9]*)*-\d{2,4}(?:\.\d+)?)\]?\*\*\s*(?<text>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex BoldRuleRegex();

    [GeneratedRegex(@"(?m)^#{2,6}\s+(?<id>[A-Z][A-Z0-9]*(?:-[A-Z][A-Z0-9]*)*-\d{2,4}(?:\.\d+)?)\b[.:]?\s*(?<text>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingRuleRegex();
}

internal sealed record DeclaredStandardRule(string Id, string Text);
