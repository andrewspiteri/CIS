using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed record BrdLayoutResult(string Status, string? Path, string? ReviewHash,
    BrdLayoutDefinition? Layout, IReadOnlyList<string> RequirementIds,
    IReadOnlyList<string> UnmappedSections, IReadOnlyList<string> Errors, bool Applied = false)
{
    public int ExitCode => Errors.Count == 0 ? 0 : 2;
}

public sealed partial class BrdService
{
    public BrdLayoutResult MapLayout(string workspacePath, string inputPath, bool confirmed = false, string? reviewHash = null)
    {
        BrdLayoutResult Failure(string message) => new("invalid", null, null, null, [], [], [message]);
        try
        {
            var resolution = ResolveAuthority(workspacePath);
            if (resolution.Authority is not { } authority || resolution.Errors.Count > 0)
                return Failure(string.Join(" ", resolution.Errors));
            var path = CanonicalPath(authority);
            var input = System.IO.Path.GetFullPath(inputPath);
            if (CisPathSafety.ContainsReparsePoint(authority.RepositoryPath, path)
                || CisPathSafety.ContainsReparsePoint(System.IO.Path.GetPathRoot(input)!, input))
                return Failure("The BRD and layout input must not be links.");
            var original = ReadLayoutText(path, 524_288);
            if (!HasManagedBlocks(original)) return Failure("Load and reconcile the canonical BRD before mapping its layout.");
            var layout = BrdDocumentLayout.Parse(ReadLayoutText(input, 32_768));
            var proposed = BrdDocumentLayout.Insert(original, layout);
            var layoutErrors = BrdDocumentLayout.Validate(proposed);
            if (layoutErrors.Count > 0) return Failure(string.Join(" ", layoutErrors));
            var requirements = BrdRequirementReader.Read(proposed, "Functional requirements");
            if (requirements.Errors.Count > 0) return Failure(string.Join(" ", requirements.Errors));
            if (layout.Sections.ContainsKey("Functional requirements") && requirements.Requirements.Count == 0)
                return Failure("The mapped functional sections contain no readable identified requirements.");
            var digest = Hash(original + "\0" + proposed);
            var result = new BrdLayoutResult("preview", path, digest, layout,
                requirements.Requirements.Select(item => item.Id).ToArray(),
                RequiredSections.Where(role => string.IsNullOrWhiteSpace(ExtractSection(proposed, role))).ToArray(), []);
            if (proposed == original) return result with { Status = "unchanged" };
            if (!confirmed) return result;
            if (!string.Equals(reviewHash, digest, StringComparison.Ordinal))
                return Failure("The reviewed layout or BRD changed. Preview the current mapping and supply its exact --review-hash.");

            var lockPath = System.IO.Path.Combine(authority.RepositoryPath, ".cis/local/brd-source-decisions.lock");
            if (CisPathSafety.ContainsReparsePoint(authority.RepositoryPath, lockPath)
                || CisPathSafety.ContainsReparsePoint(authority.RepositoryPath, path + ".tmp"))
                return Failure("BRD write paths must not be links.");
            using var writeLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (ReadLayoutText(path, 524_288) != original) return Failure("The BRD changed after layout preview.");
            proposed = ReplaceFrontMatter(proposed, "status", "Review Required");
            foreach (var key in new[] { "approved_by", "approved_at", "approval_reason", "approved_content_hash" })
                proposed = ReplaceNestedFrontMatter(proposed, key, "null");
            BackupImportedBrd(authority.RepositoryPath, path);
            Write(path, proposed);
            return result with { Status = "applied", Applied = true };
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        { return Failure("Could not map BRD layout: " + error.Message); }
    }

    private static string ReadLayoutText(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumBytes) throw new InvalidDataException("BRD layout input exceeds its supported size.");
        var bytes = new byte[maximumBytes + 1];
        var length = 0;
        while (length < bytes.Length)
        {
            var count = stream.Read(bytes, length, bytes.Length - length);
            if (count == 0) break;
            length += count;
        }
        if (length > maximumBytes) throw new InvalidDataException("BRD layout input exceeds its supported size.");
        return new UTF8Encoding(false, true).GetString(bytes, 0, length).TrimStart('\uFEFF');
    }
}
