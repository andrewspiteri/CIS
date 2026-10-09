using Cis.Abstractions;

namespace Cis.Modules.Testing;

public sealed partial class TestingService
{
    private static string? CoveragePath(string repository, string relative, ICollection<string> diagnostics, string suite, bool resolve)
    {
        if (!relative.Contains('*', StringComparison.Ordinal)) return SafePath(repository, relative, diagnostics, suite, required: false);
        try
        {
            // Native collectors create a run-specific child directory. Deliberately support one
            // bounded descendant selector, not arbitrary filesystem globs or report synthesis.
            if (!CisCoverageSelector.TryResolveRoot(repository, relative, out var root, out var recursive))
                throw new InvalidDataException("Coverage selector must be a scoped .cis/local/<run>/*/coverage.cobertura.xml (or ** descendant) path without linked parents.");
            if (!resolve) return root;
            var files = Directory.Exists(root) ? CisPathSafety.EnumerateFiles(root).Take(10_001).ToArray() : [];
            if (files.Length > 10_000) throw new InvalidDataException("Coverage selection exceeds 10,000 files.");
            var matches = files.Where(path => Path.GetFileName(path) == "coverage.cobertura.xml"
                && (recursive || Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Length == 2)).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("Coverage selector must match exactly one native report; retain separate run/target directories.");
            return matches[0];
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            diagnostics.Add($"ERROR: Test suite '{suite}' coverage: {exception.Message}");
            return null;
        }
    }
}
