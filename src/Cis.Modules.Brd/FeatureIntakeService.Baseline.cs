using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.Repository;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    internal static bool IsIntakeSource(CisWorkspaceRepository repository, string relativePath)
        => relativePath.StartsWith(".cis/inputs/features/", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(repository.DocumentationRoot.TrimEnd('/') + "/specs/feature-requests/", StringComparison.OrdinalIgnoreCase);

    // A feature request reserves a new repository; it does not adopt that repository into
    // the existing product definition. Derive this only from verified intake + untouched
    // scaffold evidence, including older intakes, without rewriting signed documents.
    internal static IReadOnlySet<string> PendingRepositoryIds(CisWorkspace workspace)
    {
        var pending = new HashSet<string>(StringComparer.Ordinal);
        var authority = workspace.AuthorityRepository;
        if (authority is null) return pending;
        var root = Path.Combine(authority.RepositoryPath, authority.DocumentationRoot, "specs/feature-requests");
        if (!Directory.Exists(root) || !SafeAbsolutePath(root)) return pending;
        foreach (var directory in CisPathSafety.EnumerateDirectories(root, recursive: false))
        {
            var path = Path.Combine(directory, "request.md");
            try
            {
                if (!File.Exists(path) || !SafeAbsolutePath(path) || new FileInfo(path).Length > 2_097_152) continue;
                var content = File.ReadAllText(path);
                var header = Regex.Match(content, @"\A---\r?\n(?<header>.*?)\r?\n---",
                    RegexOptions.Singleline, TimeSpan.FromSeconds(1)).Groups["header"].Value;
                if (!Regex.IsMatch(header, @"(?m)^type: feature-intake\r?$", RegexOptions.None, TimeSpan.FromSeconds(1))
                    || !Regex.IsMatch(header, @"(?m)^status: Draft\r?$", RegexOptions.None, TimeSpan.FromSeconds(1))) continue;
                var record = ReadRecord(path);
                if (record?.Plan is not { RepositoryMode: "new" } plan
                    || !CisPathSafety.TryResolveUnderRoot(authority.RepositoryPath, plan.RequestPath, out var recordedPath)
                    || !SamePath(path, recordedPath)
                    || !CisPathSafety.TryResolveUnderRoot(authority.RepositoryPath, plan.SourcePath, out var source)
                    || !SafeAbsolutePath(source) || !File.Exists(source) || new FileInfo(source).Length > 2_097_152
                    || Hash(File.ReadAllBytes(source)) != plan.SourceHash) continue;
                var repository = workspace.Repositories.FirstOrDefault(item => item.Role == "participant" && item.IsProductOwned
                    && SamePath(item.RepositoryPath, plan.RepositoryPath) && item.DocumentationRoot == plan.DocumentationRoot);
                if (repository is not null && RepositoryStarterState.IsUnchangedScaffold(repository)) pending.Add(repository.Id);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                // Incomplete/invalid intake is not evidence to exclude a registered repository.
            }
        }
        return pending;
    }
}
