using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cis.Abstractions;

public sealed record CisOutcomeScope(string Id, string Description, IReadOnlyList<string> Paths);

public sealed record CisOutcomeAuthorizationRequest(string Outcome, IReadOnlyList<CisOutcomeScope> Scope,
    IReadOnlyList<string> Constraints, long Seconds, int Attempts, long RestorationSeconds,
    CisOwnedStoragePolicy Storage, IReadOnlyList<string> Controllers, bool OrganizeTasks,
    bool DistributeResources, bool? VisualUi, bool ManualTestCatalogue, string DecisionReference);

public sealed record CisOutcomeAuthorizationRevision(CisOutcomeAuthorizationRequest Request, string Reviewer,
    string Reason, string AuthorizedAtUtc, string Digest)
{
    public bool VisualUiDerivedFromAcceptedSource { get; init; }
}

public sealed record CisOutcomeAuthorization(string Id, string ChangeId, CisOutcomeAuthorizationRequest Request,
    string Reviewer, string Reason, string AuthorizedAtUtc, string Digest)
{
    public IReadOnlyList<CisOutcomeAuthorizationRevision> History { get; init; } = [];
    // Attribution only; the effective applicability value is protected by Request's digest.
    public bool VisualUiDerivedFromAcceptedSource { get; init; }
}

