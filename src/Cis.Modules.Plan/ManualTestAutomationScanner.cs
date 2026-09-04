using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Plan;

internal sealed partial class ManualTestAutomationScanner
{
    private const long MaximumSourceBytes = 2 * 1024 * 1024;
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".fs", ".vb", ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs",
        ".py", ".java", ".kt", ".kts", ".swift", ".go", ".rs", ".rb", ".php", ".feature",
    };
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".codex-tmp", ".artifacts", "artifacts", "bin", "obj", "node_modules", ".vs", ".idea",
        ".next", "dist", "build", "coverage", "DerivedData", ".gradle", "build_out",
        "skills-quarantine", "_old", "nongit",
    };
    private static readonly HashSet<string> TestDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "test", "tests", "spec", "specs", "__tests__", "e2e", "integration-tests",
    };

    private readonly ICisRepositoryContextResolver _resolver;
    private readonly ICisWorkspaceRegistry? _workspaceRegistry;

    public ManualTestAutomationScanner(
        ICisRepositoryContextResolver resolver,
        ICisWorkspaceRegistry? workspaceRegistry)
    {
        _resolver = resolver;
        _workspaceRegistry = workspaceRegistry;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Scan(string repositoryPath)
    {
        var found = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var repository in ResolveRepositories(repositoryPath))
        {
            foreach (var path in EnumerateTestSources(repository.RepositoryPath))
            {
                string content;
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length > MaximumSourceBytes) continue;
                    content = File.ReadAllText(path);
                    if (!RecognizedTestPattern().IsMatch(content)) continue;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                var relative = Path.GetRelativePath(repository.RepositoryPath, path).Replace('\\', '/');
                foreach (Match match in TestCaseIdPattern().Matches(content))
                {
                    var id = match.Value.ToUpperInvariant();
                    if (!found.TryGetValue(id, out var references))
                    {
                        references = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                        found[id] = references;
                    }
                    references.Add($"{repository.Id}::{relative}:{LineNumber(content, match.Index)}");
                }
            }
        }

        return found.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<string>)item.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyList<RepositoryScope> ResolveRepositories(string repositoryPath)
    {
        if (_workspaceRegistry is not null)
        {
            var workspace = _workspaceRegistry.Resolve(repositoryPath);
            if (workspace.IsSuccess && workspace.Workspace is not null)
            {
                return workspace.Workspace.Repositories
                    .Select(item => new RepositoryScope(item.Id, item.RepositoryPath))
                    .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        var resolved = _resolver.Resolve(repositoryPath);
        return resolved.Context is null
            ? []
            : [new RepositoryScope(resolved.Context.RepositoryId, resolved.Context.RepositoryPath)];
    }

    private static IEnumerable<string> EnumerateTestSources(string repositoryPath)
    {
        var pending = new Stack<string>();
        pending.Push(repositoryPath);
        while (pending.TryPop(out var directory))
        {
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(directory)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                var relative = Path.GetRelativePath(repositoryPath, child).Replace('\\', '/');
                if (!IsExcluded(relative)
                    && !new DirectoryInfo(child).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    pending.Push(child);
                }
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var path in files)
            {
                var relative = Path.GetRelativePath(repositoryPath, path).Replace('\\', '/');
                if (IsTestSource(relative)) yield return path;
            }
        }
    }

    private static bool IsTestSource(string path)
    {
        if (!SourceExtensions.Contains(Path.GetExtension(path))) return false;
        var segments = path.Split('/');
        if (segments.Any(TestDirectoryNames.Contains)) return true;
        var name = Path.GetFileName(path);
        return name.Contains(".test.", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".spec.", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Test.cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Tests.fs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Test.java", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Tests.swift", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("_test.go", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("test_", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("_test.py", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".feature", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcluded(string path)
    {
        var segments = path.Split('/');
        for (var index = 0; index < segments.Length; index++)
        {
            if (ExcludedDirectories.Contains(segments[index])) return true;
            if (index > 0
                && segments[index - 1].Equals(".cis", StringComparison.OrdinalIgnoreCase)
                && segments[index].Equals("local", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static int LineNumber(string content, int index)
        => content.AsSpan(0, Math.Min(index, content.Length)).Count('\n') + 1;

    [GeneratedRegex(@"\bTC-[A-Z0-9]+(?:-[A-Z0-9]+)*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TestCaseIdPattern();

    [GeneratedRegex(@"(?:\[(?:Fact|Theory|Test|TestCase|TestMethod)(?:\([^\]]*\))?\]|\b(?:it|test|describe)\s*\(|^\s*(?:async\s+)?def\s+test_|@Test\b|^\s*func\s+(?:test|Test)[A-Za-z0-9_]*\s*\(|#\[test\]|^\s*Scenario(?: Outline)?:)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex RecognizedTestPattern();

    private sealed record RepositoryScope(string Id, string RepositoryPath);
}
