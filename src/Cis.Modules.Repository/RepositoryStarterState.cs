using System.Security.Cryptography;
using System.Text;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

/// <summary>Recognizes an untouched CIS scaffold, never an implemented repository.</summary>
public static class RepositoryStarterState
{
    public static bool IsUnchangedScaffold(CisWorkspaceRepository repository)
        => CisReadScope.Read(typeof(RepositoryStarterState), nameof(IsUnchangedScaffold), repository.RepositoryPath,
            () => Inspect(repository));

    private static bool Inspect(CisWorkspaceRepository repository)
    {
        var root = repository.RepositoryPath;
        try
        {
            if (CisPathSafety.IsReparsePoint(root)) return false;
            var manifestPath = Path.Combine(root, ".cis/starter-manifest.yml");
            if (CisPathSafety.ContainsReparsePoint(root, manifestPath)) return false;
            var read = new StarterManifestStore().Read(manifestPath);
            if (read.Errors.Count > 0 || read.Manifest is not { Components.Count: 0, ManagedArtifacts.Count: > 0 } manifest)
                return false;
            var expected = new Dictionary<string, string>(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var artifact in manifest.ManagedArtifacts)
            {
                if (artifact.Ownership != "managed"
                    || !CisPathSafety.TryResolveUnderRoot(root, artifact.Path, out var path)
                    || !expected.TryAdd(path, artifact.AppliedHash)) return false;
            }
            // These bootstrap files predate the managed-artifact manifest. Compare them
            // with the same deterministic generators as init, rather than ignoring them.
            var binding = new RepositoryStarterBinder().Bind(root, repository.Id, repository.DocumentationRoot,
                new RepositoryClassification("unclassified", [], []));
            CatalogArtifactEntry[] catalogEntries = [
                new($"{repository.Id}:docs:root", $"{repository.DocumentationRoot}/README.md", "navigation", "active", "routing"),
                .. binding.Artifacts.Where(item => item.CatalogEntry is not null).Select(item => item.CatalogEntry!)];
            expected[Path.GetFullPath(Path.Combine(root, ".cis/.gitignore"))] = Hash("local/\ncache/\nruns/\n");
            expected[Path.GetFullPath(Path.Combine(root, repository.DocumentationRoot, "README.md"))] = Hash(RepositoryInitializer.CreateDocumentationReadme(repository.Id));
            expected[Path.GetFullPath(Path.Combine(root, repository.DocumentationRoot, "catalog.yml"))] = Hash(new DocumentationCatalogMerger().Merge(repository.Id, null, catalogEntries).Content);
            var pending = new Stack<string>();
            pending.Push(root);
            var inspected = 0;
            while (pending.TryPop(out var directory))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (++inspected > 2000) return false;
                    var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                    // Git metadata and disposable CIS caches are not product content.
                    if (relative is ".git" or ".cis/local") continue;
                    var attributes = File.GetAttributes(entry);
                    if (attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
                    if (attributes.HasFlag(FileAttributes.Directory)) { pending.Push(entry); continue; }
                    if (relative is ".cis/repository.yml" or ".cis/starter-manifest.yml") continue;
                    if (!expected.Remove(entry, out var hash) || new FileInfo(entry).Length > 2_097_152) return false;
                    var actual = Hash(File.ReadAllText(entry));
                    if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) return false;
                }
            }
            return expected.Count == 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Missing, unreadable or modified evidence must enter the normal baseline checks.
            return false;
        }
    }

    private static string Hash(string content)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
