using Cis.Abstractions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cis.Modules.Repository;

public sealed class WorkspaceRegistry : ICisWorkspaceRegistry
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly ICisRepositoryContextResolver _repositoryResolver;
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();
    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .DisableAliases()
        .Build();

    public WorkspaceRegistry(ICisRepositoryContextResolver repositoryResolver)
    {
        _repositoryResolver = repositoryResolver;
    }

    public CisWorkspaceResolution Resolve(string workspacePath)
        => Read(workspacePath, requireConfiguration: true);

    internal CisWorkspaceResolution ReadForImport(string workspacePath)
        => Read(workspacePath, requireConfiguration: false);

    internal string Serialize(string workspacePath, IReadOnlyList<CisWorkspaceRepository> repositories)
    {
        var source = new WorkspaceFile
        {
            SchemaVersion = 1,
            Repositories = repositories
                .OrderBy(repository => repository.Id, StringComparer.Ordinal)
                .Select(repository => new WorkspaceRepositoryFile
                {
                    Id = repository.Id,
                    Path = StorePath(workspacePath, repository.RepositoryPath),
                    DocumentationRoot = repository.DocumentationRoot,
                    Role = repository.Role,
                })
                .ToList(),
        };

        return _serializer.Serialize(source).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    internal static string ConfigurationPath(string workspacePath)
        => Path.Combine(workspacePath, ".cis", "workspace.yml");

    internal static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content, new System.Text.UTF8Encoding(false));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private CisWorkspaceResolution Read(string workspacePath, bool requireConfiguration)
    {
        var errors = new List<string>();
        if (!TryResolveWorkspace(workspacePath, errors, out var resolvedWorkspace))
        {
            return new CisWorkspaceResolution(null, errors);
        }

        var configurationPath = ConfigurationPath(resolvedWorkspace!);
        if (!File.Exists(configurationPath))
        {
            return requireConfiguration
                ? Failure($"CIS workspace configuration was not found: {configurationPath}")
                : new CisWorkspaceResolution(
                    new CisWorkspace(resolvedWorkspace!, configurationPath, []),
                    []);
        }

        WorkspaceFile? source;
        try
        {
            source = _deserializer.Deserialize<WorkspaceFile>(File.ReadAllText(configurationPath));
        }
        catch (Exception exception) when (exception is YamlException or IOException or UnauthorizedAccessException)
        {
            return Failure($"CIS workspace configuration is invalid: {exception.Message}");
        }

        if (source is null)
        {
            return Failure("CIS workspace configuration is empty.");
        }

        if (source.SchemaVersion != 1)
        {
            errors.Add($"Unsupported workspace schema version '{source.SchemaVersion}'.");
        }

        if (source.Repositories is null || source.Repositories.Count == 0)
        {
            errors.Add("Workspace configuration must register at least one repository.");
        }

        var repositories = new List<CisWorkspaceRepository>();
        foreach (var entry in source.Repositories ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry.Id)
                || string.IsNullOrWhiteSpace(entry.Path)
                || string.IsNullOrWhiteSpace(entry.DocumentationRoot))
            {
                errors.Add("Every workspace repository must define id, path, and documentation_root.");
                continue;
            }

            var role = string.IsNullOrWhiteSpace(entry.Role)
                ? "participant"
                : entry.Role.Trim().ToLowerInvariant();
            if (role is not ("authority" or "participant"))
            {
                errors.Add($"Workspace repository '{entry.Id}' has unsupported role '{entry.Role}'.");
                continue;
            }

            string repositoryPath;
            try
            {
                repositoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                    Path.IsPathRooted(entry.Path)
                        ? entry.Path
                        : Path.Combine(resolvedWorkspace!, entry.Path)));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errors.Add($"Workspace repository '{entry.Id}' has an invalid path: {exception.Message}");
                continue;
            }

            var resolution = _repositoryResolver.Resolve(repositoryPath);
            if (!resolution.IsSuccess || resolution.Context is null)
            {
                errors.AddRange(resolution.Errors.Select(error => $"Workspace repository '{entry.Id}': {error}"));
                continue;
            }

            if (!string.Equals(entry.Id, resolution.Context.RepositoryId, StringComparison.Ordinal))
            {
                errors.Add(
                    $"Workspace repository id '{entry.Id}' does not match configured id '{resolution.Context.RepositoryId}'.");
            }

            if (!string.Equals(
                    Normalize(entry.DocumentationRoot),
                    resolution.Context.DocumentationRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Workspace repository '{entry.Id}' documentation root does not match its repository configuration.");
            }

            repositories.Add(new CisWorkspaceRepository(
                resolution.Context.RepositoryId,
                resolution.Context.RepositoryPath,
                resolution.Context.DocumentationRoot,
                role));
        }

        foreach (var duplicate in repositories.GroupBy(repository => repository.Id, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            errors.Add($"Workspace repository id is duplicated: {duplicate.Key}");
        }

        foreach (var duplicate in repositories.GroupBy(repository => repository.RepositoryPath, PathComparer)
                     .Where(group => group.Count() > 1))
        {
            errors.Add($"Workspace repository path is duplicated: {duplicate.Key}");
        }

        if (repositories.Count(repository => repository.Role == "authority") > 1)
        {
            errors.Add("Workspace configuration may define only one authority repository.");
        }

        return errors.Count > 0
            ? new CisWorkspaceResolution(null, errors.Distinct(StringComparer.Ordinal).ToArray())
            : new CisWorkspaceResolution(
                new CisWorkspace(
                    resolvedWorkspace!,
                    configurationPath,
                    repositories.OrderBy(repository => repository.Id, StringComparer.Ordinal).ToArray()),
                []);
    }

    private static bool TryResolveWorkspace(
        string workspacePath,
        ICollection<string> errors,
        out string? resolvedWorkspace)
    {
        resolvedWorkspace = null;
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            errors.Add("Workspace path is required.");
            return false;
        }

        try
        {
            resolvedWorkspace = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspacePath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors.Add($"Workspace path is invalid: {exception.Message}");
            return false;
        }

        if (!Directory.Exists(resolvedWorkspace))
        {
            errors.Add($"Workspace directory does not exist: {resolvedWorkspace}");
            return false;
        }

        return true;
    }

    private static string StorePath(string workspacePath, string repositoryPath)
    {
        var relative = Path.GetRelativePath(workspacePath, repositoryPath);
        return Normalize(relative);
    }

    private static string Normalize(string value) => value.Replace('\\', '/').TrimEnd('/');

    private static CisWorkspaceResolution Failure(string error) => new(null, [error]);

    private sealed class WorkspaceFile
    {
        public int SchemaVersion { get; set; }

        public List<WorkspaceRepositoryFile>? Repositories { get; set; }
    }

    private sealed class WorkspaceRepositoryFile
    {
        public string? Id { get; set; }

        public string? Path { get; set; }

        public string? DocumentationRoot { get; set; }

        public string? Role { get; set; }
    }
}