/// <summary>Reads the change-owned authorization and computes its explicit structured scope basis.</summary>
public static class CisOutcomeAuthority
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Path(CisRepositoryContext context, string changeId)
    {
        if (string.IsNullOrWhiteSpace(changeId) || changeId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new InvalidDataException("Supply a safe canonical change identity for outcome authorization.");
        if (!CisPathSafety.TryResolveUnderRoot(context.RepositoryPath,
                $"{context.DocumentationRoot}/changes/{changeId}/authorization.json", out var path)
            || CisPathSafety.ContainsReparsePoint(context.RepositoryPath, path))
            throw new InvalidDataException("Outcome authorization must remain in its unlinked canonical change directory.");
        return path;
    }

    public static CisOutcomeAuthorization? Read(CisRepositoryContext context, string changeId)
    {
        var path = Path(context, changeId);
        if (Directory.Exists(path)) throw new InvalidDataException("The canonical outcome authorization must be a regular file.");
        if (!File.Exists(path)) return null;
        try
        {
            var bytes = ReadBounded(path);
            using var document = JsonDocument.Parse(bytes);
            RejectDuplicates(document.RootElement);
            var authorization = JsonSerializer.Deserialize<CisOutcomeAuthorization>(bytes, Json)
                ?? throw new InvalidDataException("Outcome authorization is empty.");
            if (string.IsNullOrWhiteSpace(authorization.Id) || authorization.Id.Any(char.IsControl)
                || !string.Equals(authorization.ChangeId, changeId, StringComparison.OrdinalIgnoreCase)
                || authorization.History is null || authorization.Request is null
                || authorization.VisualUiDerivedFromAcceptedSource && !authorization.Request.VisualUi.HasValue)
                throw new InvalidDataException("Outcome authorization identity or history is invalid.");
            ValidateRevision(authorization.Request, authorization.Reviewer, authorization.Reason,
                authorization.AuthorizedAtUtc, authorization.Digest);
            foreach (var revision in authorization.History)
            {
                if (revision is null || revision.Request is null || revision.Reviewer != authorization.Reviewer
                    || revision.VisualUiDerivedFromAcceptedSource && !revision.Request.VisualUi.HasValue)
                    throw new InvalidDataException("Outcome amendments must preserve their original human authority.");
                ValidateRevision(revision.Request, revision.Reviewer, revision.Reason, revision.AuthorizedAtUtc, revision.Digest);
            }
            return authorization with { Request = Normalize(authorization.Request) };
        }
        catch (Exception error) when (error is JsonException or ArgumentException or OverflowException or UnauthorizedAccessException
            || error is IOException)
        { throw new InvalidDataException("The canonical outcome authorization is invalid; preserve its original authority and reconcile it.", error); }
    }

    public static CisOutcomeAuthorizationRequest ReadRequest(string path)
    {
        try
        {
            var bytes = ReadBounded(path);
            using var document = JsonDocument.Parse(bytes);
            RejectDuplicates(document.RootElement);
            return Normalize(JsonSerializer.Deserialize<CisOutcomeAuthorizationRequest>(bytes,
                new JsonSerializerOptions(Json) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
                ?? throw new InvalidDataException("The proposed outcome authorization is empty."));
        }
        catch (JsonException error) { throw new InvalidDataException("The proposed outcome authorization is not valid JSON.", error); }
    }

    public static CisOutcomeAuthorizationRequest Normalize(CisOutcomeAuthorizationRequest request)
    {
        if (request is null || request.Scope is not { Count: > 0 and <= 10000 }
            || request.Constraints is null || request.Controllers is null
            || request.Seconds is < 1 or > 31_536_000 || request.Attempts is < 1 or > 10000
            || request.RestorationSeconds < 0 || request.RestorationSeconds > request.Seconds)
            throw new InvalidDataException("Supply an explicit outcome scope, controllers and finite overall time/attempt/restoration limits.");
        var scopes = request.Scope.Select(scope =>
        {
            if (scope is null || string.IsNullOrWhiteSpace(scope.Id)
                || scope.Id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.'))
                || scope.Paths is not { Count: > 0 and <= 10000 })
                throw new InvalidDataException("Every outcome scope needs its own stable ID and explicit relative path prefixes.");
            return new CisOutcomeScope(scope.Id, Text(scope.Description), scope.Paths.Select(RelativePrefix)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        }).OrderBy(scope => scope.Id, StringComparer.Ordinal).ToArray();
        if (scopes.Select(scope => scope.Id).Distinct(StringComparer.Ordinal).Count() != scopes.Length)
            throw new InvalidDataException("Outcome scope IDs must be unique.");
        var storage = request.Storage;
        if (storage is null || storage.MaximumBytes <= 0 || storage.RestorationBytes < 0
            || storage.RestorationBytes > storage.MaximumBytes || storage.Roots is not { Count: > 0 and <= 100 }
            || storage.CleanupControllers is null)
            throw new InvalidDataException("Supply explicit owned storage roots and a finite shared byte ceiling/restoration reserve.");
        var roots = storage.Roots.Select(root =>
        {
            if (root is null || !System.IO.Path.IsPathFullyQualified(root.RepositoryPath))
                throw new InvalidDataException("Storage roots need their exact registered repository paths.");
            var relative = RelativePrefix(root.Path);
            if (!root.Scratch && relative != ".cis/local" && !relative.StartsWith(".cis/local/", StringComparison.Ordinal))
                throw new InvalidDataException("Ordinary owned storage roots must remain within CIS local state.");
            return new CisOwnedStorageRoot(System.IO.Path.GetFullPath(root.RepositoryPath), relative) { Scratch = root.Scratch };
        }).Distinct().OrderBy(root => root.RepositoryPath, StringComparer.Ordinal).ThenBy(root => root.Path, StringComparer.Ordinal)
            .ThenBy(root => root.Scratch).ToArray();
        return request with
        {
            Outcome = Text(request.Outcome), Scope = scopes, Constraints = Set(request.Constraints),
            Controllers = Set(request.Controllers), DecisionReference = Text(request.DecisionReference),
            Storage = storage with { Roots = roots, CleanupControllers = Set(storage.CleanupControllers) }
        };
    }

    public static string Digest(CisOutcomeAuthorizationRequest request) => Hash(Normalize(request));

    public static string ScopeBasis(CisOutcomeAuthorization authorization, IReadOnlyList<string> scopeIds)
    {
        var request = Normalize(authorization.Request);
        if (scopeIds is not { Count: > 0 and <= 10000 })
            throw new InvalidDataException("A derived task needs explicit authorized scope IDs.");
        var selected = scopeIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(id =>
            request.Scope.SingleOrDefault(scope => scope.Id == id)
            ?? throw new InvalidDataException($"Outcome authorization does not include scope '{id}'.")).ToArray();
        return Hash(new { request.Outcome, request.Constraints, Scope = selected });
    }

    public static bool IsController(CisOutcomeAuthorization authorization, string actor)
        => !string.IsNullOrWhiteSpace(actor) && (authorization.Reviewer == actor.Trim()
            || authorization.Request.Controllers.Contains(actor.Trim(), StringComparer.Ordinal));

    private static void ValidateRevision(CisOutcomeAuthorizationRequest request, string reviewer, string reason, string at, string digest)
    {
        _ = Text(reviewer); _ = Text(reason);
        if (!DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)
            || !string.Equals(Digest(request), digest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Outcome authorization provenance or semantic digest is invalid.");
    }

    private static byte[] ReadBounded(string path)
    {
        using var input = File.OpenRead(path);
        if (input.Length is < 1 or > MaximumBytes) throw new InvalidDataException("Outcome authorization exceeds its bounded record size.");
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = input.Read(buffer)) > 0)
        {
            if (output.Length + count > MaximumBytes) throw new InvalidDataException("Outcome authorization changed beyond its bounded record size.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static string RelativePrefix(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || System.IO.Path.IsPathRooted(value) || value.StartsWith('/') || value.StartsWith('\\')
            || value.Any(character => char.IsControl(character) || ":*?[]".Contains(character)))
            throw new InvalidDataException("Scope and storage paths must be explicit relative prefixes, without glob patterns.");
        var segments = value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == ".." || segment.Equals(".git", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Scope and storage prefixes cannot escape the repository or include Git internals.");
        var normalized = string.Join('/', segments.Where(segment => segment != "."));
        return normalized.Length == 0 ? "." : normalized;
    }

    private static string Text(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\0')) throw new InvalidDataException("Outcome authorization text must be explicit and nonempty.");
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }

    private static string[] Set(IReadOnlyList<string> values)
    {
        if (values.Count > 10000) throw new InvalidDataException("Outcome authorization exceeds its bounded item inventory.");
        return values.Select(Text).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
    private static string Hash<T>(T value) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json)));

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            { if (!names.Add(property.Name)) throw new InvalidDataException("Outcome authorization contains duplicate JSON fields."); RejectDuplicates(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) RejectDuplicates(child);
    }
}
