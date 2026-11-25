using System.Text;
using Cis.Abstractions;

namespace Cis.Modules.Context;

// The budget covers the complete standard JSON/agent/human result and the written Markdown, not just excerpts.
internal static class ContextPackBudget
{
    internal static ContextPackResult Fit(ContextPackResult original, int? maximumBytes = null)
    {
        int characters = Math.Clamp(original.MaxCharacters, 1000, 1_000_000);
        int bytes = maximumBytes ?? checked(characters * 4);
        ContextPackResult result = CompactMetadata(original);
        while (true)
        {
            result = Measure(result);
            if (Fits(result, characters, bytes))
            {
                return result;
            }
            int index = result.Sources.ToList().FindLastIndex(source => source.Excerpts.Any(excerpt => excerpt.Content.Length > 0));
            if (index >= 0)
            {
                var sources = result.Sources.ToArray();
                var source = sources[index];
                var excerpts = source.Excerpts.ToArray();
                int last = Array.FindLastIndex(excerpts, excerpt => excerpt.Content.Length > 0);
                string text = Cut(excerpts[last].Content, excerpts[last].Content.Length / 2);
                excerpts[last] = excerpts[last] with { Content = text, IncludedCharacters = text.Length, Truncated = true };
                int included = excerpts.Sum(excerpt => excerpt.Content.Length);
                sources[index] = source with { Excerpts = excerpts, IncludedCharacters = included, EstimatedTokens = Tokens(included), Truncated = true };
                result = result with { Sources = sources, ContentTruncated = true };
            }
            else if (result.Sources.Count > 0)
            {
                result = result with
                {
                    Sources = result.Sources.Take(result.Sources.Count - 1).ToArray(),
                    ContentTruncated = true,
                    OmittedMetadataItems = result.OmittedMetadataItems + 1,
                    Omissions = [new(null, null, "*", "complete-delivery-budget", null)]
                };
            }
            else if (result.Roots.Count > 0)
            {
                result = result with { Roots = result.Roots.Take(result.Roots.Count - 1).ToArray(), OmittedMetadataItems = result.OmittedMetadataItems + 1, ContentTruncated = true };
            }
            else if (result.StartNode is not null)
            {
                result = result with { StartNode = null, OmittedMetadataItems = result.OmittedMetadataItems + 1, ContentTruncated = true };
            }
            else if (result.Diagnostics.Count > 0)
            {
                result = result with
                {
                    Diagnostics = result.Diagnostics.Take(result.Diagnostics.Count - 1).ToArray(),
                    OmittedErrors = result.OmittedErrors || result.Diagnostics.Last().Severity == "error",
                    OmittedMetadataItems = result.OmittedMetadataItems + 1,
                    ContentTruncated = true
                };
            }
            else if (result.VerificationFacts is { Count: > 0 } facts)
            {
                result = result with
                {
                    VerificationFacts = facts.Take(facts.Count - 1).ToArray(),
                    OmittedMetadataItems = result.OmittedMetadataItems + 1,
                    ContentTruncated = true
                };
            }
            else if (result.Build is not null || result.RepositoryPath is not null || result.Purpose.Length > 32 || result.Verification.Length > 32)
            {
                result = result with
                {
                    Build = null,
                    RepositoryPath = null,
                    Purpose = Cut(result.Purpose, 32),
                    Verification = Cut(result.Verification, 32),
                    OmittedMetadataItems = result.OmittedMetadataItems + 1,
                    ContentTruncated = true
                };
            }
            else if (result.Omissions.Count > 0)
            {
                result = result with { Omissions = [], OmittedMetadataItems = result.OmittedMetadataItems + 1, ContentTruncated = true };
            }
            else
            {
                // Mandatory fixed result fields remain visible even for an unusably small explicit byte budget.
                return Measure(result with
                {
                    Status = "delivery-budget-too-small",
                    RequestValid = false,
                    OutputPath = null,
                    DetailPath = null,
                    Purpose = "omitted",
                    Verification = "omitted",
                    ContentTruncated = true
                });
            }
        }
    }

