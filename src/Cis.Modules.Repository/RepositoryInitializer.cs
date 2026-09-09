using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cis.Modules.Repository;

public sealed partial class RepositoryInitializer : Cis.Abstractions.ICisObservedReferencePreparer
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly DocumentationCatalogMerger _catalogMerger;
    private readonly RepositoryClassifier _classifier;
    private readonly StarterManifestStore _manifestStore;
    private readonly RepositoryStarterBinder _starterBinder;

    public RepositoryInitializer()
        : this(
            new RepositoryClassifier(),
            new RepositoryStarterBinder(),
            new StarterManifestStore(),
            new DocumentationCatalogMerger())
    {
    }

    internal RepositoryInitializer(
        RepositoryClassifier classifier,
        RepositoryStarterBinder starterBinder,
        StarterManifestStore manifestStore,
        DocumentationCatalogMerger catalogMerger)
    {
        _classifier = classifier;
        _starterBinder = starterBinder;
        _manifestStore = manifestStore;
        _catalogMerger = catalogMerger;
    }

    public RepositoryInitResult Initialize(RepositoryInitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = CreatePlan(request);
        if (plan.Result.ExitCode != 0
            || request.DryRun
            || (plan.AbsoluteDirectories.Count == 0 && plan.Files.Count == 0 && plan.Moves.Count == 0))
        {
            return plan.Result;
        }

        foreach (var directory in plan.AbsoluteDirectories)
        {
            Directory.CreateDirectory(directory);
        }

        foreach (var move in plan.Moves)
        {
            if (!File.Exists(move.SourceAbsolutePath))
            {
                throw new IOException($"File changed after initialization planning: {move.SourceRelativePath}");
            }

            var currentHash = ComputeHash(File.ReadAllText(move.SourceAbsolutePath));
            if (!string.Equals(currentHash, move.SourceHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"File changed after initialization planning: {move.SourceRelativePath}");
            }

            if (File.Exists(move.TargetAbsolutePath) || Directory.Exists(move.TargetAbsolutePath))
            {
                throw new IOException($"Quarantine destination changed after initialization planning: {move.TargetRelativePath}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(move.TargetAbsolutePath)!);
            File.Move(move.SourceAbsolutePath, move.TargetAbsolutePath);
        }

        foreach (var file in plan.Files)
        {
            if (file.Action == PlannedFileAction.Create)
            {
                using var stream = new FileStream(
                    file.AbsolutePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(file.Content);
                continue;
            }

            var currentContent = File.ReadAllText(file.AbsolutePath);
            if (!string.Equals(currentContent, file.PreviousContent, StringComparison.Ordinal))
            {
                throw new IOException($"File changed after initialization planning: {file.RelativePath}");
            }

            File.WriteAllText(
                file.AbsolutePath,
                file.Content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return plan.Result with { Status = "initialized", Applied = true };
    }

    /// <summary>
    /// Adds only missing governed contract/reference starters for a documentation authority.
    /// Existing files are never rewritten, so this bounded reconciliation cannot collide with
    /// human-managed technical, product, workflow, skill, or standard content.
    /// </summary>
    public RepositoryReferenceSeedResult SeedAuthorityReferences(string repositoryPath, string documentationRoot)
    {
        try
        {
            var root = Path.GetFullPath(repositoryPath);
            var normalizedRoot = documentationRoot.Replace('\\', '/').Trim('/');
            if (Path.IsPathRooted(documentationRoot) || normalizedRoot.Length == 0
                || normalizedRoot.Split('/').Any(segment => segment is "" or "." or ".."))
                return new("invalid", [], [], ["Documentation root must be a contained repository-relative path."], false);
            var documentationPath = Path.GetFullPath(Path.Combine(root,
                normalizedRoot.Replace('/', Path.DirectorySeparatorChar)));
            if (!documentationPath.StartsWith(root + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return new("invalid", [], [], ["Documentation root escapes the authority repository."], false);

            var repositoryId = CreateRepositoryId(root);
            var classification = _classifier.Classify(root);
            var binding = _starterBinder.Bind(root, repositoryId, normalizedRoot, classification, workspaceAuthority: true);
            var selected = binding.Artifacts
                .Where(artifact => artifact.Definition.StartsWith("reference.", StringComparison.Ordinal))
                .ToArray();
            var catalogPath = Path.Combine(documentationPath, "catalog.yml");
            if (!File.Exists(catalogPath))
                return new("invalid", [], [], ["The documentation catalogue is missing."], false);
            var catalog = _catalogMerger.Merge(repositoryId, File.ReadAllText(catalogPath),
                selected.Where(artifact => artifact.CatalogEntry is not null).Select(artifact => artifact.CatalogEntry!).ToArray());
            if (catalog.Collisions.Count > 0)
                return new("collision", [], catalog.Collisions, [], false);

            var created = new List<string>();
            foreach (var artifact in selected)
            {
                var path = Path.GetFullPath(Path.Combine(root,
                    artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!path.StartsWith(root + Path.DirectorySeparatorChar,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    return new("invalid", created, [], [$"Starter reference path escapes the repository: {artifact.RelativePath}"], created.Count > 0);
                if (File.Exists(path)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(artifact.Content);
                created.Add(artifact.RelativePath);
            }
            if (catalog.Changed)
            {
                var temporary = catalogPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, catalog.Content, new UTF8Encoding(false));
                File.Move(temporary, catalogPath, true);
            }
            return new(created.Count > 0 || catalog.Changed ? "initialized" : "unchanged", created, [], [], created.Count > 0 || catalog.Changed);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new("invalid", [], [], [exception.Message], false);
        }
    }

    private InitializationPlan CreatePlan(RepositoryInitRequest request)
    {
        var errors = new List<string>();
        var collisions = new List<string>();
        var warnings = new List<string>();
        var retained = new List<string>();
        var directories = new List<PlannedDirectory>();
        var files = new List<PlannedFile>();
        var moves = new List<PlannedMove>();

        if (!TryResolvePaths(
                request,
                errors,
                out var repositoryPath,
                out var documentationPath,
                out var documentationRoot))
        {
            return InvalidPlan(errors, repositoryPath);
        }

        var existingContext = new CisRepositoryContextResolver().Resolve(repositoryPath!);
        var repositoryId = existingContext.Context?.RepositoryId ?? CreateRepositoryId(repositoryPath!);
        var classification = _classifier.Classify(repositoryPath!);
        warnings.AddRange(classification.Warnings);
        var binding = _starterBinder.Bind(
            repositoryPath!,
            repositoryId,
            documentationRoot!,
            classification,
            request.WorkspaceAuthority);
        var manifestPath = Path.Combine(repositoryPath!, ".cis", "starter-manifest.yml");
        var previousManifestResult = _manifestStore.Read(manifestPath);
        if (previousManifestResult.Errors.Count > 0)
        {
            collisions.AddRange(previousManifestResult.Errors);
        }

        var previousManifest = previousManifestResult.Manifest
            ?? new StarterManifest("unclassified", [], []);
        var previousArtifacts = previousManifest.ManagedArtifacts
            .Where(artifact => !string.IsNullOrWhiteSpace(artifact.Path))
            .GroupBy(artifact => artifact.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var nextManagedArtifacts = new Dictionary<string, ManagedStarterArtifact>(
            previousArtifacts,
            StringComparer.OrdinalIgnoreCase);

        var standardDirectories = CreateStandardDirectories(
            repositoryPath!,
            documentationPath!,
            binding.Artifacts.Select(artifact => artifact.RelativePath));
        PlanDirectories(repositoryPath!, standardDirectories, directories, retained, collisions);

        AddPreservedFile(
            repositoryPath!,
            Path.Combine(documentationPath!, "README.md"),
            CreateDocumentationReadme(repositoryId),
            files,
            retained,
            collisions);
        AddStrictFile(
            repositoryPath!,
            Path.Combine(repositoryPath!, ".cis", "repository.yml"),
            CreateRepositoryConfiguration(repositoryId, documentationRoot!),
            files,
            retained,
            collisions);
        AddPreservedFile(
            repositoryPath!,
            Path.Combine(repositoryPath!, ".cis", ".gitignore"),
            "local/\ncache/\nruns/\n",
            files,
            retained,
            collisions);

        foreach (var artifact in binding.Artifacts)
        {
            PlanManagedArtifact(
                repositoryPath!,
                artifact,
                previousArtifacts,
                nextManagedArtifacts,
                files,
                retained,
                collisions,
                request.AcceptCurrent);
        }

        foreach (var previous in previousManifest.ManagedArtifacts.Where(previous =>
                     !binding.Artifacts.Any(current =>
                         string.Equals(current.RelativePath, previous.Path, StringComparison.OrdinalIgnoreCase))))
        {
            var previousPath = Path.GetFullPath(Path.Combine(
                repositoryPath!,
                previous.Path.Replace('/', Path.DirectorySeparatorChar)));
            var repositoryPrefix = repositoryPath! + Path.DirectorySeparatorChar;
            if (!previousPath.StartsWith(repositoryPrefix, PathComparison))
            {
                collisions.Add($"Previously managed artifact path escapes the repository: {previous.Path}");
                continue;
            }

            if (!File.Exists(previousPath) && !Directory.Exists(previousPath))
            {
                nextManagedArtifacts.Remove(previous.Path);
                warnings.Add(
                    $"Previously managed artifact is missing and no longer selected; its manifest entry was removed: {previous.Path}");
                continue;
            }

            if (request.QuarantineObsolete)
            {
                PlanObsoleteQuarantine(
                    repositoryPath!,
                    previous,
                    previousPath,
                    nextManagedArtifacts,
                    moves,
                    retained,
                    warnings,
                    collisions);
                continue;
            }

            warnings.Add(
                $"Previously managed artifact is no longer selected and was retained: {previous.Path}");
        }

        var catalogPath = Path.Combine(documentationPath!, "catalog.yml");
        var catalogEntries = new List<CatalogArtifactEntry>
        {
            new(
                $"{repositoryId}:docs:root",
                $"{documentationRoot}/README.md",
                "navigation",
                "active",
                "routing"),
        };
        catalogEntries.AddRange(binding.Artifacts
            .Where(artifact => artifact.CatalogEntry is not null)
            .Select(artifact => artifact.CatalogEntry!));
        var existingCatalog = File.Exists(catalogPath) ? File.ReadAllText(catalogPath) : null;
        var catalogMerge = _catalogMerger.Merge(repositoryId, existingCatalog, catalogEntries);
        collisions.AddRange(catalogMerge.Collisions);
        if (existingCatalog is null)
        {
            files.Add(new PlannedFile(
                ToRepositoryPath(repositoryPath!, catalogPath),
                catalogPath,
                catalogMerge.Content,
                null,
                PlannedFileAction.Create));
        }
        else if (catalogMerge.Changed)
        {
            files.Add(new PlannedFile(
                ToRepositoryPath(repositoryPath!, catalogPath),
                catalogPath,
                catalogMerge.Content,
                existingCatalog,
                PlannedFileAction.Update));
        }
        else
        {
            retained.Add(ToRepositoryPath(repositoryPath!, catalogPath));
        }

        var nextManifest = new StarterManifest(
            classification.Shape,
            classification.Components,
            nextManagedArtifacts.Values
                .OrderBy(artifact => artifact.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        var manifestContent = _manifestStore.Write(nextManifest);
        AddOwnedStateFile(
            repositoryPath!,
            manifestPath,
            manifestContent,
            previousManifestResult.Errors.Count == 0,
            files,
            retained,
            collisions);

        var scanPath = Path.Combine(repositoryPath!, ".cis", "local", "init", "scan.json");
        var scanContent = JsonSerializer.Serialize(
            classification,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            }) + "\n";
        AddOwnedStateFile(
            repositoryPath!,
            scanPath,
            scanContent,
            previousStateValid: true,
            files,
            retained,
            collisions);

        var existingNonEmptyRoot = Directory.Exists(documentationPath)
            && Directory.EnumerateFileSystemEntries(documentationPath).Any();
        var hasChanges = directories.Count > 0 || files.Count > 0 || moves.Count > 0;
        var filesToCreate = files
            .Where(file => file.Action == PlannedFileAction.Create)
            .Select(file => file.RelativePath)
            .ToArray();
        var filesToUpdate = files
            .Where(file => file.Action == PlannedFileAction.Update)
            .Select(file => file.RelativePath)
            .ToArray();
        var requiresReview = existingNonEmptyRoot
            || classification.Components.Count > 0
            || filesToUpdate.Length > 0;
        var confirmationRequired = collisions.Count == 0
            && hasChanges
            && requiresReview
            && !request.DryRun
            && !request.Confirmed;
        var status = collisions.Count > 0
            ? "collision"
            : confirmationRequired
                ? "confirmation-required"
                : request.DryRun
                    ? "dry-run"
                    : hasChanges
                        ? "planned"
                        : "unchanged";

        var result = new RepositoryInitResult(
            status,
            repositoryPath,
            documentationRoot,
            classification,
            binding.Selections,
            directories.Select(item => item.RelativePath).ToArray(),
            filesToCreate,
            filesToUpdate,
            moves.Select(move => $"{move.SourceRelativePath} -> {move.TargetRelativePath}").ToArray(),
            retained.Distinct(StringComparer.Ordinal).Order().ToArray(),
            warnings.Distinct(StringComparer.Ordinal).Order().ToArray(),
            collisions.Distinct(StringComparer.Ordinal).Order().ToArray(),
            errors,
            confirmationRequired,
            Applied: false);

        return new InitializationPlan(
            result,
            directories.Select(item => item.AbsolutePath).ToArray(),
            files,
            moves);
    }

    private static void PlanObsoleteQuarantine(
        string repositoryPath,
        ManagedStarterArtifact previous,
        string previousPath,
        IDictionary<string, ManagedStarterArtifact> nextManagedArtifacts,
        ICollection<PlannedMove> moves,
        ICollection<string> retained,
        ICollection<string> warnings,
        ICollection<string> collisions)
    {
        if (Directory.Exists(previousPath))
        {
            collisions.Add($"Previously managed artifact is a directory and cannot be quarantined safely: {previous.Path}");
            return;
        }

        var currentHash = ComputeHash(File.ReadAllText(previousPath));
        if (!string.Equals(previous.Ownership, "managed", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(currentHash, previous.AppliedHash, StringComparison.OrdinalIgnoreCase))
        {
            retained.Add(previous.Path);
            warnings.Add(
                $"Previously managed artifact is no longer selected but was retained because it is human-owned or edited: {previous.Path}");
            return;
        }

        var targetRelativePath = ".cis/quarantine/repository-init/" + previous.Path.TrimStart('/');
        var targetAbsolutePath = Path.GetFullPath(Path.Combine(
            repositoryPath,
            targetRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var repositoryPrefix = repositoryPath + Path.DirectorySeparatorChar;
        if (!targetAbsolutePath.StartsWith(repositoryPrefix, PathComparison))
        {
            collisions.Add($"Quarantine path escapes the repository: {previous.Path}");
            return;
        }

        if (File.Exists(targetAbsolutePath) || Directory.Exists(targetAbsolutePath))
        {
            collisions.Add($"Quarantine destination already exists: {targetRelativePath}");
            return;
        }

        moves.Add(new PlannedMove(
            previous.Path,
            previousPath,
            targetRelativePath,
            targetAbsolutePath,
            currentHash));
        nextManagedArtifacts.Remove(previous.Path);
    }

    private static bool TryResolvePaths(
        RepositoryInitRequest request,
        ICollection<string> errors,
        out string? repositoryPath,
        out string? documentationPath,
        out string? documentationRoot)
    {
        repositoryPath = null;
        documentationPath = null;
        documentationRoot = null;
        if (string.IsNullOrWhiteSpace(request.RepositoryPath))
        {
            errors.Add("Repository path is required.");
            return false;
        }

        try
        {
            repositoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.RepositoryPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors.Add($"Repository path is invalid: {exception.Message}");
            return false;
        }

        if (!Directory.Exists(repositoryPath))
        {
            errors.Add($"Repository directory does not exist: {repositoryPath}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.DocumentationRoot))
        {
            errors.Add("Documentation root is required.");
            return false;
        }

        if (Path.IsPathRooted(request.DocumentationRoot))
        {
            errors.Add("Documentation root must be relative to the repository.");
            return false;
        }

        try
        {
            documentationPath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.Combine(repositoryPath, request.DocumentationRoot)));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors.Add($"Documentation root is invalid: {exception.Message}");
            return false;
        }

        var repositoryPrefix = repositoryPath + Path.DirectorySeparatorChar;
        if (string.Equals(documentationPath, repositoryPath, PathComparison))
        {
            errors.Add("Documentation root must not resolve to the repository itself.");
            return false;
        }

        if (!documentationPath.StartsWith(repositoryPrefix, PathComparison))
        {
            errors.Add("Documentation root must not escape the repository.");
            return false;
        }

        documentationRoot = ToRepositoryPath(repositoryPath, documentationPath);
        return true;
    }

    private static IReadOnlyList<string> CreateStandardDirectories(
        string repositoryPath,
        string documentationPath,
        IEnumerable<string> artifactPaths)
    {
        var directories = new List<string>();
        AddAncestors(documentationPath, repositoryPath, directories);
        directories.AddRange(
        [
            Path.Combine(documentationPath, "architecture"),
            Path.Combine(documentationPath, "architecture", "decisions"),
            Path.Combine(documentationPath, "specs"),
            Path.Combine(documentationPath, "standards"),
            Path.Combine(documentationPath, "references"),
            Path.Combine(documentationPath, "changes"),
            Path.Combine(repositoryPath, ".cis"),
            Path.Combine(repositoryPath, ".cis", "local"),
            Path.Combine(repositoryPath, ".cis", "local", "init"),
        ]);

        foreach (var artifactPath in artifactPaths)
        {
            var absolutePath = Path.GetFullPath(Path.Combine(
                repositoryPath,
                artifactPath.Replace('/', Path.DirectorySeparatorChar)));
            var parent = Path.GetDirectoryName(absolutePath);
            if (parent is not null)
            {
                AddAncestors(parent, repositoryPath, directories);
            }
        }

        return directories.Distinct(PathComparer).ToArray();
    }

    private static void AddAncestors(
        string path,
        string repositoryPath,
        ICollection<string> directories)
    {
        for (var current = path;
             !string.Equals(current, repositoryPath, PathComparison);
             current = Directory.GetParent(current)?.FullName
                 ?? throw new InvalidOperationException("Generated path has no repository ancestor."))
        {
            directories.Add(current);
        }
    }

    private static void PlanDirectories(
        string repositoryPath,
        IEnumerable<string> expectedDirectories,
        ICollection<PlannedDirectory> directories,
        ICollection<string> retained,
        ICollection<string> collisions)
    {
        foreach (var directory in expectedDirectories.Order(PathComparer))
        {
            var relativePath = ToRepositoryPath(repositoryPath, directory);
            if (File.Exists(directory))
            {
                collisions.Add($"{relativePath} is a file but must be a directory.");
            }
            else if (Directory.Exists(directory))
            {
                retained.Add(relativePath + "/");
            }
            else
            {
                directories.Add(new PlannedDirectory(relativePath, directory));
            }
        }
    }

    private static void PlanManagedArtifact(
        string repositoryPath,
        RepositoryStarterArtifact artifact,
        IReadOnlyDictionary<string, ManagedStarterArtifact> previousArtifacts,
        IDictionary<string, ManagedStarterArtifact> nextArtifacts,
        ICollection<PlannedFile> files,
        ICollection<string> retained,
        ICollection<string> collisions,
        bool acceptCurrent)
    {
        var absolutePath = Path.GetFullPath(Path.Combine(
            repositoryPath,
            artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var expectedHash = ComputeHash(artifact.Content);
        var next = new ManagedStarterArtifact(
            artifact.Id,
            artifact.RelativePath,
            artifact.Definition,
            TemplateVersion: 1,
            expectedHash,
            Ownership: "managed");

        if (Directory.Exists(absolutePath))
        {
            collisions.Add($"{artifact.RelativePath} is a directory but must be a file.");
            return;
        }

        if (!File.Exists(absolutePath))
        {
            files.Add(new PlannedFile(
                artifact.RelativePath,
                absolutePath,
                artifact.Content,
                null,
                PlannedFileAction.Create));
            nextArtifacts[artifact.RelativePath] = next;
            return;
        }

        var currentContent = File.ReadAllText(absolutePath);
        var currentHash = ComputeHash(currentContent);
        if (HasEquivalentContent(currentContent, artifact.Content))
        {
            retained.Add(artifact.RelativePath);
            nextArtifacts[artifact.RelativePath] = next;
            return;
        }

        if (artifact.Definition.Equals("reference.standards-conformance-matrix", StringComparison.Ordinal)
            && previousArtifacts.ContainsKey(artifact.RelativePath)
            && TryMergeConformanceRows(currentContent, artifact.Content, out var mergedContent))
        {
            if (!string.Equals(currentContent, mergedContent, StringComparison.Ordinal))
            {
                files.Add(new PlannedFile(
                    artifact.RelativePath,
                    absolutePath,
                    mergedContent,
                    currentContent,
                    PlannedFileAction.Update));
            }
            else
            {
                retained.Add(artifact.RelativePath);
            }

            nextArtifacts[artifact.RelativePath] = next with
            {
                AppliedHash = ComputeHash(mergedContent),
                Ownership = "human",
            };
            return;
        }

        // Some starter documents become canonical, human-governed authorities after a
        // dedicated CIS workflow has initialized them. Repository init must not mistake
        // that governed evolution for an arbitrary edit when its starter later changes.
        if (IsGovernedCanonicalEvolution(artifact, currentContent))
        {
            retained.Add(artifact.RelativePath);
            nextArtifacts[artifact.RelativePath] = next with
            {
                AppliedHash = currentHash,
                Ownership = "human",
            };
            return;
        }

        // Source evidence is seeded by init but becomes a human-governed append-only registry.
        // Repositories initialized before the seed was introduced may already have a valid
        // registry created by `cis references source import`; adopt only its bounded schema.
        if (!previousArtifacts.ContainsKey(artifact.RelativePath)
            && IsAdoptableDynamicArtifact(artifact, currentContent))
        {
            retained.Add(artifact.RelativePath);
            nextArtifacts[artifact.RelativePath] = next with
            {
                AppliedHash = currentHash,
                Ownership = "human",
            };
            return;
        }

        if (previousArtifacts.TryGetValue(artifact.RelativePath, out var owned)
            && string.Equals(owned.Ownership, "human", StringComparison.OrdinalIgnoreCase))
        {
            retained.Add(artifact.RelativePath);
            nextArtifacts[artifact.RelativePath] = owned with
            {
                Id = artifact.Id,
                Definition = artifact.Definition,
                TemplateVersion = 1,
                AppliedHash = currentHash,
            };
            return;
        }

        if (acceptCurrent)
        {
            retained.Add(artifact.RelativePath);
            nextArtifacts[artifact.RelativePath] = next with
            {
                AppliedHash = currentHash,
                Ownership = "human",
            };
            return;
        }

        if (!previousArtifacts.TryGetValue(artifact.RelativePath, out var previous))
        {
            collisions.Add($"{artifact.RelativePath} already exists and is not managed by CIS.");
            return;
        }

        if (string.Equals(expectedHash, previous.AppliedHash, StringComparison.OrdinalIgnoreCase))
        {
            retained.Add(artifact.RelativePath);
            nextArtifacts[artifact.RelativePath] = previous;
            return;
        }

        if (string.Equals(currentHash, previous.AppliedHash, StringComparison.OrdinalIgnoreCase))
        {
            files.Add(new PlannedFile(
                artifact.RelativePath,
                absolutePath,
                artifact.Content,
                currentContent,
                PlannedFileAction.Update));
            nextArtifacts[artifact.RelativePath] = next;
            return;
        }

        collisions.Add($"{artifact.RelativePath} was edited and also requires a generated update; merge it manually.");
    }

    private static bool IsGovernedCanonicalEvolution(RepositoryStarterArtifact artifact, string content)
    {
        if (!artifact.Definition.Equals("specification.technical-intent", StringComparison.Ordinal)) return false;

        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!Regex.IsMatch(
                normalized,
                "(?m)^\\s*technical_intent_schema:\\s*(?:2|3)\\s*$",
                RegexOptions.CultureInvariant))
            return false;

        return normalized.Contains("<!-- cis:technical-intent-baseline:start -->", StringComparison.Ordinal)
            && normalized.Contains("<!-- cis:technical-intent-baseline:end -->", StringComparison.Ordinal)
            && normalized.Contains("<!-- cis:technical-intent-business-evidence:start -->", StringComparison.Ordinal)
            && normalized.Contains("<!-- cis:technical-intent-business-evidence:end -->", StringComparison.Ordinal);
    }

    private static bool IsAdoptableDynamicArtifact(RepositoryStarterArtifact artifact, string content)
    {
        if (!artifact.Definition.Equals("reference.source-evidence", StringComparison.Ordinal)) return false;
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal)) return false;
        var frontmatterEnd = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (frontmatterEnd < 0) return false;
        var frontmatter = normalized[4..frontmatterEnd].Split('\n');
        if (!frontmatter.Any(line => line.Trim().Equals("type: source-evidence-registry", StringComparison.OrdinalIgnoreCase)))
            return false;

        var lines = normalized.Split('\n');
        var headerIndex = Array.FindIndex(lines, line =>
            SourceEvidenceCells(line) is ["ID", "Source path", "Format", "Registered SHA-256", "Assessment", "Actor", "Registered at (UTC)", "Rationale"]);
        if (headerIndex < 0 || headerIndex + 1 >= lines.Length || !IsMarkdownSeparator(lines[headerIndex + 1])) return false;
        foreach (var line in lines.Skip(headerIndex + 2).Where(line => line.TrimStart().StartsWith('|')))
        {
            var cells = SourceEvidenceCells(line);
            if (cells.Length != 8
                || !Regex.IsMatch(cells[0], "^BRD-SRC-[A-Za-z0-9-]{6,80}$", RegexOptions.CultureInvariant)
                || string.IsNullOrWhiteSpace(cells[1])
                || cells[2] is not ("docx" or "md" or "txt" or "repository")
                || !Regex.IsMatch(cells[3], "^sha256:[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)
                || cells[4] is not ("Unreviewed" or "Reference" or "Adopted" or "Rejected")
                || string.IsNullOrWhiteSpace(cells[5])
                || !DateTimeOffset.TryParse(cells[6], out _)
                || string.IsNullOrWhiteSpace(cells[7]))
                return false;
        }
        return true;
    }

    private static string[] SourceEvidenceCells(string line)
        => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();

    private static bool IsMarkdownSeparator(string line)
    {
        var cells = SourceEvidenceCells(line);
        return cells.Length == 8 && cells.All(cell => cell.Length >= 3
            && cell.Trim(':').All(character => character == '-'));
    }

    private static bool TryMergeConformanceRows(string current, string generated, out string merged)
    {
        merged = current;
        if (!current.Contains("| Standard ID | Rule ID |", StringComparison.OrdinalIgnoreCase))
            return false;

        static (string Key, string Line)? ParseRuleRow(string line)
        {
            if (!line.TrimStart().StartsWith('|')) return null;
            var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length < 2
                || cells[0].Equals("Standard ID", StringComparison.OrdinalIgnoreCase)
                || cells.All(cell => cell.All(character => character is '-' or ':' or ' '))
                || !cells[0].Contains(":standard:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return ($"{cells[0]}|{cells[1]}", line.TrimEnd());
        }

        var currentKeys = current.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseRuleRow).Where(row => row is not null)
            .Select(row => row!.Value.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = generated.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseRuleRow).Where(row => row is not null && !currentKeys.Contains(row.Value.Key))
            .Select(row => row!.Value.Line).ToArray();
        if (missing.Length == 0) return true;

        var newline = current.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        merged = current.TrimEnd('\r', '\n') + newline + string.Join(newline, missing) + newline;
        return true;
    }

    private static void AddPreservedFile(
        string repositoryPath,
        string absolutePath,
        string content,
        ICollection<PlannedFile> files,
        ICollection<string> retained,
        ICollection<string> collisions)
    {
        AddFile(repositoryPath, absolutePath, content, preserveExistingContent: true, files, retained, collisions);
    }

    private static void AddStrictFile(
        string repositoryPath,
        string absolutePath,
        string content,
        ICollection<PlannedFile> files,
        ICollection<string> retained,
        ICollection<string> collisions)
    {
        AddFile(repositoryPath, absolutePath, content, preserveExistingContent: false, files, retained, collisions);
    }

    private static void AddFile(
        string repositoryPath,
        string absolutePath,
        string content,
        bool preserveExistingContent,
        ICollection<PlannedFile> files,
        ICollection<string> retained,
        ICollection<string> collisions)
    {
        var relativePath = ToRepositoryPath(repositoryPath, absolutePath);
        if (Directory.Exists(absolutePath))
        {
            collisions.Add($"{relativePath} is a directory but must be a file.");
            return;
        }

        if (!File.Exists(absolutePath))
        {
            files.Add(new PlannedFile(relativePath, absolutePath, content, null, PlannedFileAction.Create));
            return;
        }

        var existing = File.ReadAllText(absolutePath);
        if (preserveExistingContent || HasEquivalentContent(existing, content))
        {
            retained.Add(relativePath);
            return;
        }

        collisions.Add($"{relativePath} already exists with different content.");
    }

    private static void AddOwnedStateFile(
        string repositoryPath,
        string absolutePath,
        string content,
        bool previousStateValid,
        ICollection<PlannedFile> files,
        ICollection<string> retained,
        ICollection<string> collisions)
    {
        var relativePath = ToRepositoryPath(repositoryPath, absolutePath);
        if (Directory.Exists(absolutePath))
        {
            collisions.Add($"{relativePath} is a directory but must be a file.");
            return;
        }

        if (!File.Exists(absolutePath))
        {
            files.Add(new PlannedFile(relativePath, absolutePath, content, null, PlannedFileAction.Create));
            return;
        }

        var existing = File.ReadAllText(absolutePath);
        if (HasEquivalentContent(existing, content))
        {
            retained.Add(relativePath);
        }
        else if (previousStateValid)
        {
            files.Add(new PlannedFile(relativePath, absolutePath, content, existing, PlannedFileAction.Update));
        }
    }

    private static InitializationPlan InvalidPlan(
        IReadOnlyList<string> errors,
        string? repositoryPath = null)
    {
        return new InitializationPlan(
            new RepositoryInitResult(
                "invalid",
                repositoryPath,
                null,
                null,
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                errors,
                ConfirmationRequired: false,
                Applied: false),
            [],
            [],
            []);
    }

    private static bool HasEquivalentContent(string actual, string expected)
    {
        static string Normalize(string value)
            => value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

        return string.Equals(Normalize(actual), Normalize(expected), StringComparison.Ordinal);
    }

    private static string ComputeHash(string content)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static string ToRepositoryPath(string repositoryPath, string absolutePath)
        => Path.GetRelativePath(repositoryPath, absolutePath).Replace('\\', '/');

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal static string CreateRepositoryId(string repositoryPath)
    {
        var name = Path.GetFileName(repositoryPath).ToLowerInvariant();
        var id = InvalidRepositoryIdCharacters().Replace(name, "-").Trim('-');
        return string.IsNullOrWhiteSpace(id) ? "repository" : id;
    }

    private static string CreateDocumentationReadme(string repositoryId) =>
        "---\n" +
        $"title: \"{repositoryId} Engineering Documentation\"\n" +
        "type: navigation\nstatus: Active\nowner: Repository maintainer\n" +
        "review_cadence: on change\ncis:\n" +
        $"  stable_id: {repositoryId}:docs:root\n---\n\n" +
        $"# {repositoryId} engineering documentation\n\n" +
        "This directory is the canonical documentation root managed by Change Impact Studio.\n\n" +
        "- `architecture/decisions/` - durable architecture decisions\n" +
        "- `specs/` - product and technical specifications\n" +
        "- `standards/` - normative, testable expectations for how governed work is performed\n" +
        "- `references/` - inventories, contracts, and lookup material\n" +
        "- `templates/` - reusable feature specification and architecture decision starters\n" +
        "- `changes/` - change dossiers and delivery evidence\n";

    private static string CreateRepositoryConfiguration(string repositoryId, string documentationRoot) =>
        "schema_version: 1\n\n" +
        "repository:\n" +
        $"  id: {repositoryId}\n\n" +
        $"documentation_root: {documentationRoot}\n";

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidRepositoryIdCharacters();

    private sealed record PlannedDirectory(string RelativePath, string AbsolutePath);

    private sealed record PlannedFile(
        string RelativePath,
        string AbsolutePath,
        string Content,
        string? PreviousContent,
        PlannedFileAction Action);

    private enum PlannedFileAction
    {
        Create,
        Update,
    }

    private sealed record PlannedMove(
        string SourceRelativePath,
        string SourceAbsolutePath,
        string TargetRelativePath,
        string TargetAbsolutePath,
        string SourceHash);

    private sealed record InitializationPlan(
        RepositoryInitResult Result,
        IReadOnlyList<string> AbsoluteDirectories,
        IReadOnlyList<PlannedFile> Files,
        IReadOnlyList<PlannedMove> Moves);
}
