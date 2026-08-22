namespace Cis.Modules.Tracker;

public sealed record TrackerProviderConfiguration(
    string Key,
    string Kind,
    bool Enabled,
    string Target,
    string BaseUrl,
    string Direction,
    string IssueType,
    string CredentialSource);

public sealed record TrackerProfile(IReadOnlyList<TrackerProviderConfiguration> Providers);

public sealed record TrackerTaskProjection(
    string RepositoryId,
    string ChangeId,
    string TaskId,
    string TaskType,
    string Category,
    string Status,
    string Title,
    string Body,
    IReadOnlyList<string> Labels,
    string CanonicalPath,
    string Digest);

public sealed record TrackerRemoteItem(
    string Id,
    string Url,
    string Title,
    string Body,
    string State,
    IReadOnlyList<string> Labels,
    string UpdatedAt,
    string Digest);

public sealed record TrackerAvailability(bool Available, string Message);

public interface ICisTrackerProvider
{
    string Kind { get; }
    TrackerAvailability Probe(TrackerProviderConfiguration configuration);
    TrackerRemoteItem? Get(TrackerProviderConfiguration configuration, string remoteId);
    TrackerRemoteItem Create(TrackerProviderConfiguration configuration, TrackerTaskProjection task);
    TrackerRemoteItem Update(TrackerProviderConfiguration configuration, string remoteId, TrackerTaskProjection task);
}

public sealed record TrackerMapping(
    string Provider,
    string ChangeId,
    string TaskId,
    string RemoteId,
    string Url,
    string CanonicalDigest,
    string RemoteDigest,
    string LastSynchronizedUtc,
    string State);

public sealed record TrackerConflict(
    string Id,
    string Provider,
    string ChangeId,
    string TaskId,
    string Kind,
    string Message,
    string CanonicalDigest,
    string RemoteDigest,
    string DetectedAtUtc,
    string Status,
    string? Resolution,
    string? Reviewer,
    string? Rationale,
    string? ResolvedAtUtc);

public sealed record TrackerSyncItem(
    string Provider,
    string ChangeId,
    string TaskId,
    string Action,
    string Message,
    string? RemoteId,
    string? Url,
    bool Applied);

public sealed record TrackerSyncResult(
    string Status,
    string? RepositoryPath,
    string? ChangeId,
    IReadOnlyList<TrackerSyncItem> Items,
    IReadOnlyList<TrackerConflict> Conflicts,
    IReadOnlyList<string> Errors,
    bool Applied,
    bool RepositoryConfigurationValid)
{
    public int ExitCode => !RepositoryConfigurationValid ? 2 : Errors.Count > 0 ? 4 : Conflicts.Any(item => item.Status == "open") ? 5 : 0;
}

public sealed record TrackerStatusResult(
    string Status,
    string? RepositoryPath,
    IReadOnlyList<TrackerProviderConfiguration> Providers,
    IReadOnlyList<TrackerMapping> Mappings,
    IReadOnlyList<TrackerConflict> Conflicts,
    IReadOnlyList<string> Errors,
    bool RepositoryConfigurationValid)
{
    public int ExitCode => !RepositoryConfigurationValid ? 2 : Errors.Count > 0 ? 4 : Conflicts.Any(item => item.Status == "open") ? 5 : 0;
}
