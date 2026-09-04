namespace Cis.Abstractions;

public sealed record CisCiTarget(
    string Repository,
    string BaseUrl = "https://api.github.com");

public sealed record CisCiProviderAvailability(
    bool Available,
    string Message);

public sealed record CisCiCheck(
    long Id,
    string Name,
    string Status,
    string Conclusion,
    string Url,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record CisCiRun(
    long Id,
    string Name,
    string Event,
    string Status,
    string Conclusion,
    string HeadSha,
    string Branch,
    int Attempt,
    string Url,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CisCiJob(
    long Id,
    long RunId,
    string Name,
    string Status,
    string Conclusion,
    int Attempt,
    string RunnerName,
    string Url,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<CisCiStep> Steps);

public sealed record CisCiStep(
    int Number,
    string Name,
    string Status,
    string Conclusion,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record CisCiArtifact(
    long Id,
    string Name,
    long SizeBytes,
    bool Expired,
    string DownloadUrl,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc);

public sealed record CisCiJobLog(
    long JobId,
    string MediaType,
    byte[] Content,
    long? TotalBytes = null,
    bool Truncated = false,
    string? ContentDigest = null);

public interface ICisCiProvider
{
    string Kind { get; }

    CisCiProviderAvailability Probe(CisCiTarget target);

    IReadOnlyList<CisCiCheck> PullRequestChecks(CisCiTarget target, int pullRequest);

    IReadOnlyList<CisCiRun> Runs(CisCiTarget target, int? pullRequest, int limit);

    IReadOnlyList<CisCiJob> Jobs(CisCiTarget target, long runId);

    CisCiJobLog JobLog(CisCiTarget target, long jobId);

    IReadOnlyList<CisCiArtifact> Artifacts(CisCiTarget target, long runId);

    void RerunFailed(CisCiTarget target, long runId);
}
