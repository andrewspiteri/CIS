using System.IO.Compression;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Artifacts;

public sealed class ArtifactRetentionService
{
    public const string ProfilePath = "references/local-artifact-retention.md";
    public const string StateRoot = ".cis/local/artifacts";
    private const int MaximumArchiveEntries = 100_000;
    private const long MaximumExpandedBytes = 20L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ICisRepositoryContextResolver _resolver;
    private readonly Func<DateTimeOffset> _clock;

    public ArtifactRetentionService(ICisRepositoryContextResolver resolver, Func<DateTimeOffset>? clock = null)
    {
        _resolver = resolver;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public ArtifactOperationResult Inventory(string repositoryPath, string? family = null)
    {
        var context = Resolve(repositoryPath, out var errors);
        if (context is null) return Failed(errors);
        var rules = ReadRules(context, errors);
        if (!string.IsNullOrWhiteSpace(family)) rules = rules.Where(rule => rule.Family.Equals(family, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (rules.Count == 0 && !string.IsNullOrWhiteSpace(family)) errors.Add($"Artifact family is not configured: {family}");
        var entries = rules.SelectMany(rule => ReadEntries(context, rule, errors)).ToArray();
        return Result(context, errors.Count == 0 ? "inventoried" : "invalid", rules, entries, ReadArchives(context), [], errors, false, reuseEntries: true);
    }

    public ArtifactOperationResult Plan(string repositoryPath, string? family = null)
    {
        var inventory = Inventory(repositoryPath, family);
        return inventory with { Status = inventory.Errors.Count == 0 ? "planned" : inventory.Status };
    }

    // Doctor only needs eligibility. Content digests remain mandatory in explicit plans
    // and destructive operations; a health check must not read every retained file.
    internal (IReadOnlyList<string> Candidates, IReadOnlyList<string> Errors) InspectRetention(string repositoryPath)
    {
        var context = Resolve(repositoryPath, out var errors);
        if (context is null) return ([], errors);
        var rules = ReadRules(context, errors);
        var candidates = new List<string>();
        foreach (var rule in rules)
        {
            if (rule.Action == "preserve" && rule.MaximumBytes is null)
            {
                _ = FullLocalPath(context, rule.Path, errors);
                continue;
            }
            candidates.AddRange(RankEntries(context, rule, errors, candidatesOnly: true)
                .Where(entry => entry.Candidate).Select(entry => Relative(context, entry.Metadata.Path)));
        }
        return (candidates, errors);
    }

    public ArtifactOperationResult Compact(string repositoryPath, string family, bool confirmed)
    {
        var context = Resolve(repositoryPath, out var errors);
        if (context is null) return Failed(errors);
        if (!confirmed) errors.Add("Compaction removes verified source entries after archiving; pass --yes after reviewing the plan.");
        var inventory = Inventory(repositoryPath, family);
        errors.AddRange(inventory.Errors);
        var rule = inventory.Rules.SingleOrDefault();
        if (rule is null) errors.Add($"Artifact family is not configured: {family}");
        else if (!rule.Action.Equals("archive", StringComparison.OrdinalIgnoreCase)) errors.Add($"Family '{family}' uses action '{rule.Action}', not archive.");
        var candidates = inventory.Entries.Where(entry => entry.Candidate).ToArray();
        if (errors.Count > 0) return Result(context, "failed", inventory.Rules, candidates, ReadArchives(context), [], errors, false);
        if (candidates.Length == 0) return Result(context, "unchanged", inventory.Rules, [], ReadArchives(context), [], [], false);

        var archiveDirectory = Path.Combine(context.RepositoryPath, StateRoot.Replace('/', Path.DirectorySeparatorChar), "archive");
        Directory.CreateDirectory(archiveDirectory);
        var archiveId = UniqueArchiveId(archiveDirectory,
            $"ARC-{_clock().ToUniversalTime():yyyyMMddHHmmss}-{ShortDigest(string.Join('\n', candidates.Select(item => item.Path + "\n" + item.Digest)))}");
        var zipPath = Path.Combine(archiveDirectory, archiveId + ".zip");
        var manifestPath = Path.Combine(archiveDirectory, archiveId + ".json");
        var temporary = zipPath + ".tmp";
        using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            foreach (var candidate in candidates) AddToArchive(context, archive, candidate);
        VerifyArchive(temporary, candidates, errors);
        if (errors.Count > 0) { File.Delete(temporary); return Result(context, "failed", inventory.Rules, candidates, ReadArchives(context), [], errors, false); }
        File.Move(temporary, zipPath);
        var manifest = new ArtifactArchiveManifest(1, archiveId, _clock().ToUniversalTime().ToString("O"), family,
            Relative(context, zipPath), Sha256File(zipPath), candidates);
        AtomicWrite(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine);
        foreach (var candidate in candidates)
            try { DeleteEntry(context, candidate.Path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            { errors.Add($"Archived but could not remove {candidate.Path}: {exception.Message}"); }
        return Result(context, errors.Count == 0 ? "compacted" : "partial", inventory.Rules, candidates, ReadArchives(context),
            [Relative(context, zipPath), Relative(context, manifestPath)], errors, true);
    }

    public ArtifactOperationResult Clean(string repositoryPath, string family, bool confirmed)
    {
        var context = Resolve(repositoryPath, out var errors);
        if (context is null) return Failed(errors);
        if (!confirmed) errors.Add("Cleanup deletes derived entries; pass --yes after reviewing the plan.");
        var inventory = Inventory(repositoryPath, family);
        errors.AddRange(inventory.Errors);
        var rule = inventory.Rules.SingleOrDefault();
        if (rule is null) errors.Add($"Artifact family is not configured: {family}");
        else if (!rule.Action.Equals("delete", StringComparison.OrdinalIgnoreCase)) errors.Add($"Family '{family}' uses action '{rule.Action}', not delete.");
        var candidates = inventory.Entries.Where(entry => entry.Candidate).ToArray();
        if (errors.Count > 0) return Result(context, "failed", inventory.Rules, candidates, ReadArchives(context), [], errors, false);
        if (candidates.Length == 0) return Result(context, "unchanged", inventory.Rules, [], ReadArchives(context), [], [], false);
        var id = $"CLEAN-{_clock().ToUniversalTime():yyyyMMddHHmmss}-{ShortDigest(string.Join('\n', candidates.Select(item => item.Path)))}";
        var recordPath = Path.Combine(context.RepositoryPath, StateRoot.Replace('/', Path.DirectorySeparatorChar), "cleanup", id + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(recordPath)!);
        File.WriteAllText(recordPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            cleanupId = id,
            createdUtc = _clock().ToUniversalTime().ToString("O"),
            family,
            entries = candidates
        }, JsonOptions) + Environment.NewLine);
        foreach (var candidate in candidates)
            try { DeleteEntry(context, candidate.Path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            { errors.Add($"Could not remove {candidate.Path}: {exception.Message}"); }
        return Result(context, errors.Count == 0 ? "cleaned" : "partial", inventory.Rules, candidates, ReadArchives(context), [Relative(context, recordPath)], errors, true);
    }

    public ArtifactOperationResult Archives(string repositoryPath)
    {
        var context = Resolve(repositoryPath, out var errors);
        return context is null ? Failed(errors) : Result(context, "listed", ReadRules(context, errors), [], ReadArchives(context), [], errors, false);
    }

    public ArtifactOperationResult Retrieve(string repositoryPath, string archiveId, string? entry = null)
    {
        var context = Resolve(repositoryPath, out var errors);
        if (context is null) return Failed(errors);
        var manifest = FindArchive(context, archiveId, errors);
        if (manifest is null) return Result(context, "failed", ReadRules(context, errors), [], ReadArchives(context), [], errors, false);
        var zipPath = FullLocalPath(context, manifest.ZipPath, errors);
        if (zipPath is null || !File.Exists(zipPath)) errors.Add("Archive ZIP is missing.");
        else if (!Sha256File(zipPath).Equals(manifest.ZipDigest, StringComparison.OrdinalIgnoreCase)) errors.Add("Archive ZIP digest does not match its manifest.");
        if (errors.Count == 0) VerifyArchive(zipPath!, manifest.Entries, errors);
        if (errors.Count > 0) return Result(context, "failed", ReadRules(context, errors), [], ReadArchives(context), [], errors, false);
        var selected = SelectEntries(manifest, entry, errors);
        if (errors.Count > 0) return Result(context, "failed", ReadRules(context, errors), [], ReadArchives(context), [], errors, false);
        var outputRoot = Path.Combine(context.RepositoryPath, StateRoot.Replace('/', Path.DirectorySeparatorChar), "retrieved", manifest.ArchiveId);
        var outputs = Extract(context, zipPath!, outputRoot, selected, overwrite: true, errors);
        return Result(context, errors.Count == 0 ? "retrieved" : "failed", ReadRules(context, errors), selected, ReadArchives(context), outputs, errors, outputs.Count > 0);
    }

    public ArtifactOperationResult Restore(string repositoryPath, string archiveId, bool confirmed)
    {
        var context = Resolve(repositoryPath, out var errors);
        if (context is null) return Failed(errors);
        if (!confirmed) errors.Add("Restore writes archived entries back to their original local paths; pass --yes after conflict review.");
        var manifest = FindArchive(context, archiveId, errors);
        if (manifest is null || errors.Count > 0) return Result(context, "failed", ReadRules(context, errors), [], ReadArchives(context), [], errors, false);
        var zipPath = FullLocalPath(context, manifest.ZipPath, errors);
        if (zipPath is null || !File.Exists(zipPath)) errors.Add("Archive ZIP is missing.");
        else if (!Sha256File(zipPath).Equals(manifest.ZipDigest, StringComparison.OrdinalIgnoreCase)) errors.Add("Archive ZIP digest does not match its manifest.");
        if (errors.Count == 0) VerifyArchive(zipPath!, manifest.Entries, errors);
        foreach (var archived in manifest.Entries)
        {
            var target = FullLocalPath(context, archived.Path, errors);
            if (target is null || !File.Exists(target) && !Directory.Exists(target)) continue;
            if (!HashEntry(target).Equals(archived.Digest, StringComparison.OrdinalIgnoreCase)) errors.Add($"Restore conflict: {archived.Path}");
        }
        if (errors.Count > 0) return Result(context, "failed", ReadRules(context, errors), manifest.Entries, ReadArchives(context), [], errors, false);
        var staging = Path.Combine(context.RepositoryPath, StateRoot.Replace('/', Path.DirectorySeparatorChar), "restore", manifest.ArchiveId);
        var outputs = Extract(context, zipPath!, staging, manifest.Entries, overwrite: true, errors);
        if (errors.Count == 0)
        {
            foreach (var archived in manifest.Entries)
            {
                var staged = Path.Combine(staging, archived.Path.Replace('/', Path.DirectorySeparatorChar));
                var target = FullLocalPath(context, archived.Path, errors)!;
                if (archived.Kind == "directory") Directory.CreateDirectory(target);
                if (File.Exists(staged) && !File.Exists(target)) { Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Move(staged, target); }
                else if (Directory.Exists(staged)) MoveDirectoryFiles(staged, target);
            }
            if (errors.Count == 0) outputs = manifest.Entries.Select(item => item.Path).ToArray();
        }
        try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { errors.Add($"Restored content but could not remove staging directory: {exception.Message}"); }
        return Result(context, errors.Count == 0 ? "restored" : "partial", ReadRules(context, errors), manifest.Entries, ReadArchives(context), outputs, errors, outputs.Count > 0);
    }

    private IReadOnlyList<ArtifactRetentionRule> ReadRules(CisRepositoryContext context, List<string> errors)
    {
        var path = Path.Combine(context.DocumentationPath, ProfilePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) { errors.Add($"Artifact retention profile is missing: {ProfilePath}"); return []; }
        var rules = new List<ArtifactRetentionRule>();
        foreach (var line in File.ReadLines(path))
        {
            if (!line.TrimStart().StartsWith('|')) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length < 5 || cells[0].Equals("Family", StringComparison.OrdinalIgnoreCase) || cells.All(cell => cell.All(ch => ch is '-' or ':' or ' '))) continue;
            long? maximumBytes = null;
            if (cells.Length > 6 || cells.Length == 6 && cells[5].Length > 0
                && (!long.TryParse(cells[5], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedBudget) || parsedBudget < 0))
            { errors.Add($"Invalid artifact byte budget: {line.Trim()}"); continue; }
            if (cells.Length == 6 && cells[5].Length > 0) maximumBytes = long.Parse(cells[5], CultureInfo.InvariantCulture);
            var familyPath = cells[1].Replace('\\', '/').TrimEnd('/');
            if (!int.TryParse(cells[2], out var days) || days < 0 || !int.TryParse(cells[3], out var latest) || latest < 0
                || familyPath.Contains("..", StringComparison.Ordinal) || !familyPath.StartsWith(".cis/local/", StringComparison.Ordinal)
                || familyPath.Equals(StateRoot, StringComparison.OrdinalIgnoreCase)
                || familyPath.StartsWith(StateRoot + "/", StringComparison.OrdinalIgnoreCase)
                || cells[4] is not ("preserve" or "archive" or "delete"))
            { errors.Add($"Invalid artifact retention row: {line.Trim()}"); continue; }
            rules.Add(new(cells[0], familyPath, days, latest, cells[4], maximumBytes));
        }
        foreach (var conflict in rules.GroupBy(rule => rule.Family, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)) errors.Add($"Duplicate artifact family: {conflict.Key}");
        for (var index = 0; index < rules.Count; index++)
            foreach (var other in rules.Skip(index + 1))
                if (rules[index].Path.Equals(other.Path, StringComparison.OrdinalIgnoreCase)
                    || rules[index].Path.StartsWith(other.Path + "/", StringComparison.OrdinalIgnoreCase)
                    || other.Path.StartsWith(rules[index].Path + "/", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Artifact families have overlapping paths: {rules[index].Family}, {other.Family}");
        return rules;
    }

    private IReadOnlyList<LocalArtifactEntry> ReadEntries(CisRepositoryContext context, ArtifactRetentionRule rule, List<string> errors)
        => RankEntries(context, rule, errors).Select(entry => new LocalArtifactEntry(
            rule.Family, Relative(context, entry.Metadata.Path), entry.Metadata.Kind,
            entry.Metadata.Size, HashEntry(entry.Metadata.Path), entry.Metadata.LastWrite.ToUniversalTime().ToString("O"),
            entry.Candidate, entry.Candidate ? rule.Action : "retain")).ToArray();

    private IReadOnlyList<(EntryMetadata Metadata, bool Candidate)> RankEntries(
        CisRepositoryContext context, ArtifactRetentionRule rule, List<string> errors, bool candidatesOnly = false)
    {
        var full = FullLocalPath(context, rule.Path, errors);
        if (full is null || !File.Exists(full) && !Directory.Exists(full)) return [];
        FileSystemInfo[] children = File.Exists(full) ? [new FileInfo(full)] :
            new DirectoryInfo(full).EnumerateFileSystemInfos("*", MetadataOptions(recursive: false)).ToArray();
        // If every entry is protected by KeepLatest, no age or size can make one eligible.
        // Doctor can avoid traversing large retained trees; explicit plans still report them.
        if (candidatesOnly && rule.MaximumBytes is null && children.Length <= rule.KeepLatest) return [];
        var ranked = children.Select(ReadEntryMetadata).OrderByDescending(item => item.LastWrite)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        var cutoff = _clock().ToUniversalTime().AddDays(-rule.RetainDays);
        var candidates = ranked.Select((item, index) =>
            rule.Action != "preserve" && index >= rule.KeepLatest && item.LastWrite.ToUniversalTime() < cutoff).ToArray();
        var remaining = ranked.Where((_, index) => !candidates[index]).Sum(item => item.Size);
        if (rule.MaximumBytes is { } maximum)
        {
            for (var index = ranked.Length - 1; index >= rule.KeepLatest && remaining > maximum && rule.Action != "preserve"; index--)
            {
                if (candidates[index]) continue;
                candidates[index] = true;
                remaining -= ranked[index].Size;
            }
            if (remaining > maximum)
                errors.Add($"Artifact family '{rule.Family}' exceeds its {maximum} byte budget: {remaining} protected bytes remain; review the retention policy or budget before cleanup.");
        }
        return ranked.Select((item, index) => (item, candidates[index])).ToArray();
    }

    private sealed record EntryMetadata(string Path, string Kind, long Size, DateTimeOffset LastWrite);

    private static EnumerationOptions MetadataOptions(bool recursive) => new()
    {
        RecurseSubdirectories = recursive,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false
    };

    private static EntryMetadata ReadEntryMetadata(FileSystemInfo entry)
    {
        if (entry is FileInfo file) return new(file.FullName, "file", file.Length, file.LastWriteTimeUtc);
        var directory = (DirectoryInfo)entry;
        long size = 0;
        DateTime? latest = null;
        // FileInfo from enumeration already contains metadata. Reopening a path to stat it
        // separately for size and timestamp turned one traversal into thousands of syscalls.
        foreach (var child in directory.EnumerateFiles("*", MetadataOptions(recursive: true)))
        {
            size = checked(size + child.Length);
            var written = child.LastWriteTimeUtc;
            if (latest is null || written > latest) latest = written;
        }
        return new(directory.FullName, "directory", size, latest ?? directory.LastWriteTimeUtc);
    }

    private static void AddToArchive(CisRepositoryContext context, ZipArchive archive, LocalArtifactEntry candidate)
    {
        var full = Path.Combine(context.RepositoryPath, candidate.Path.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(full)) archive.CreateEntryFromFile(full, candidate.Path, CompressionLevel.SmallestSize);
        else
        {
            var files = CisPathSafety.EnumerateFiles(full).ToArray();
            if (files.Length == 0) archive.CreateEntry(candidate.Path.TrimEnd('/') + "/");
            foreach (var file in files) archive.CreateEntryFromFile(file, Relative(context, file), CompressionLevel.SmallestSize);
        }
    }

    private static void VerifyArchive(string zipPath, IReadOnlyList<LocalArtifactEntry> candidates, List<string> errors)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            ValidateArchive(archive, new ArtifactArchiveManifest(1, "verification", "", "", "", "", candidates), errors);
            if (errors.Count > 0) return;
            var entries = archive.Entries.OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase).ToArray();
            var names = entries.Select(entry => entry.FullName).ToArray();
            var byName = entries.ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
            var buffer = new byte[81920];
            foreach (var candidate in candidates)
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var prefix = candidate.Path.TrimEnd('/') + "/";
                var selected = new List<ZipArchiveEntry>();
                if (candidate.Kind == "file")
                {
                    if (byName.TryGetValue(candidate.Path, out var entry) && !entry.FullName.EndsWith('/')) selected.Add(entry);
                }
                else
                {
                    var start = Array.BinarySearch(names, prefix, StringComparer.OrdinalIgnoreCase);
                    if (start < 0) start = ~start;
                    for (var index = start; index < entries.Length && names[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase); index++)
                        if (names[index].StartsWith(prefix, StringComparison.Ordinal)) selected.Add(entries[index]);
                }
                long size = 0;
                foreach (var entry in selected)
                {
                    if (entry.FullName.EndsWith('/')) continue;
                    if (candidate.Kind == "directory") hash.AppendData(Encoding.UTF8.GetBytes(entry.FullName[prefix.Length..] + "\n"));
                    using var input = entry.Open();
                    long readBytes = 0; int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (read > entry.Length - readBytes || read > MaximumExpandedBytes - size)
                            throw new InvalidDataException("Expanded entry exceeds its declared or configured size.");
                        readBytes += read; size += read;
                        hash.AppendData(buffer.AsSpan(0, read));
                    }
                    if (readBytes != entry.Length) throw new InvalidDataException("Expanded entry size does not match its declaration.");
                }
                if (selected.Count == 0 || size != candidate.SizeBytes
                    || !Convert.ToHexString(hash.GetHashAndReset()).Equals(candidate.Digest, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Archive verification failed for {candidate.Path}");
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        { errors.Add($"Could not verify archive: {exception.Message}"); }
    }

    private static IReadOnlyList<string> Extract(CisRepositoryContext context, string zipPath, string outputRoot,
        IReadOnlyList<LocalArtifactEntry> selected, bool overwrite, List<string> errors)
    {
        if (CisPathSafety.ContainsReparsePoint(context.RepositoryPath, outputRoot))
        { errors.Add("Artifact extraction root crosses a symbolic link."); return []; }
        Directory.CreateDirectory(outputRoot);
        var prefixes = selected.Select(item => item.Path.TrimEnd('/') + "/").ToArray();
        var exact = selected.Where(item => item.Kind == "file").Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outputs = new List<string>();
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var item in archive.Entries)
        {
            if (!exact.Contains(item.FullName) && !prefixes.Any(prefix => item.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) continue;
            var target = SafeExtractionPath(outputRoot, item.FullName, errors); if (target is null) continue;
            if (string.IsNullOrEmpty(item.Name)) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var before = errors.Count; ExtractBounded(item, target, overwrite, errors);
            if (errors.Count == before) outputs.Add(Relative(context, target));
        }
        return outputs;
    }

    private static IReadOnlyList<LocalArtifactEntry> SelectEntries(ArtifactArchiveManifest manifest, string? entry, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(entry)) return manifest.Entries;
        var selected = manifest.Entries.Where(item => item.Path.Equals(entry, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (selected.Length == 0) errors.Add($"Archive entry is not available: {entry}");
        return selected;
    }

    private static ArtifactArchiveManifest? FindArchive(CisRepositoryContext context, string id, List<string> errors)
    {
        var manifest = ReadArchives(context).FirstOrDefault(item => item.ArchiveId.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (manifest is null) errors.Add($"Artifact archive does not exist: {id}");
        return manifest;
    }

    private static IReadOnlyList<ArtifactArchiveManifest> ReadArchives(CisRepositoryContext context)
    {
        var root = Path.Combine(context.RepositoryPath, StateRoot.Replace('/', Path.DirectorySeparatorChar), "archive");
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*.json").Where(path => new FileInfo(path).Length <= 5 * 1024 * 1024)
            .Select(path => { try { return JsonSerializer.Deserialize<ArtifactArchiveManifest>(File.ReadAllText(path), JsonOptions); } catch { return null; } })
            .Where(item => item is not null && item.SchemaVersion == 1 && ValidArchiveId(item.ArchiveId))
            .Cast<ArtifactArchiveManifest>().OrderByDescending(item => item.CreatedUtc, StringComparer.Ordinal).ToArray();
    }

    private static string? FullLocalPath(CisRepositoryContext context, string relative, List<string> errors)
    {
        var local = Path.GetFullPath(Path.Combine(context.RepositoryPath, ".cis", "local"));
        if (!CisPathSafety.TryResolveUnderRoot(context.RepositoryPath, relative, out var full)
            || !CisPathSafety.IsUnderRoot(local, full)
            || CisPathSafety.ContainsReparsePoint(context.RepositoryPath, full))
        { errors.Add($"Artifact path escapes .cis/local or crosses a symbolic link: {relative}"); return null; }
        return full;
    }

    private static string? SafeExtractionPath(string root, string relative, List<string> errors)
    {
        var normalized = relative.Replace('\\', '/');
        if (normalized.EndsWith('/')) normalized = normalized[..^1];
        if (!CisPathSafety.TryResolveUnderRoot(root, normalized, out var target)
            || CisPathSafety.ContainsReparsePoint(root, target))
        { errors.Add($"Archive entry escapes its extraction root or crosses a symbolic link: {relative}"); return null; }
        return target;
    }

    private static void DeleteEntry(CisRepositoryContext context, string relative)
    {
        var errors = new List<string>(); var full = FullLocalPath(context, relative, errors) ?? throw new InvalidOperationException(string.Join("; ", errors));
        if (File.Exists(full)) File.Delete(full);
        else if (Directory.Exists(full))
        {
            if (CisPathSafety.ContainsReparsePointInTree(full)) throw new InvalidOperationException("Artifact directory crosses a symbolic link or cannot be safely inspected.");
            Directory.Delete(full, true);
        }
    }
    private static string HashEntry(string path)
    {
        if (File.Exists(path)) return Sha256File(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in CisPathSafety.EnumerateFiles(path).Order(StringComparer.OrdinalIgnoreCase))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(path, file).Replace('\\', '/') + "\n")); AppendFile(hash, file);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private static void ValidateArchive(ZipArchive archive, ArtifactArchiveManifest manifest, List<string> errors)
    {
        if (archive.Entries.Count > MaximumArchiveEntries)
        { errors.Add($"Archive exceeds the {MaximumArchiveEntries} entry limit."); return; }
        var exact = manifest.Entries.Where(item => item.Kind == "file").Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var prefixes = manifest.Entries.Where(item => item.Kind == "directory").Select(item => item.Path.TrimEnd('/') + "/").ToArray();
        if (manifest.Entries.GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            errors.Add("Archive manifest contains duplicate entry paths.");
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validationRoot = Path.Combine(Path.GetTempPath(), "cis-artifact-validation-root");
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            var identity = normalized.TrimEnd('/');
            if (identity.Length == 0 || !identities.Add(identity)) { errors.Add($"Archive contains an empty or duplicate path: {entry.FullName}"); continue; }
            if (!CisPathSafety.TryResolveUnderRoot(validationRoot, identity, out _)) errors.Add($"Archive contains an invalid path: {entry.FullName}");
            if (IsSymbolicLink(entry)) errors.Add($"Archive contains a symbolic-link entry: {entry.FullName}");
            if (entry.FullName.Contains('\\') || entry.FullName.EndsWith('/') && entry.Length != 0)
                errors.Add($"Archive contains an invalid file or directory name: {entry.FullName}");
            if (!exact.Contains(normalized) && !prefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"Archive contains an entry not declared by its manifest: {entry.FullName}");
            if (entry.Length < 0 || entry.Length > MaximumExpandedBytes - expanded) errors.Add($"Archive exceeds the {MaximumExpandedBytes} byte expanded-size limit.");
            else expanded += entry.Length;
        }
        foreach (var item in manifest.Entries)
            if (!CisPathSafety.TryResolveUnderRoot(validationRoot, item.Path, out _)
                || item.Kind is not ("file" or "directory")
                || item.SizeBytes < 0
                || !System.Text.RegularExpressions.Regex.IsMatch(item.Digest, "^[0-9a-f]{64}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                errors.Add($"Archive manifest entry is invalid: {item.Path}");
    }

    private static void ExtractBounded(ZipArchiveEntry entry, string target, bool overwrite, List<string> errors)
    {
        try
        {
            using var input = entry.Open();
            using var output = new FileStream(target, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920]; long written = 0; int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                written += read;
                if (written > entry.Length || written > MaximumExpandedBytes) throw new InvalidDataException("Expanded entry exceeds its declared or configured size.");
                output.Write(buffer, 0, read);
            }
            if (written != entry.Length) throw new InvalidDataException("Expanded entry size does not match its declaration.");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            errors.Add($"Could not extract archive entry {entry.FullName}: {exception.Message}");
            try { if (File.Exists(target)) File.Delete(target); } catch { }
        }
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
        return unixMode == 0xA000 || ((FileAttributes)(entry.ExternalAttributes & 0xFFFF)).HasFlag(FileAttributes.ReparsePoint);
    }
    private static void MoveDirectoryFiles(string source, string target)
    {
        foreach (var file in CisPathSafety.EnumerateFiles(source))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file)); if (File.Exists(destination)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Move(file, destination);
        }
    }
    private CisRepositoryContext? Resolve(string path, out List<string> errors) { var result = _resolver.Resolve(path); errors = result.Errors.ToList(); return result.Context; }
    private ArtifactOperationResult Result(CisRepositoryContext context, string status, IReadOnlyList<ArtifactRetentionRule> rules,
        IReadOnlyList<LocalArtifactEntry> entries, IReadOnlyList<ArtifactArchiveManifest> archives, IReadOnlyList<string> outputs,
        IReadOnlyList<string> errors, bool applied, bool reuseEntries = false)
    {
        var storageErrors = errors.ToList();
        var families = new List<ArtifactFamilyStorage>();
        foreach (var rule in rules)
        {
            var ranked = reuseEntries
                ? entries.Where(entry => entry.Family.Equals(rule.Family, StringComparison.OrdinalIgnoreCase))
                    .Select(entry => (Size: entry.SizeBytes, entry.Candidate)).ToArray()
                : RankEntries(context, rule, storageErrors).Select(entry => (entry.Metadata.Size, entry.Candidate)).ToArray();
            long live = ranked.Sum(item => item.Size);
            long candidates = ranked.Where(item => item.Candidate).Sum(item => item.Size);
            long protectedBytes = rule.Action == "preserve" ? live : ranked.Take(rule.KeepLatest).Sum(item => item.Size);
            families.Add(new(rule.Family, live, protectedBytes, candidates, live - candidates, rule.MaximumBytes,
                rule.MaximumBytes is { } maximum && live > maximum,
                rule.MaximumBytes is { } limit && live - candidates > limit));
        }
        long management = 0, archiveBytes = 0;
        var root = FullLocalPath(context, StateRoot, storageErrors);
        if (root is not null && Directory.Exists(root))
        {
            management = ReadEntryMetadata(new DirectoryInfo(root)).Size;
            var archiveRoot = Path.Combine(root, "archive");
            if (Directory.Exists(archiveRoot))
                archiveBytes = new DirectoryInfo(archiveRoot).EnumerateFiles("*.zip", MetadataOptions(recursive: false)).Sum(file => file.Length);
        }
        long configured = families.Sum(family => family.LiveBytes);
        return new(status, context.RepositoryPath, rules, entries, archives, outputs, storageErrors.Distinct(StringComparer.Ordinal).ToArray(), applied)
        {
            FamilyStorage = families,
            Storage = new(configured, archiveBytes, management, checked(configured + management))
        };
    }
    private static ArtifactOperationResult Failed(IReadOnlyList<string> errors) => new("failed", null, [], [], [], [], errors, false);
    private static string Relative(CisRepositoryContext context, string path) => Path.GetRelativePath(context.RepositoryPath, path).Replace('\\', '/');
    private static string Sha256(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private static string Sha256File(string path) { using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); AppendFile(hash, path); return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(); }
    private static void AppendFile(IncrementalHash hash, string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); var buffer = new byte[81920]; int read; while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) hash.AppendData(buffer.AsSpan(0, read)); }
    private static string ShortDigest(string value) => Sha256(Encoding.UTF8.GetBytes(value))[..10];
    private static void AtomicWrite(string path, string content) { var temporary = path + ".tmp"; File.WriteAllText(temporary, content); File.Move(temporary, path, true); }
    private static string UniqueArchiveId(string directory, string basis) { var candidate = basis; var suffix = 2; while (File.Exists(Path.Combine(directory, candidate + ".zip")) || File.Exists(Path.Combine(directory, candidate + ".json"))) candidate = basis + "-" + suffix++; return candidate; }
    private static bool ValidArchiveId(string value) => value.Length is > 0 and <= 128 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
