using System.Security.Cryptography;
using System.Text;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

/// <summary>Checks the coverage and bytes of an explicit compatibility review, not its semantic truth.</summary>
internal sealed class FeatureReconciliationEvidence(string repositoryPath)
{
    internal static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private const int MaximumFileBytes = 1024 * 1024;
    private const int MaximumTotalBytes = 32 * MaximumFileBytes;
    private readonly Dictionary<string, byte[]> _inputs = new(PathComparer);
    private int _totalBytes;
    private readonly HashSet<string> _absent = new(PathComparer);

    public void WatchOptional(string relative)
    {
        if (!CisPathSafety.TryResolveUnderRoot(repositoryPath, relative, out var path)
            || CisPathSafety.ContainsReparsePoint(repositoryPath, path))
            throw new InvalidDataException("Unsafe reconciliation authority input: " + relative);
        if (File.Exists(path)) Read(relative);
        else _absent.Add(path);
    }

    public IReadOnlyDictionary<string, byte[]> Inputs => _inputs;

    private string ResolvePath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)
            || relative.Replace('\\', '/').Split('/').Any(part => part.Equals(".git", StringComparison.OrdinalIgnoreCase))
            || !CisPathSafety.TryResolveUnderRoot(repositoryPath, relative, out var path)
            || CisPathSafety.ContainsReparsePoint(repositoryPath, path)
            || !File.Exists(path) || new FileInfo(path).Length > MaximumFileBytes)
            throw new InvalidDataException("Reconciliation requires bounded, existing, repository-local files without symbolic links: " + relative);
        return path;
    }

    public byte[] Read(string relative)
    {
        var path = ResolvePath(relative);
        if (_inputs.TryGetValue(path, out var retained)) return retained;
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > MaximumFileBytes || (_totalBytes += bytes.Length) > MaximumTotalBytes)
            throw new InvalidDataException("Reconciliation evidence exceeds its size limit.");
        _ = new UTF8Encoding(false, true).GetString(bytes);
        _inputs.Add(path, bytes);
        return bytes;
    }

    public void Validate(CisFeatureReconciliationReview review, IReadOnlyList<string> requiredPaths, IReadOnlySet<string> outputPaths)
    {
        if (review.SchemaVersion != 1 || review.Comparison is null || review.Artifacts is null
            || review.Artifacts.Count is 0 or > 128 || requiredPaths.Count == 0)
            throw new InvalidDataException("A schemaVersion 1 review and a complete native product-definition inventory are required.");
        if (!Matches(Read(review.Comparison.Path), review.Comparison.Sha256))
            throw new InvalidDataException("The source-backed compatibility comparison changed after review.");
        if (string.IsNullOrWhiteSpace(Encoding.UTF8.GetString(Read(review.Comparison.Path))))
            throw new InvalidDataException("The compatibility comparison cannot be empty.");
        var provided = review.Artifacts.Select(item => item?.Path).ToArray();
        if (provided.Any(string.IsNullOrWhiteSpace)
            || provided.Distinct(StringComparer.Ordinal).Count() != provided.Length
            || !provided.Order(StringComparer.Ordinal).SequenceEqual(requiredPaths.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Compare every native product-definition artifact exactly once; missing or additional paths are not accepted.");
        var comparisonPaths = review.Artifacts.SelectMany(item => new[] { item.Path, item.Prior?.Path })
            .Append(review.Comparison.Path).Where(path => path is not null).Select(path => ResolvePath(path!));
        if (comparisonPaths.Any(outputPaths.Contains))
            throw new InvalidDataException("Comparison evidence cannot overlap the feature or catalogue being updated.");
        var currentPaths = requiredPaths.Select(ResolvePath).ToHashSet(PathComparer);
        var priorPaths = new HashSet<string>(PathComparer);
        foreach (var item in review.Artifacts)
        {
            if (item.Prior is null || string.IsNullOrWhiteSpace(item.Reason)
                || item.Assessment is not ("unchanged" or "compatible"))
                throw new InvalidDataException("Every artifact needs retained prior evidence and a resolved compatibility assessment: " + item.Path);
            var current = Read(item.Path);
            var prior = Read(item.Prior.Path);
            var priorPath = ResolvePath(item.Prior.Path);
            if (currentPaths.Contains(priorPath) || !priorPaths.Add(priorPath))
                throw new InvalidDataException("Prior artifacts must be distinct retained files, not aliases of current inputs.");
            if (!Matches(current, item.CurrentSha256) || !Matches(prior, item.Prior.Sha256))
                throw new InvalidDataException("A compared artifact changed after review: " + item.Path);
            if (item.Assessment == "unchanged" && !current.SequenceEqual(prior))
                throw new InvalidDataException("An unchanged assessment has different artifact bytes: " + item.Path);
        }
    }

    public bool UnchangedExcept(IReadOnlySet<string> writtenPaths)
        => _absent.All(path => !File.Exists(path) && !Directory.Exists(path)
                && !CisPathSafety.ContainsReparsePoint(repositoryPath, path))
            && _inputs.Where(item => !writtenPaths.Contains(item.Key))
            .All(item => !CisPathSafety.ContainsReparsePoint(repositoryPath, item.Key)
                && File.Exists(item.Key) && File.ReadAllBytes(item.Key).SequenceEqual(item.Value));

    public static string Digest(byte[] bytes)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static bool Matches(byte[] bytes, string? expected)
        => expected is { Length: 71 } && expected.StartsWith("sha256:", StringComparison.Ordinal)
            && Digest(bytes).Equals(expected, StringComparison.OrdinalIgnoreCase);
}
