using Cis.Abstractions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cis.Modules.Repository;

public sealed class CisRepositoryContextResolver : ICisRepositoryContextResolver
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public CisRepositoryContextResolution Resolve(string repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            return Failure("Repository path is required.");
        }

        string resolvedRepository;
        try
        {
            resolvedRepository = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure($"Repository path is invalid: {exception.Message}");
        }

        if (!Directory.Exists(resolvedRepository))
        {
            return Failure($"Repository directory does not exist: {resolvedRepository}");
        }

        var configurationPath = Path.Combine(resolvedRepository, ".cis", "repository.yml");
        if (!File.Exists(configurationPath))
        {
            return Failure($"CIS repository configuration was not found: {configurationPath}");
        }

        RepositoryConfiguration? configuration;
        try
        {
            configuration = _deserializer.Deserialize<RepositoryConfiguration>(
                File.ReadAllText(configurationPath));
        }
        catch (Exception exception) when (exception is YamlException or IOException or UnauthorizedAccessException)
        {
            return Failure($"CIS repository configuration is invalid: {exception.Message}");
        }

        var errors = new List<string>();
        if (configuration is null)
        {
            errors.Add("CIS repository configuration is empty.");
        }
        else
        {
            if (configuration.SchemaVersion != 1)
            {
                errors.Add($"Unsupported repository schema version '{configuration.SchemaVersion}'.");
            }

            if (string.IsNullOrWhiteSpace(configuration.Repository?.Id))
            {
                errors.Add("Repository configuration must define repository.id.");
            }

            if (string.IsNullOrWhiteSpace(configuration.DocumentationRoot))
            {
                errors.Add("Repository configuration must define documentation_root.");
            }
            else if (Path.IsPathRooted(configuration.DocumentationRoot))
            {
                errors.Add("Configured documentation_root must be repository-relative.");
            }
        }

        if (errors.Count > 0 || configuration is null)
        {
            return new CisRepositoryContextResolution(null, errors);
        }

        string documentationPath;
        try
        {
            documentationPath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.Combine(resolvedRepository, configuration.DocumentationRoot)));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure($"Configured documentation_root is invalid: {exception.Message}");
        }
        var repositoryPrefix = resolvedRepository + Path.DirectorySeparatorChar;
        if (string.Equals(documentationPath, resolvedRepository, PathComparison)
            || !documentationPath.StartsWith(repositoryPrefix, PathComparison))
        {
            return Failure("Configured documentation_root must resolve inside the repository.");
        }

        if (!Directory.Exists(documentationPath))
        {
            return Failure($"Configured documentation root does not exist: {documentationPath}");
        }

        var normalizedRoot = Path.GetRelativePath(resolvedRepository, documentationPath).Replace('\\', '/');
        return new CisRepositoryContextResolution(
            new CisRepositoryContext(
                resolvedRepository,
                configuration.Repository!.Id!,
                normalizedRoot,
                documentationPath,
                Path.Combine(documentationPath, "catalog.yml")),
            []);
    }

    private static CisRepositoryContextResolution Failure(string error)
        => new(null, [error]);

    private sealed class RepositoryConfiguration
    {
        public int SchemaVersion { get; init; }

        public RepositoryIdentity? Repository { get; init; }

        public string DocumentationRoot { get; init; } = string.Empty;
    }

    private sealed class RepositoryIdentity
    {
        public string? Id { get; init; }
    }
}
