using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class BrdService
{
    public BrdResult AssessSources(string workspacePath, IReadOnlyList<CisBrdSourceDecision> decisions)
    {
        var discovery = Discover(workspacePath, validateGraph: false);
        if (discovery.ExitCode != 0) return Error("invalid", discovery, discovery.Errors);
        if (decisions.Count is < 1 or > 100 || decisions.Any(item => item is null)
            || decisions.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != decisions.Count)
            return Error("invalid", discovery, ["Supply between 1 and 100 unique source decisions."]);
        foreach (var decision in decisions)
        {
            if (decision.Assessment is not ("Adopted" or "Reference" or "Rejected")
                || string.IsNullOrWhiteSpace(decision.Reason) || decision.Reason.Length > 4096
                || PlaceholderPattern().IsMatch(decision.Reason)
                || decision.Reason.Contains("<!--", StringComparison.Ordinal) || decision.Reason.Contains("-->", StringComparison.Ordinal))
                return Error("invalid", discovery, [$"Choose Adopted, Reference or Rejected and enter a meaningful reason for {decision.Id} (up to 4096 characters, without HTML comment markers)."]);
        }
        var authority = _workspaceRegistry.Resolve(workspacePath).Workspace!.AuthorityRepository!;
        var canonical = CanonicalPath(authority);
        if (!File.Exists(canonical)) return Error("missing", discovery, ["Create the BRD before reviewing its sources."]);
        var lockPath = Path.Combine(authority.RepositoryPath, ".cis", "local", "brd-source-decisions.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        try
        {
            using var writeLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var content = File.ReadAllText(canonical);
            if (!HasManagedBlocks(content)) return Error("invalid", discovery, ["Refresh the BRD evidence before saving source decisions."]);
            var reviews = DescribeSourceReviews(content, discovery.Candidates, ParseSourceRows(content));
            var lines = content.Split('\n');
            foreach (var decision in decisions)
            {
                var review = reviews.SingleOrDefault(item => item.Id == decision.Id);
                if (review is null || review.AssessmentLine is null || review.RequiresReconciliation
                    || string.IsNullOrWhiteSpace(decision.ReviewToken) || decision.ReviewToken != review.ReviewToken)
                    return Error("conflict", discovery, [$"The source or decision changed for {decision.Id}. Refresh the wizard and review the current evidence. No decisions were saved."]);
                var row = lines[review.AssessmentLine.Value - 1];
                var separators = Enumerable.Range(0, row.Length)
                    .Where(index => row[index] == '|' && (index == 0 || row[index - 1] != '\\')).ToArray();
                if (separators.Length is not (7 or 8))
                    return Error("conflict", discovery, ["The source table changed. Refresh the wizard. No decisions were saved."]);
                lines[review.AssessmentLine.Value - 1] = row[..(separators[^3] + 1)]
                    + $" {Cell(decision.Assessment)} | {Cell(decision.Reason)} |" + (row.EndsWith('\r') ? "\r" : string.Empty);
            }
            var next = string.Join('\n', lines);
            if (next == content) return new("unchanged", discovery.WorkspacePath, authority.Id, discovery.CanonicalPath,
                null, null, [], [], Applied: false);
            if (File.ReadAllText(canonical) != content)
                return Error("conflict", discovery, ["The BRD changed while saving. Refresh the wizard. No decisions were saved."]);
            // Source assessments are managed provenance. Never alter business text, approval or baselines here.
            Write(canonical, next);
            return new("saved", discovery.WorkspacePath, authority.Id, discovery.CanonicalPath,
                null, null, [], [], Applied: true);
        }
        catch (IOException)
        {
            return Error("blocked", discovery, ["The BRD could not be saved. Another write may be running; retry when it finishes."]);
        }
    }
}
