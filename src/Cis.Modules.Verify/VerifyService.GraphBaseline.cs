using Cis.Abstractions;
using Cis.Modules.Change;

namespace Cis.Modules.Verify;

public sealed partial class VerifyService
{
    private IReadOnlyList<VerifyFileChange> GraphBaselineDiff(CisWorkspaceRepository repository,
        ChangeRepositoryBaseline? baseline, IReadOnlyList<string> nestedRoots, List<VerifyFinding> findings)
    {
        if (baseline?.CreationInputs is null)
        {
            findings.Add(new("error", "CIS-VERIFY-GRAPH-BASELINE-MISSING",
                "The graph baseline has no retained creation inventory. Restore the original evidence or explicitly authorize a new change baseline; current files cannot reconstruct history.", repository.Id));
            return [];
        }
        try
        {
            var context = _resolver.Resolve(repository.RepositoryPath).Context
                ?? throw new InvalidDataException("Repository context is unavailable.");
            if (context.RepositoryId != repository.Id || context.DocumentationRoot != repository.DocumentationRoot)
                throw new InvalidDataException("Repository registration does not match its configured identity or documentation root.");
            var initial = baseline.CreationInputs;
            if (initial.Count > 100_000 || initial.Any(input => input is null ||
                    !CisPathSafety.TryResolveUnderRoot(repository.RepositoryPath, input.Path, out _)
                    || input.Path.Contains('\\') || input.Digest is null || input.Digest.Length != 64 || !input.Digest.All(Uri.IsHexDigit))
                || initial.Select(input => input.Path).Distinct(StringComparer.Ordinal).Count() != initial.Count)
                throw new InvalidDataException("The retained creation inventory contains invalid or duplicate paths or hashes.");
            var before = initial.Where(input => !nestedRoots.Any(nested => CisPathSafety.IsUnderRoot(nested,
                Path.Combine(repository.RepositoryPath, input.Path), allowRoot: true))).ToDictionary(x => x.Path, StringComparer.Ordinal);
            var after = CisExecutionIdentity.CaptureInputs(context, includeDossiers: true, excludedRepositoryRoots: nestedRoots)
                .ToDictionary(x => x.Path, StringComparer.Ordinal);
            return before.Keys.Union(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Where(path => !before.TryGetValue(path, out var old) || !after.TryGetValue(path, out var current)
                    || !old.Digest.Equals(current.Digest, StringComparison.OrdinalIgnoreCase))
                .Select(path => new VerifyFileChange(!after.ContainsKey(path) ? "D" : before.ContainsKey(path) ? "M" : "A",
                    path, repository.Id, after.TryGetValue(path, out var current) ? current.Digest : "missing"))
                .ToArray();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            findings.Add(new("error", "CIS-VERIFY-GRAPH-BASELINE", error.Message, repository.Id));
            return [];
        }
    }
}
