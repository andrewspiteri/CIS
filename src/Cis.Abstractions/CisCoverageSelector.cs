namespace Cis.Abstractions;

/// <summary>Shared grammar and containment checks for native collector report selectors.</summary>
public static class CisCoverageSelector
{
    public static bool TryResolveRoot(string repository, string selector, out string root, out bool recursive)
    {
        root = string.Empty;
        recursive = selector.Contains("/**/", StringComparison.Ordinal);
        var parts = selector.Split(recursive ? "/**/" : "/*/", StringSplitOptions.None);
        return parts.Length == 2 && !parts[0].Contains('*', StringComparison.Ordinal)
            && parts[1] == "coverage.cobertura.xml"
            && CisPathSafety.TryResolveUnderRoot(repository, parts[0], out root)
            && CisPathSafety.IsUnderRoot(Path.Combine(repository, ".cis/local"), root)
            && !CisPathSafety.ContainsReparsePoint(repository, root);
    }
}
