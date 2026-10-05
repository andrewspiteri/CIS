using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Definition;

public sealed partial class DefinitionWizardService
{
    private static readonly (string Role, string Title, string Pattern)[] DocumentRoles =
    [
        ("business", "Business requirements", @"\bbrd\b|business[- _]+requirements"),
        ("technical", "Technical intent", @"technical[- _]+intent|technical[- _]+direction"),
        ("architecture", "Solution architecture", @"solution[- _]+design|solution[- _]+architecture|architecture[- _]+(?:overview|documentation)|system[- _]+architecture"),
        ("components", "Component sheet", @"component[- _]+(?:sheet|catalog|model)"),
        ("diagrams", "Architecture diagrams", @"architecture[- _]+diagrams|\bc4\b|system[- _]+context[- _]+diagram"),
        ("experience", "UI direction", @"ui[- _]+direction|design[- _]+guidelines|wireframes[- _]+guide|design[- _]+system"),
        ("delivery", "High-level backlog", @"high[- _]+level[- _]+backlog|product[- _]+backlog|delivery[- _]+(?:plan|map)|roadmap"),
    ];
    private static readonly HashSet<string> DiscoveryExcluded = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".cis", ".github", ".codex", ".claude", ".agents", "skills", "node_modules", "bin", "obj",
        ".artifacts", "artifacts", ".next", "dist", "build", "tmp", "temp", ".tmp", ".temp", ".cache",
        "__pycache__", ".pytest_cache", ".mypy_cache", ".ruff_cache", ".stryker-tmp", "TestResults",
        "test-results", "playwright-report", "blob-report", "cards", "templates", "reviews",
    };

    public ExistingDocumentsResult Documents(string workspacePath, string? role = null, string? relativePath = null, string? sourcePath = null)
    {
        var workspace = _workspaces.Resolve(workspacePath);
        var authority = workspace.Workspace?.AuthorityRepository;
        if (!workspace.IsSuccess || authority is null)
            return new("invalid", null, [], [], workspace.Errors.Count > 0 ? workspace.Errors : ["Workspace authority is unavailable."], false);
        try
        {
            var selected = CisProductDocumentPaths.Read(authority.RepositoryPath);
            var applied = false;
            if (role is not null || relativePath is not null || sourcePath is not null)
            {
                if (role is null || (relativePath is null) == (sourcePath is null) || !CisProductDocumentPaths.Defaults.ContainsKey(role))
                    return new("invalid", authority.RepositoryPath, [], [], ["Choose a document role and either a project document or a Markdown file to import."], false);
                if (sourcePath is not null) relativePath = ImportDocument(authority, role, sourcePath);
                var absolute = CisProductDocumentPaths.ValidatePath(authority.RepositoryPath, relativePath!);
                if (!File.Exists(absolute)) return new("invalid", authority.RepositoryPath, [], [], ["The selected document does not exist."], false);
                if (File.GetAttributes(absolute).HasFlag(FileAttributes.ReadOnly))
                    return new("invalid", authority.RepositoryPath, [], [], ["The selected document is read-only."], false);
                var normalized = Normalize(Path.GetRelativePath(authority.RepositoryPath, absolute));
                if (selected.Any(pair => pair.Key != role && string.Equals(
                    CisProductDocumentPaths.ValidatePath(authority.RepositoryPath, pair.Value), absolute, CisPathSafety.PlatformComparison)))
                    return new("invalid", authority.RepositoryPath, [], [], ["This document is already loaded for another role."], false);
                selected[role] = normalized;
                WriteAtomic(Path.Combine(authority.RepositoryPath, CisProductDocumentPaths.SelectionFile), JsonSerializer.Serialize(selected, JsonOptions));
                applied = true;
            }
            var warnings = new List<string>();
            var candidates = DocumentRoles.ToDictionary(item => item.Role, _ => new List<ExistingDocumentCandidate>());
            var count = 0;
            foreach (var file in ExistingMarkdown(authority.RepositoryPath, warnings))
            {
                if (++count > 10_000) { warnings.Add("Search stopped after 10000 Markdown files. Use Browse to select another document."); break; }
                try
                {
                    using var reader = File.OpenText(file);
                    var buffer = new char[4096];
                    var text = new string(buffer, 0, reader.ReadBlock(buffer, 0, buffer.Length));
                    var titleMatch = Regex.Match(text, @"(?m)^#\s+(.+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                    var title = titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension(file);
                    var relative = Normalize(Path.GetRelativePath(authority.RepositoryPath, file));
                    var info = new FileInfo(file);
                    if (info.IsReadOnly) continue;
                    var summary = DocumentSummary(text);
                    foreach (var item in DocumentRoles)
                        if (Regex.IsMatch(Path.GetFileNameWithoutExtension(file).Replace('_', '-') + " " + title, item.Pattern,
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                            if (!selected.Any(pair => pair.Key != item.Role && string.Equals(pair.Value, relative, CisPathSafety.PlatformComparison)))
                                candidates[item.Role].Add(new(relative, title, info.Length, new DateTimeOffset(info.LastWriteTimeUtc), summary));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { warnings.Add($"Could not read '{Normalize(Path.GetRelativePath(authority.RepositoryPath, file))}': {exception.Message}"); }
            }
            return new(applied ? "loaded" : "discovered", authority.RepositoryPath,
                DocumentRoles.Select(item => new ExistingDocumentRole(item.Role, item.Title, selected.GetValueOrDefault(item.Role),
                    candidates[item.Role].OrderByDescending(candidate => DocumentRank(item.Role, candidate.Path))
                        .ThenBy(candidate => candidate.Path, StringComparer.Ordinal).ToArray())).ToArray(), warnings, [], applied);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or NotSupportedException)
        { return new("invalid", authority.RepositoryPath, [], [], [exception.Message], false); }
    }

    private static string ImportDocument(CisWorkspaceRepository authority, string role, string sourcePath)
    {
        if (!Path.IsPathFullyQualified(sourcePath) || !sourcePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a Markdown (.md) file using its full path. CIS will copy it into the project.");
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new InvalidDataException("The selected document does not exist.");
        // Imported files get a separate working copy, including when a previous import has
        // since been reconciled. Never overwrite a selected document or the supplied original.
        var relative = Normalize(Path.Combine(authority.DocumentationRoot, "imports", role, Guid.NewGuid().ToString("N"), Path.GetFileName(source)));
        var target = CisProductDocumentPaths.ValidatePath(authority.RepositoryPath, relative);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > 20 * 1024 * 1024) throw new InvalidDataException("The selected Markdown document exceeds the 20 MB import limit.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
        return relative;
    }

    private static int DocumentRank(string role, string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals(Path.GetFileName(CisProductDocumentPaths.Defaults[role]), StringComparison.OrdinalIgnoreCase)
            || role == "business" && new[] { "brd.md", "brd-spec.md", "business-requirements-document.md" }.Contains(name, StringComparer.OrdinalIgnoreCase))
            return 100;
        return path.Split('/').Any(segment => segment is "features" or "issues" or "issue_pack") ? 0 : 50;
    }

    private static string DocumentSummary(string text)
    {
        text = Regex.Replace(text, @"\A\uFEFF?---\r?\n[\s\S]*?\r?\n---(?:\r?\n|$)", "", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        text = Regex.Replace(text, @"<!--[\s\S]*?-->", "", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var lines = text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0
            && !line.StartsWith('#') && !line.StartsWith('|') && !line.StartsWith("```", StringComparison.Ordinal));
        var excerpt = string.Join(" ", lines);
        return excerpt.Length > 700 ? excerpt[..700] + "…" : excerpt;
    }

    private static IEnumerable<string> ExistingMarkdown(string directory, ICollection<string> warnings)
    {
        string[] files = [], directories = [];
        try
        {
            files = CisPathSafety.EnumerateFiles(directory, "*.md", false).ToArray();
            directories = CisPathSafety.EnumerateDirectories(directory, recursive: false).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { warnings.Add($"Could not search '{directory}': {exception.Message}"); }
        foreach (var file in files)
            if (Path.GetFileName(file) is not ("AGENTS.md" or "CLAUDE.md" or "SKILL.md")) yield return file;
        foreach (var child in directories)
            if (!DiscoveryExcluded.Contains(Path.GetFileName(child)))
                foreach (var file in ExistingMarkdown(child, warnings)) yield return file;
    }
}
