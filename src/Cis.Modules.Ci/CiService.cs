using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Ci;

public sealed partial class CiService
{
    private const int MaximumLogBytes = 10 * 1024 * 1024;
    private readonly ICisRepositoryContextResolver _resolver;
    private readonly IReadOnlyList<ICisCiProvider> _providers;

    public CiService(ICisRepositoryContextResolver resolver, IEnumerable<ICisCiProvider> providers)
    {
        _resolver = resolver;
        _providers = providers.OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.GetType().FullName, StringComparer.Ordinal).ToArray();
    }

    public CiInvestigationResult Providers(string repositoryPath, string? repository = null)
    {
        var resolved = Resolve(repositoryPath, repository, null);
        if (resolved.Error is not null) return resolved.Error;
        var availability = new List<CisCiProviderAvailability>();
        foreach (var group in _providers.GroupBy(item => item.Kind, StringComparer.OrdinalIgnoreCase))
            availability.Add(!ValidProviderKind(group.Key) ? new(false, $"Provider kind is unsafe: {group.Key}")
                : group.Count() == 1 ? group.Single().Probe(resolved.Target!) : new(false, $"Provider kind conflict: {group.Key}"));
        var unavailable = availability.Where(item => !item.Available).Select(item => item.Message).ToArray();
        return Result(unavailable.Length == 0 ? "available" : "unavailable", resolved.Path, "all", resolved.Target!.Repository,
            availability: availability, errors: unavailable);
    }

    public CiInvestigationResult Status(string repositoryPath, string? provider, string? repository, int pullRequest)
    {
        if (pullRequest <= 0) return Invalid(repositoryPath, provider, repository, "Pull request number must be positive.");
        var selected = Select(repositoryPath, provider, repository); if (selected.Error is not null) return selected.Error;
        try
        {
            var availability = selected.Provider!.Probe(selected.Target!);
            if (!availability.Available) return Result("unavailable", selected.Path, selected.Provider.Kind, selected.Target!.Repository, availability: [availability], errors: [availability.Message]);
            return Result("inspected", selected.Path, selected.Provider.Kind, selected.Target!.Repository,
                availability: [availability], checks: selected.Provider.PullRequestChecks(selected.Target, pullRequest),
                runs: selected.Provider.Runs(selected.Target, pullRequest, 50));
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
        { return Failure(selected, exception); }
    }

    public CiInvestigationResult Runs(string repositoryPath, string? provider, string? repository, int? pullRequest, int limit)
    {
        if (limit is < 1 or > 100 || pullRequest <= 0) return Invalid(repositoryPath, provider, repository, "Limit must be 1-100 and pull request, when supplied, must be positive.");
        var selected = Select(repositoryPath, provider, repository); if (selected.Error is not null) return selected.Error;
        try { return Result("inspected", selected.Path, selected.Provider!.Kind, selected.Target!.Repository, runs: selected.Provider.Runs(selected.Target, pullRequest, limit)); }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException) { return Failure(selected, exception); }
    }

    public CiInvestigationResult Jobs(string repositoryPath, string? provider, string? repository, long runId)
    {
        if (runId <= 0) return Invalid(repositoryPath, provider, repository, "Run ID must be positive.");
        var selected = Select(repositoryPath, provider, repository); if (selected.Error is not null) return selected.Error;
        try { return Result("inspected", selected.Path, selected.Provider!.Kind, selected.Target!.Repository, jobs: selected.Provider.Jobs(selected.Target, runId)); }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException) { return Failure(selected, exception); }
    }

    public CiInvestigationResult Logs(string repositoryPath, string? provider, string? repository, long jobId)
    {
        if (jobId <= 0) return Invalid(repositoryPath, provider, repository, "Job ID must be positive.");
        var selected = Select(repositoryPath, provider, repository); if (selected.Error is not null) return selected.Error;
        try
        {
            var evidence = SaveLog(selected.Path!, selected.Provider!.Kind, selected.Provider.JobLog(selected.Target!, jobId));
            return Result("downloaded", selected.Path, selected.Provider.Kind, selected.Target!.Repository, evidence: [evidence], applied: true);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or IOException or TaskCanceledException) { return Failure(selected, exception); }
    }

    public CiInvestigationResult Artifacts(string repositoryPath, string? provider, string? repository, long runId)
    {
        if (runId <= 0) return Invalid(repositoryPath, provider, repository, "Run ID must be positive.");
        var selected = Select(repositoryPath, provider, repository); if (selected.Error is not null) return selected.Error;
        try { return Result("inspected", selected.Path, selected.Provider!.Kind, selected.Target!.Repository, artifacts: selected.Provider.Artifacts(selected.Target, runId)); }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException) { return Failure(selected, exception); }
    }

    public CiInvestigationResult Diagnose(string repositoryPath, string? provider, string? repository, long runId)
    {
        if (runId <= 0) return Invalid(repositoryPath, provider, repository, "Run ID must be positive.");
        var selected = Select(repositoryPath, provider, repository); if (selected.Error is not null) return selected.Error;
        try
        {
            var ciProvider = selected.Provider!;
            var target = selected.Target!;
            var jobs = ciProvider.Jobs(target, runId);
            var failed = jobs.Where(item => item.Conclusion is "failure" or "timed_out" or "cancelled").Take(5).ToArray();
            var failures = new List<CiFailure>(); var evidence = new List<CiEvidence>();
            foreach (var job in failed)
            {
                var log = ciProvider.JobLog(target, job.Id);
                var saved = SaveLog(selected.Path!, ciProvider.Kind, log); evidence.Add(saved);
                var text = File.ReadAllText(Path.Combine(selected.Path!, saved.Path.Replace('/', Path.DirectorySeparatorChar)));
                failures.Add(Classify(job, text, saved.Path));
            }
            return Result("diagnosed", selected.Path, ciProvider.Kind, target.Repository, jobs: jobs, failures: failures, evidence: evidence, applied: evidence.Count > 0);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or IOException or TaskCanceledException) { return Failure(selected, exception); }
    }

    public CiInvestigationResult Reproduce(string repositoryPath, string? provider, string? repository, long runId)
    {
        var result = Diagnose(repositoryPath, provider, repository, runId);
        return result.ExitCode == 0 ? result with { Status = "reproduction-planned", Applied = false } : result;
    }

    public CiInvestigationResult RerunFailed(string repositoryPath, string? provider, string? repository, long runId, bool confirmed)
    {
        if (!confirmed) return Invalid(repositoryPath, provider, repository, "Remote rerun requires explicit --yes confirmation.");
        if (runId <= 0) return Invalid(repositoryPath, provider, repository, "Run ID must be positive.");
        var selected = Select(repositoryPath, provider, repository); if (selected.Error is not null) return selected.Error;
        try
        {
            var ciProvider = selected.Provider!; var target = selected.Target!;
            ciProvider.RerunFailed(target, runId);
            return Result("rerun-requested", selected.Path, ciProvider.Kind, target.Repository, applied: true);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException) { return Failure(selected, exception); }
    }

    private Selection Select(string path, string? provider, string? repository)
    {
        var resolved = Resolve(path, repository, provider); if (resolved.Error is not null) return new(null, null, resolved.Path, resolved.Error);
        var kind = string.IsNullOrWhiteSpace(provider) ? _providers.Select(item => item.Kind).Distinct(StringComparer.OrdinalIgnoreCase).SingleOrDefault() : provider;
        if (kind is null) return new(null, resolved.Target, resolved.Path, Invalid(path, provider, repository, "Select --provider because more than one CI provider is loaded."));
        var matches = _providers.Where(item => item.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (!ValidProviderKind(kind)) return new(null, resolved.Target, resolved.Path, Invalid(path, kind, repository, $"CI provider kind is unsafe: {kind}"));
        if (matches.Length != 1) return new(null, resolved.Target, resolved.Path, Invalid(path, kind, repository, matches.Length == 0 ? $"CI provider is not loaded: {kind}" : $"CI provider kind conflict: {kind}"));
        return new(matches[0], resolved.Target, resolved.Path, null);
    }

    private Resolved Resolve(string repositoryPath, string? repository, string? provider)
    {
        var resolution = _resolver.Resolve(repositoryPath);
        if (!resolution.IsSuccess || resolution.Context is null)
            return new(null, null, Invalid(repositoryPath, provider, repository, string.Join("; ", resolution.Errors)));
        var slug = string.IsNullOrWhiteSpace(repository) ? InferRepository(resolution.Context.RepositoryPath) : repository.Trim();
        if (string.IsNullOrWhiteSpace(slug) || !ValidRepositorySlug(slug))
            return new(resolution.Context.RepositoryPath, null, Invalid(repositoryPath, provider, repository, "GitHub repository target is unavailable. Supply --repository owner/repository or configure origin/GITHUB_REPOSITORY."));
        return new(resolution.Context.RepositoryPath, new(slug), null);
    }

    private static string? InferRepository(string path)
    {
        var environment = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        if (!string.IsNullOrWhiteSpace(environment)) return environment.Trim();
        var remote = Git(path, "remote", "get-url", "origin");
        if (remote.ExitCode != 0) return null;
        var value = remote.Output.Trim().TrimEnd('/');
        var match = Regex.Match(value, @"github\.com[/:](?<slug>[^/\s]+/[^/\s]+?)(?:\.git)?$");
        return match.Success ? match.Groups["slug"].Value : null;
    }

    private static CiEvidence SaveLog(string repositoryPath, string provider, CisCiJobLog log)
    {
        var rawHash = log.ContentDigest is { Length: 64 } digest && digest.All(Uri.IsHexDigit)
            ? digest.ToLowerInvariant() : Convert.ToHexString(SHA256.HashData(log.Content)).ToLowerInvariant();
        var truncated = log.Truncated || log.Content.Length > MaximumLogBytes;
        var content = Encoding.UTF8.GetString(log.Content, 0, Math.Min(log.Content.Length, MaximumLogBytes));
        content = Redact(content);
        var relative = $".cis/local/ci/{provider}/logs/job-{log.JobId}-{rawHash[..12]}.log";
        var path = Path.Combine(repositoryPath, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return new("job-log", relative, rawHash, log.TotalBytes ?? log.Content.LongLength, truncated, "known credential and authorization patterns replaced");
    }

    private static CiFailure Classify(CisCiJob job, string log, string evidence)
    {
        var failedStep = job.Steps.LastOrDefault(item => item.Conclusion is "failure" or "timed_out" or "cancelled")?.Name ?? "unknown";
        var lower = log.ToLowerInvariant();
        var classification = job.Conclusion == "timed_out" ? "timeout"
            : job.Conclusion == "cancelled" ? "cancellation"
            : lower.Contains("no space left") || lower.Contains("cannot connect to the docker daemon") || lower.Contains("runner lost") ? "runner-infrastructure"
            : lower.Contains("command not found") || lower.Contains("not recognized as") || lower.Contains("sdk not found") ? "missing-prerequisite"
            : lower.Contains("assert") || lower.Contains("test failed") || lower.Contains("failed:") ? "assertion-product"
            : "unknown";
        var commands = Suggestions(job.Name, failedStep, log);
        return new(job.RunId, job.Id, job.Name, failedStep, classification,
            $"{job.Name} failed in '{failedStep}' and was classified as {classification}.", commands, [evidence, job.Url]);
    }

    private static IReadOnlyList<string> Suggestions(string job, string step, string log)
    {
        var text = $"{job} {step} {log}".ToLowerInvariant();
        var commands = new List<string>();
        if (text.Contains("dotnet")) commands.Add("dotnet test --no-restore");
        if (text.Contains("vitest") || text.Contains("npm test")) commands.Add("npm test -- --run");
        if (text.Contains("playwright")) commands.Add("npx playwright test --retries=0");
        if (text.Contains("terraform")) commands.Add("terraform validate");
        if (text.Contains("docker") || text.Contains("compose")) commands.Add("docker compose config --quiet");
        return commands.Count == 0 ? ["Inspect the preserved failed-step log and run the repository-owned focused workflow command."] : commands;
    }

    private static string Redact(string value)
    {
        value = GitHubTokenRegex().Replace(value, "[REDACTED-GITHUB-TOKEN]");
        value = AuthorizationRegex().Replace(value, "$1[REDACTED]");
        value = TokenRegex().Replace(value, "$1=[REDACTED]");
        value = JsonSecretRegex().Replace(value, "$1[REDACTED]$3");
        value = UriCredentialRegex().Replace(value, "$1[REDACTED]@");
        return value;
    }
    [GeneratedRegex("(?i)(authorization\\s*[:=]\\s*(?:bearer\\s+)?)[^\\s]+")]
    private static partial Regex AuthorizationRegex();
    [GeneratedRegex("(?i)\\b(token|password|secret|api[_-]?key)\\s*=\\s*[^\\s]+")]
    private static partial Regex TokenRegex();
    [GeneratedRegex("(?i)([\\\"']?(?:token|password|secret|api[_-]?key|client[_-]?secret)[\\\"']?\\s*:\\s*[\\\"']?)([^\\\"'\\s,;}]+)([\\\"']?)")]
    private static partial Regex JsonSecretRegex();
    [GeneratedRegex("(?i)(https?://[^:/\\s]+:)[^@/\\s]+@")]
    private static partial Regex UriCredentialRegex();
    [GeneratedRegex("\\b(?:gh[opusr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,})\\b")]
    private static partial Regex GitHubTokenRegex();

    private static CiInvestigationResult Invalid(string? path, string? provider, string? target, string error)
        => Result("invalid-request", path, provider ?? "none", target, errors: [error]);
    private static CiInvestigationResult Failure(Selection selection, Exception exception)
        => Result("provider-error", selection.Path, selection.Provider?.Kind ?? "none", selection.Target?.Repository, errors: [Limit(Redact(exception.Message))]);
    private static string Limit(string value) => value.Length <= 500 ? value : value[..500];
    private static CiInvestigationResult Result(string status, string? path, string provider, string? target,
        IReadOnlyList<CisCiProviderAvailability>? availability = null, IReadOnlyList<CisCiCheck>? checks = null,
        IReadOnlyList<CisCiRun>? runs = null, IReadOnlyList<CisCiJob>? jobs = null, IReadOnlyList<CisCiArtifact>? artifacts = null,
        IReadOnlyList<CiFailure>? failures = null, IReadOnlyList<CiEvidence>? evidence = null, IReadOnlyList<string>? errors = null, bool applied = false)
        => new(status, path, provider, target, availability ?? [], checks ?? [], runs ?? [], jobs ?? [], artifacts ?? [], failures ?? [], evidence ?? [], errors ?? [], applied);

    private static (int ExitCode, string Output) Git(string path, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = path };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            var result = CisProcessSafety.Run(start, TimeSpan.FromSeconds(5));
            return (result.ExitCode ?? 1, result.StandardOutput.Trim());
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { return (1, string.Empty); }
    }
    private static bool ValidProviderKind(string value) => value.Length is > 0 and <= 64
        && char.IsAsciiLetterOrDigit(value[0])
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
    private static bool ValidRepositorySlug(string value)
    {
        var parts = value.Split('/');
        return parts.Length == 2 && parts.All(part => part.Length is > 0 and <= 100 && part is not "." and not ".."
            && part.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'));
    }
    private sealed record Resolved(string? Path, CisCiTarget? Target, CiInvestigationResult? Error);
    private sealed record Selection(ICisCiProvider? Provider, CisCiTarget? Target, string? Path, CiInvestigationResult? Error);
}
