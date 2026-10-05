using System.Text.Json;

namespace Cis.Abstractions;

/// <summary>Repository-owned document selections shared by readers, writers and approval snapshots.</summary>
public static class CisProductDocumentPaths
{
    public const string SelectionFile = ".cis/product-documents.json";
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["business"] = "specs/business-requirements.md",
        ["technical"] = "specs/technical-intent-spec.md",
        ["architecture"] = "architecture/overall-solution-design.md",
        ["components"] = "references/component-sheet.md",
        ["diagrams"] = "architecture/high-level-architecture-diagrams.md",
        ["experience"] = "design/ui-direction.md",
        ["delivery"] = "plans/high-level-backlog.md",
    };

    public static Dictionary<string, string> Read(string repository)
    {
        var file = Path.Combine(repository, SelectionFile);
        if (CisPathSafety.ContainsReparsePoint(repository, file))
            throw new InvalidDataException("Document selections cannot use symbolic links.");
        if (!File.Exists(file)) return new(StringComparer.Ordinal);
        var selections = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))
            ?? throw new InvalidDataException("Document selections must be a JSON object.");
        foreach (var (role, relative) in selections)
        {
            if (!Defaults.ContainsKey(role)) throw new InvalidDataException($"Unknown document role '{role}'.");
            ValidatePath(repository, relative);
        }
        if (selections.Values.Select(path => ValidatePath(repository, path)).Distinct(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Count() != selections.Count)
            throw new InvalidDataException("Each document role must use a separate file.");
        return selections;
    }

    public static string ValidatePath(string repository, string relative)
    {
        if (!CisPathSafety.TryResolveUnderRoot(repository, relative, out var path)
            || relative.Replace('\\', '/').Split('/').Any(segment => segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
                || segment.Equals(".cis", StringComparison.OrdinalIgnoreCase))
            || CisPathSafety.ContainsReparsePoint(repository, path)
            || !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select a Markdown document inside the project, without symbolic links.");
        return path;
    }

    public static string Resolve(string documentationPath, params string[] segments)
    {
        var relative = Path.Combine(segments).Replace('\\', '/');
        var role = Defaults.FirstOrDefault(pair => pair.Value == relative).Key;
        if (role is null) return Path.Combine(documentationPath, Path.Combine(segments));
        // Stop at the nearest repository; never inherit another project's selections.
        for (var directory = new DirectoryInfo(Path.GetFullPath(documentationPath)); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, ".cis/repository.yml"))) continue;
            var selected = Read(directory.FullName);
            return selected.TryGetValue(role, out var path)
                ? ValidatePath(directory.FullName, path)
                : Path.Combine(documentationPath, Path.Combine(segments));
        }
        return Path.Combine(documentationPath, Path.Combine(segments));
    }
}
