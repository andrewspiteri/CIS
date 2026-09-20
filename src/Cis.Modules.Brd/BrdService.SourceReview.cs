using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class BrdService
{
    private static IReadOnlyList<CisBrdSourceReview> DescribeSourceReviews(
        string content, IReadOnlyList<BrdCandidate> candidates, IReadOnlyDictionary<string, SourceRow> rows, string? authorityPath = null)
    {
        var lines = content.Split('\n');
        var start = Array.FindIndex(lines, line => line.Contains(SourcesStart, StringComparison.Ordinal));
        var end = Array.FindIndex(lines, Math.Max(0, start), line => line.Contains(SourcesEnd, StringComparison.Ordinal));
        var locations = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = Math.Max(0, start); index < end; index++)
        {
            var cells = Cells(lines[index]);
            if (cells.Length is 6 or 7 && cells[0].StartsWith("BRD-SRC-", StringComparison.Ordinal))
                locations.TryAdd(cells[0], index + 1);
        }
        return candidates.Where(candidate => !candidate.Canonical).Select(candidate =>
        {
            rows.TryGetValue(candidate.Id, out var row);
            var issues = new List<string>();
            if (row is null) issues.Add("New source: refresh the BRD evidence to add its decision row.");
            else
            {
                if (row.Assessment is not ("Adopted" or "Reference" or "Rejected"))
                    issues.Add("Choose Adopted, Reference, or Rejected.");
                if (string.IsNullOrWhiteSpace(row.Rationale) || PlaceholderPattern().IsMatch(row.Rationale))
                    issues.Add("Explain why this source is used or excluded.");
                if (row.Hash != candidate.ContentHash)
                    issues.Add("This source changed after assessment. Refresh the evidence and review its decision again.");
                if (candidate.Kind == "feature-specification" && row.Assessment == "Adopted"
                    && !ExtractSection(content, "Traceability").Contains(candidate.Id, StringComparison.Ordinal))
                    issues.Add("The adopted feature must be incorporated and linked in the BRD traceability section.");
            }
            if (candidate.MaterialSourceDrift) issues.Add("Cited evidence changed materially. Review the source changes.");
            return new CisBrdSourceReview(candidate.Id, candidate.RepositoryId, candidate.RepositoryPath,
                candidate.Path, row?.Assessment ?? "Not recorded", row?.Rationale ?? string.Empty,
                issues.Count > 0, locations.TryGetValue(candidate.Id, out var line) ? line : null, issues)
            {
                RequiresReconciliation = row is null || row.Hash != candidate.ContentHash || candidate.MaterialSourceDrift,
                ReviewToken = locations.TryGetValue(candidate.Id, out var rowLine)
                    ? Hash(candidate.Id + "\n" + candidate.ContentHash + "\n" + lines[rowLine - 1].TrimEnd('\r')) : null,
                Summary = authorityPath is null ? null : BrdSourceSummaryService.Describe(authorityPath, candidate),
            };
        }).ToArray();
    }
}