    private static ContextPackResult CompactMetadata(ContextPackResult original)
    {
        int omitted = Math.Max(0, original.Roots.Count - 4) + Math.Max(0, original.Sources.Count - 32)
            + Math.Max(0, original.Omissions.Count - 16);
        CisGraphNode Node(CisGraphNode node)
        {
            var locations = node.Locations.Where(value => value.Path.Length <= 512 && value.LocatorValue.Length <= 512).Take(2).ToArray();
            var provenance = node.Provenance.Where(value => value.Path.Length <= 512 && value.LocatorValue.Length <= 512).Take(2).ToArray();
            omitted += node.Properties.Count + Math.Max(0, node.Facets.Count - 8) + node.Locations.Count - locations.Length
                + node.Provenance.Count - provenance.Length + (node.Label.Length > 256 ? 1 : 0);
            return node with
            {
                Label = Cut(node.Label, 256),
                Properties = new Dictionary<string, string>(),
                Facets = node.Facets.Take(8).ToArray(),
                Locations = locations,
                Provenance = provenance
            };
        }
        ContextPackSource Source(ContextPackSource source)
        {
            var evidence = source.Evidence.Where(item => item.LocatorValue.Length <= 512 && (item.Method?.Length ?? 0) <= 256).Take(4).ToArray();
            omitted += Math.Max(0, source.NodeKeys.Count - 8) + Math.Max(0, source.Reasons.Count - 8)
                + source.Reasons.Take(8).Count(reason => reason.Length > 160) + source.Evidence.Count - evidence.Length
                + source.Excerpts.Sum(excerpt => excerpt.Locators.Count - Math.Min(4, excerpt.Locators.Count(locator => locator.Length <= 256)));
            return source with
            {
                NodeKeys = source.NodeKeys.Take(8).ToArray(),
                Reasons = source.Reasons.Take(8).Select(reason => Cut(reason, 160)).ToArray(),
                Evidence = evidence,
                OmittedEvidence = source.TotalEvidence - evidence.Length,
                Excerpts = source.Excerpts.Select(excerpt => excerpt with { Locators = excerpt.Locators.Where(locator => locator.Length <= 256).Take(4).ToArray() }).ToArray()
            };
        }
        var diagnostics = original.Diagnostics.OrderByDescending(item => item.Severity == "error").Take(8)
            .Select(item => item with { Message = Cut(item.Message, 512), Evidence = item.Evidence.Take(2).Select(value => Cut(value, 256)).ToArray() }).ToArray();
        omitted += Math.Max(0, original.Diagnostics.Count - diagnostics.Length) + original.Diagnostics.Take(8).Count(item => item.Message.Length > 512)
            + original.Diagnostics.Take(8).Sum(item => Math.Max(0, item.Evidence.Count - 2) + item.Evidence.Take(2).Count(value => value.Length > 256))
            + (original.Purpose.Length > 512 ? 1 : 0) + (original.Verification.Length > 512 ? 1 : 0);
        var startNode = original.StartNode is null ? null : Node(original.StartNode);
        var roots = original.Roots.Take(4).Select(root => root with { StartNode = Node(root.StartNode) }).ToArray();
        var sources = original.Sources.Take(32).Select(Source).ToArray();
        var facts = original.VerificationFacts?.Where(fact => fact.CheckKey.Length <= 2048 && fact.ResultReference.Length <= 2048
            && fact.RecordedStatus.Length <= 128 && fact.RecordedOutcome.Length <= 128 && fact.InputState.Length <= 128
            && fact.ReceiptState.Length <= 128 && fact.Applicability.Length <= 128).Take(8).ToArray();
        omitted += (original.VerificationFacts?.Count ?? 0) - (facts?.Length ?? 0);
        return original with
        {
            Purpose = Cut(original.Purpose, 512),
            Verification = Cut(original.Verification, 512),
            StartNode = startNode,
            Roots = roots,
            Sources = sources,
            Omissions = original.Omissions.Take(16).ToArray(),
            Diagnostics = diagnostics,
            VerificationFacts = facts,
            OmittedErrors = original.OmittedErrors || original.Diagnostics.Count(item => item.Severity == "error") > diagnostics.Count(item => item.Severity == "error"),
            OmittedMetadataItems = original.OmittedMetadataItems + omitted,
            ContentTruncated = original.ContentTruncated || omitted > 0
        };
    }

    private static ContextPackResult Measure(ContextPackResult result)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            string json = ContextPackOutput.Render(result, "json");
            string markdown = ContextPackService.RenderMarkdown(result);
            string agent = ContextPackOutput.Render(result, "agent") + Environment.NewLine;
            string human = ContextPackOutput.Render(result, "human") + Environment.NewLine;
            int count = new[] { json.Length + Environment.NewLine.Length, markdown.Length, agent.Length, human.Length }.Max();
            int bytes = new[] { Encoding.UTF8.GetByteCount(json + Environment.NewLine), Encoding.UTF8.GetByteCount(markdown),
                Encoding.UTF8.GetByteCount(agent), Encoding.UTF8.GetByteCount(human) }.Max();
            if (result.DeliveredCharacters == count && result.DeliveredUtf8Bytes == bytes)
            {
                return result;
            }
            result = result with
            {
                DeliveredCharacters = count,
                DeliveredUtf8Bytes = bytes,
                EstimatedDeliveredTokens = Tokens(count),
                EstimatedTokens = Tokens(result.Sources.Sum(source => source.IncludedCharacters))
            };
        }
        return result;
    }

    private static bool Fits(ContextPackResult result, int characters, int bytes)
        => result.DeliveredCharacters <= characters && result.DeliveredUtf8Bytes <= bytes;

    internal static string Cut(string value, int limit)
    {
        if (value.Length <= limit)
        {
            return value;
        }
        if (limit > 0 && char.IsHighSurrogate(value[limit - 1]))
        {
            limit--;
        }
        return value[..limit];
    }

    private static int Tokens(int characters) => (characters + 3) / 4;
}
