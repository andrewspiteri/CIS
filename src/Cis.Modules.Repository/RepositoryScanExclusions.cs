namespace Cis.Modules.Repository;

/// <summary>Import discovery boundary, applied before traversal or reading linked evidence.</summary>
internal static class RepositoryScanExclusions
{
    private static readonly HashSet<string> Directories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".cis", ".idea", ".vs", ".codex-tmp", ".stryker-tmp", ".next", ".nuxt", ".output",
        ".svelte-kit", ".terraform", "node_modules", "bin", "obj", "dist", "out", "artifacts", ".artifacts",
        "coverage", "build_out", "nongit", "skills-quarantine", "_old", "tmp", "temp", ".tmp", ".temp",
        "cache", ".cache", "__pycache__", ".pytest_cache", ".mypy_cache", ".ruff_cache",
        "test-results", "TestResults", "playwright-report", "blob-report",
    };

    internal static bool IsPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        return normalized.Split('/').Any(Directories.Contains)
            || normalized.StartsWith(".github/copilot-runtime/", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(".github/copilot-runtime", StringComparison.OrdinalIgnoreCase);
    }
}
