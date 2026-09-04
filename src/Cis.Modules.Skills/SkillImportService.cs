using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Skills;

public sealed class SkillImportService
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".cis", ".codex-tmp", "node_modules", "bin", "obj", "skills-quarantine",
    };

    private const long MaximumDownloadBytes = 100 * 1024 * 1024;
    private const long MaximumExpandedBytes = 250 * 1024 * 1024;
    private const int MaximumArchiveEntries = 10_000;
    private const int MaximumBundleFiles = 2_000;
    private readonly HttpClient _httpClient;
    private readonly ICisRepositoryContextResolver _repositoryContextResolver;
    private readonly SkillValidationService _validationService;

    public SkillImportService(
        ICisRepositoryContextResolver repositoryContextResolver,
        SkillValidationService validationService,
        HttpClient? httpClient = null)
    {
        _repositoryContextResolver = repositoryContextResolver;
        _validationService = validationService;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public SkillImportResult Import(SkillImportRequest request)
    {
        var resolution = _repositoryContextResolver.Resolve(request.RepositoryPath);
        if (!resolution.IsSuccess)
        {
            return Result("invalid", 2, null, request, [], [], [], resolution.Errors);
        }

        if (request.Sources.Count == 0)
        {
            return Result("invalid", 2, resolution.Context!.RepositoryPath, request, [], [], [],
                ["At least one --source path or URL is required."]);
        }

        var context = resolution.Context!;
        var stagingRoot = Path.Combine(Path.GetTempPath(), "cis-skill-import", Guid.NewGuid().ToString("N"));
        var warnings = new List<string>();
        var errors = new List<string>();
        var conflicts = new List<string>();
        try
        {
            Directory.CreateDirectory(stagingRoot);
            var validationRepository = CreateValidationRepository(stagingRoot);
            var stagedSkillsRoot = Path.Combine(validationRepository, ".github", "skills");
            Directory.CreateDirectory(stagedSkillsRoot);
            var sourceByName = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var source in request.Sources.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                MaterializeSource(source, context.RepositoryPath, stagingRoot, stagedSkillsRoot,
                    sourceByName, warnings, errors);
            }

            if (errors.Count > 0)
            {
                return Result("invalid", 2, context.RepositoryPath, request, [], warnings, conflicts, errors);
            }

            var validation = _validationService.Validate(validationRepository, request.Strict, request.Fix);
            warnings.AddRange(validation.Diagnostics
                .Where(item => item.Severity == "warning")
                .Select(item => $"{item.Code}: {item.Message} [{item.Path}]"));
            errors.AddRange(validation.Diagnostics
                .Where(item => item.Severity == "error")
                .Select(item => $"{item.Code}: {item.Message} [{item.Path}]"));
            if (validation.ExitCode != 0)
            {
                return Result("invalid", 2, context.RepositoryPath, request, [], warnings, conflicts, errors);
            }

            var items = new List<SkillImportItem>();
            foreach (var skill in validation.Skills)
            {
                var stagedDirectory = Path.Combine(stagedSkillsRoot, skill.Name);
                var destination = Path.Combine(context.RepositoryPath, ".github", "skills", skill.Name);
                var hash = HashDirectory(stagedDirectory);
                var relativeDestination = Normalize(Path.GetRelativePath(context.RepositoryPath, destination));
                var status = "create";
                if (Directory.Exists(destination))
                {
                    if (string.Equals(hash, HashDirectory(destination), StringComparison.Ordinal))
                    {
                        status = "unchanged";
                    }
                    else
                    {
                        status = "conflict";
                        conflicts.Add($"Skill '{skill.Name}' already exists with different content at {relativeDestination}.");
                    }
                }

                items.Add(new SkillImportItem(
                    skill.Name,
                    sourceByName.GetValueOrDefault(skill.Name, "unknown"),
                    relativeDestination,
                    hash,
                    status,
                    CountBundleFiles(stagedDirectory)));
            }

            if (conflicts.Count > 0)
            {
                return Result("conflict", 4, context.RepositoryPath, request, items, warnings, conflicts, errors);
            }

            var creates = items.Where(item => item.Status == "create").ToArray();
            if (request.DryRun)
            {
                return Result("dry-run", 0, context.RepositoryPath, request, items, warnings, conflicts, errors);
            }

            if (creates.Length > 0 && !request.Confirmed)
            {
                return Result("confirmation-required", 3, context.RepositoryPath, request, items, warnings, conflicts, errors,
                    confirmationRequired: true);
            }

            foreach (var item in creates)
            {
                var sourceDirectory = Path.Combine(stagedSkillsRoot, item.Name);
                var destination = Path.Combine(context.RepositoryPath, item.Destination.Replace('/', Path.DirectorySeparatorChar));
                CopyBundleAtomically(sourceDirectory, destination);
            }

            WriteIndex(context, items);
            return Result(creates.Length == 0 ? "unchanged" : "imported", 0, context.RepositoryPath,
                request, items, warnings, conflicts, errors, applied: creates.Length > 0);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or HttpRequestException
            or InvalidDataException
            or TaskCanceledException)
        {
            errors.Add(exception.Message);
            return Result("invalid", 2, context.RepositoryPath, request, [], warnings, conflicts, errors);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
                try { Directory.Delete(stagingRoot, recursive: true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private void MaterializeSource(
        string source,
        string repositoryPath,
        string stagingRoot,
        string stagedSkillsRoot,
        IDictionary<string, string> sourceByName,
        ICollection<string> warnings,
        ICollection<string> errors)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == "http")
            throw new InvalidDataException("Remote skill sources must use HTTPS.");
        if (uri is not null && uri.Scheme == "https")
        {
            if (!string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("Remote skill source URLs cannot contain credentials.");
            var remote = ResolveRemote(uri);
            var archivePath = Path.Combine(stagingRoot, "download-" + Guid.NewGuid().ToString("N") + ".zip");
            Download(remote.ArchiveUri, archivePath);
            var extractionRoot = Path.Combine(stagingRoot, "archive-" + Guid.NewGuid().ToString("N"));
            ExtractArchive(archivePath, extractionRoot);
            var scanRoot = ResolveArchiveScanRoot(extractionRoot, remote.Subpath);
            StageDiscoveredSkills(scanRoot, source, stagedSkillsRoot, sourceByName, warnings, errors);
            return;
        }

        var localPath = Path.GetFullPath(Path.IsPathRooted(source)
            ? source
            : Path.Combine(repositoryPath, source));
        if (File.Exists(localPath) && string.Equals(Path.GetExtension(localPath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            var extractionRoot = Path.Combine(stagingRoot, "archive-" + Guid.NewGuid().ToString("N"));
            ExtractArchive(localPath, extractionRoot);
            StageDiscoveredSkills(extractionRoot, source, stagedSkillsRoot, sourceByName, warnings, errors);
            return;
        }

        if (File.Exists(localPath) && string.Equals(Path.GetFileName(localPath), "SKILL.md", StringComparison.OrdinalIgnoreCase))
        {
            localPath = Path.GetDirectoryName(localPath)!;
        }

        if (!Directory.Exists(localPath))
        {
            errors.Add($"Skill source does not exist or is not a supported URL/ZIP: {source}");
            return;
        }

        StageDiscoveredSkills(localPath, source, stagedSkillsRoot, sourceByName, warnings, errors);
    }

    private static RemoteSource ResolveRemote(Uri source)
    {
        if (!string.Equals(source.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            if (!source.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Remote skill sources must be GitHub repository URLs or direct HTTP(S) ZIP archives.");
            }

            return new RemoteSource(source, null);
        }

        var segments = source.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            throw new InvalidDataException("GitHub skill URLs must include an owner and repository.");
        }

        if (source.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return new RemoteSource(source, null);
        }

        var owner = segments[0];
        var repository = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? segments[1][..^4]
            : segments[1];
        if (!SafeGitHubSegment(owner) || !SafeGitHubSegment(repository))
            throw new InvalidDataException("GitHub skill owner and repository names contain unsupported characters.");
        var reference = "HEAD";
        string? subpath = null;
        if (segments.Length >= 4 && string.Equals(segments[2], "tree", StringComparison.OrdinalIgnoreCase))
        {
            reference = Uri.UnescapeDataString(segments[3]);
            subpath = segments.Length > 4 ? string.Join('/', segments.Skip(4)) : null;
        }
        else if (segments.Length > 2)
        {
            throw new InvalidDataException("Use a GitHub repository URL, a tree/<ref>/<path> URL, or a direct ZIP URL.");
        }

        var archive = new Uri($"https://github.com/{owner}/{repository}/archive/{Uri.EscapeDataString(reference)}.zip");
        return new RemoteSource(archive, subpath);
    }

    private void Download(Uri uri, string destination)
    {
        using var response = _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead)
            .GetAwaiter().GetResult();
        var finalUri = response.RequestMessage?.RequestUri ?? uri;
        if (!finalUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Skill download redirected to a non-HTTPS endpoint.");
        if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
        {
            throw new HttpRequestException($"Skill download failed with HTTP {(int)response.StatusCode} from {uri}.");
        }

        if (response.Content.Headers.ContentLength > MaximumDownloadBytes)
        {
            throw new InvalidDataException($"Skill archive exceeds the {MaximumDownloadBytes} byte download limit.");
        }

        using var input = response.Content.ReadAsStream();
        using var output = File.Create(destination);
        CopyWithLimit(input, output, MaximumDownloadBytes, "download");
    }

    private static void ExtractArchive(string archivePath, string destination)
        => CisArchiveSafety.ExtractZip(archivePath, destination, MaximumArchiveEntries, MaximumExpandedBytes);

    private static string ResolveArchiveScanRoot(string extractionRoot, string? subpath)
    {
        if (string.IsNullOrWhiteSpace(subpath))
        {
            return extractionRoot;
        }

        var topDirectories = Directory.EnumerateDirectories(extractionRoot).ToArray();
        var archiveRoot = topDirectories.Length == 1 ? topDirectories[0] : extractionRoot;
        if (!CisPathSafety.TryResolveUnderRoot(archiveRoot, subpath, out var resolved) || !Directory.Exists(resolved))
        {
            throw new InvalidDataException($"GitHub archive does not contain requested skill path '{subpath}'.");
        }

        return resolved;
    }

    private static void StageDiscoveredSkills(
        string scanRoot,
        string source,
        string stagedSkillsRoot,
        IDictionary<string, string> sourceByName,
        ICollection<string> warnings,
        ICollection<string> errors)
    {
        var skillFiles = EnumerateSkillFiles(scanRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (skillFiles.Length == 0)
        {
            errors.Add($"No SKILL.md files were found in source '{source}'.");
            return;
        }

        foreach (var skillFile in skillFiles)
        {
            var skillDirectory = Path.GetDirectoryName(skillFile)!;
            var name = Path.GetFileName(skillDirectory);
            if (sourceByName.TryGetValue(name, out var existingSource))
            {
                errors.Add($"Sources '{existingSource}' and '{source}' both contain skill directory '{name}'.");
                continue;
            }

            if (ContainsReparsePoint(skillDirectory))
            {
                errors.Add($"Skill '{name}' contains a symbolic link or reparse point and was rejected.");
                continue;
            }

            var destination = Path.Combine(stagedSkillsRoot, name);
            CopyDirectory(skillDirectory, destination);
            sourceByName[name] = source;
        }
    }

    private static IEnumerable<string> EnumerateSkillFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (ExcludedDirectories.Contains(Path.GetFileName(directory)))
            {
                continue;
            }

            var skill = Path.Combine(directory, "SKILL.md");
            if (File.Exists(skill))
            {
                yield return skill;
                continue;
            }

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                pending.Push(child);
            }
        }
    }

    private static bool ContainsReparsePoint(string root)
        => CisPathSafety.ContainsReparsePointInTree(root);

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        var files = CisPathSafety.EnumerateFiles(source)
            .Where(path => !Path.GetRelativePath(source, path).Split(Path.DirectorySeparatorChar)
                .Any(ExcludedDirectories.Contains))
            .ToArray();
        if (files.Length > MaximumBundleFiles)
        {
            throw new InvalidDataException($"Skill bundle '{Path.GetFileName(source)}' exceeds the {MaximumBundleFiles} file limit.");
        }

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private static void CopyBundleAtomically(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".cis-import-" + Guid.NewGuid().ToString("N");
        try
        {
            CopyDirectory(source, temporary);
            Directory.Move(temporary, destination);
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private SkillImportResult Result(
        string status,
        int exitCode,
        string? repositoryPath,
        SkillImportRequest request,
        IReadOnlyList<SkillImportItem> skills,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> conflicts,
        IReadOnlyList<string> errors,
        bool confirmationRequired = false,
        bool applied = false)
        => new(status, exitCode, repositoryPath, request.DryRun, confirmationRequired, applied,
            skills.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).ToArray(),
            conflicts.Distinct(StringComparer.Ordinal).ToArray(),
            errors.Distinct(StringComparer.Ordinal).ToArray());

    private static string CreateValidationRepository(string stagingRoot)
    {
        var repository = Path.Combine(stagingRoot, "validation-repository");
        Directory.CreateDirectory(Path.Combine(repository, ".cis"));
        Directory.CreateDirectory(Path.Combine(repository, "docs"));
        File.WriteAllText(Path.Combine(repository, ".cis", "repository.yml"),
            "schema_version: 1\nrepository:\n  id: skill-import-staging\ndocumentation_root: docs\n");
        return repository;
    }

    private static void WriteIndex(CisRepositoryContext context, IReadOnlyList<SkillImportItem> imported)
    {
        var skillsRoot = Path.Combine(context.RepositoryPath, ".github", "skills");
        var importedByName = imported.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var items = Directory.Exists(skillsRoot)
            ? Directory.EnumerateDirectories(skillsRoot)
                .Where(directory => File.Exists(Path.Combine(directory, "SKILL.md")))
                .Select(directory =>
                {
                    var name = Path.GetFileName(directory);
                    return new
                    {
                        name,
                        path = Normalize(Path.GetRelativePath(context.RepositoryPath, directory)),
                        hash = HashDirectory(directory),
                        source = importedByName.TryGetValue(name, out var item) ? Normalize(item.Source) : "repository",
                    };
                })
                .OrderBy(item => item.name, StringComparer.Ordinal)
                .ToArray()
            : [];
        var output = Path.Combine(context.RepositoryPath, ".cis", "local", "skills", "index.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temporary = output + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            repositoryId = context.RepositoryId,
            skills = items,
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        File.Move(temporary, output, overwrite: true);
    }

    private static string HashDirectory(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in CisPathSafety.EnumerateFiles(directory)
                     .OrderBy(path => Path.GetRelativePath(directory, path), StringComparer.Ordinal))
        {
            var relative = Normalize(Path.GetRelativePath(directory, file));
            hash.AppendData(Encoding.UTF8.GetBytes(relative + "\n"));
            using var stream = File.OpenRead(file);
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer.AsSpan(0, read));
            }
        }

        return "sha256:" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static int CountBundleFiles(string directory)
        => CisPathSafety.EnumerateFiles(directory).Count();

    private static void CopyWithLimit(Stream input, Stream output, long limit, string operation)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > limit)
            {
                throw new InvalidDataException($"Skill {operation} exceeds the {limit} byte limit.");
            }

            output.Write(buffer, 0, read);
        }
    }

    private static string Normalize(string value) => value.Replace('\\', '/');

    private static bool SafeGitHubSegment(string value) => value.Length is > 0 and <= 100
        && value is not "." and not ".."
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private sealed record RemoteSource(Uri ArchiveUri, string? Subpath);
}

public sealed record SkillImportRequest(
    string RepositoryPath,
    IReadOnlyList<string> Sources,
    bool DryRun,
    bool Confirmed,
    bool Fix,
    bool Strict);
