using System.Security.Cryptography;
using System.Net.Http.Headers;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Providers.Ci.GitHub;

public sealed class GitHubCiProvider : ICisCiProvider
{
    private const int MaximumJsonBytes = 16 * 1024 * 1024;
    private const int MaximumRetainedLogBytes = 10 * 1024 * 1024;
    private const int MaximumSourceLogBytes = 100 * 1024 * 1024;
    private readonly HttpClient _client;
    private readonly Func<string, string?> _environment;
    private readonly Func<string?> _ghCredential;
    public string Kind => "github";

    public GitHubCiProvider() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, Environment.GetEnvironmentVariable, ResolveGhCredential) { }
    public GitHubCiProvider(HttpClient client, Func<string, string?> environment, Func<string?>? ghCredential = null)
    {
        _client = client; _environment = environment; _ghCredential = ghCredential ?? (() => null);
    }

    public CisCiProviderAvailability Probe(CisCiTarget target)
    {
        if (!ValidTarget(target, out var targetError)) return new(false, targetError);
        if (Credential() is null) return new(false, "GitHub credential is unavailable from GH_TOKEN, GITHUB_TOKEN, or gh auth.");
        try
        {
            using var response = Send(target, HttpMethod.Get, string.Empty);
            return response.IsSuccessStatusCode
                ? new(true, "GitHub Actions repository and credential are available.")
                : new(false, $"GitHub repository probe returned {(int)response.StatusCode}.");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
        { return new(false, Limit(exception.Message)); }
    }

    public IReadOnlyList<CisCiCheck> PullRequestChecks(CisCiTarget target, int pullRequest)
    {
        using var pr = Json(target, HttpMethod.Get, $"pulls/{pullRequest}");
        var sha = pr.RootElement.GetProperty("head").GetProperty("sha").GetString()
            ?? throw new InvalidOperationException("GitHub pull request has no head SHA.");
        using var checks = Json(target, HttpMethod.Get, $"commits/{Uri.EscapeDataString(sha)}/check-runs?per_page=100");
        return checks.RootElement.GetProperty("check_runs").EnumerateArray().Select(item => new CisCiCheck(
            item.GetProperty("id").GetInt64(), Text(item, "name"), Text(item, "status"), Text(item, "conclusion"),
            Text(item, "html_url"), Time(item, "started_at"), Time(item, "completed_at"))).ToArray();
    }

    public IReadOnlyList<CisCiRun> Runs(CisCiTarget target, int? pullRequest, int limit)
    {
        string? sha = null;
        if (pullRequest is not null)
        {
            using var pr = Json(target, HttpMethod.Get, $"pulls/{pullRequest}");
            sha = pr.RootElement.GetProperty("head").GetProperty("sha").GetString();
        }
        var query = $"actions/runs?per_page={Math.Clamp(limit, 1, 100)}" + (sha is null ? string.Empty : $"&head_sha={Uri.EscapeDataString(sha)}");
        using var document = Json(target, HttpMethod.Get, query);
        return document.RootElement.GetProperty("workflow_runs").EnumerateArray().Select(item => new CisCiRun(
            item.GetProperty("id").GetInt64(), Text(item, "name"), Text(item, "event"), Text(item, "status"), Text(item, "conclusion"),
            Text(item, "head_sha"), Text(item, "head_branch"), Number(item, "run_attempt", 1), Text(item, "html_url"), Time(item, "created_at"), Time(item, "updated_at"))).ToArray();
    }

    public IReadOnlyList<CisCiJob> Jobs(CisCiTarget target, long runId)
    {
        using var document = Json(target, HttpMethod.Get, $"actions/runs/{runId}/jobs?per_page=100");
        return document.RootElement.GetProperty("jobs").EnumerateArray().Select(item => new CisCiJob(
            item.GetProperty("id").GetInt64(), runId, Text(item, "name"), Text(item, "status"), Text(item, "conclusion"),
            Number(item, "run_attempt", 1), Text(item, "runner_name"), Text(item, "html_url"), Time(item, "started_at"), Time(item, "completed_at"),
            item.TryGetProperty("steps", out var steps) ? steps.EnumerateArray().Select(step => new CisCiStep(
                Number(step, "number", 0), Text(step, "name"), Text(step, "status"), Text(step, "conclusion"), Time(step, "started_at"), Time(step, "completed_at"))).ToArray() : [])).ToArray();
    }

    public CisCiJobLog JobLog(CisCiTarget target, long jobId)
    {
        using var response = Send(target, HttpMethod.Get, $"actions/jobs/{jobId}/logs");
        Ensure(response);
        var (content, total, truncated, digest) = ReadLog(response);
        return new(jobId, response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", content, total, truncated, digest);
    }

    public IReadOnlyList<CisCiArtifact> Artifacts(CisCiTarget target, long runId)
    {
        using var document = Json(target, HttpMethod.Get, $"actions/runs/{runId}/artifacts?per_page=100");
        return document.RootElement.GetProperty("artifacts").EnumerateArray().Select(item => new CisCiArtifact(
            item.GetProperty("id").GetInt64(), Text(item, "name"), item.TryGetProperty("size_in_bytes", out var size) ? size.GetInt64() : 0,
            item.TryGetProperty("expired", out var expired) && expired.GetBoolean(), Text(item, "archive_download_url"), Time(item, "created_at"), Time(item, "expires_at"))).ToArray();
    }

    public void RerunFailed(CisCiTarget target, long runId)
    {
        using var response = Send(target, HttpMethod.Post, $"actions/runs/{runId}/rerun-failed-jobs");
        Ensure(response);
    }

    private JsonDocument Json(CisCiTarget target, HttpMethod method, string relative)
    {
        using var response = Send(target, method, relative); Ensure(response);
        return JsonDocument.Parse(ReadBounded(response.Content.ReadAsStream(), MaximumJsonBytes, "GitHub JSON response"));
    }
    private HttpResponseMessage Send(CisCiTarget target, HttpMethod method, string relative)
    {
        if (!ValidTarget(target, out var targetError)) throw new InvalidOperationException(targetError);
        var token = Credential() ?? throw new InvalidOperationException("GitHub credential is unavailable.");
        var url = new Uri(new Uri(target.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute), $"repos/{target.Repository}/{relative}".TrimEnd('/'));
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("change-impact-studio/0.3");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return _client.Send(request, HttpCompletionOption.ResponseHeadersRead);
    }
    private string? Credential()
        => Present(_environment("GH_TOKEN")) ?? Present(_environment("GITHUB_TOKEN")) ?? Present(_ghCredential());
    private static string? Present(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool ValidTarget(CisCiTarget target, out string error)
    {
        error = string.Empty;
        var parts = target.Repository.Split('/');
        if (parts.Length != 2 || parts.Any(part => !SafeSegment(part)))
        { error = "GitHub repository must be a safe owner/repository identifier."; return false; }
        if (!Uri.TryCreate(target.BaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.Query.Length > 0 || uri.Fragment.Length > 0
            || uri.AbsolutePath.Trim('/').Length > 0)
        { error = "The built-in GitHub provider only sends credentials to https://api.github.com."; return false; }
        return true;
    }
    private static bool SafeSegment(string value) => value.Length is > 0 and <= 100 && value is not "." and not ".."
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
    private static string Text(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? value.ToString() : string.Empty;
    private static int Number(JsonElement item, string property, int fallback) => item.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : fallback;
    private static DateTimeOffset? Time(JsonElement item, string property) => DateTimeOffset.TryParse(Text(item, property), out var result) ? result : null;
    private static void Ensure(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = System.Text.Encoding.UTF8.GetString(ReadBounded(response.Content.ReadAsStream(), 4096, "GitHub error response"));
        throw new InvalidOperationException($"GitHub returned {(int)response.StatusCode}: {Limit(detail)}");
    }
    private static string Limit(string value) => value.Length <= 500 ? value : value[..500];
    private static string? ResolveGhCredential()
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("gh");
            start.ArgumentList.Add("auth"); start.ArgumentList.Add("token");
            var result = CisProcessSafety.Run(start, TimeSpan.FromSeconds(5), maximumCharactersPerStream: 16_384);
            return !result.TimedOut && !result.OutputTruncated && result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }

    private static byte[] ReadBounded(Stream input, int limit, string operation)
    {
        using var output = new MemoryStream(); var buffer = new byte[81920]; var total = 0; int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        { total += read; if (total > limit) throw new InvalidOperationException($"{operation} exceeds the {limit} byte limit."); output.Write(buffer, 0, read); }
        return output.ToArray();
    }

    private static (byte[] Content, long Total, bool Truncated, string Digest) ReadLog(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentLength is > MaximumSourceLogBytes)
            throw new InvalidOperationException($"GitHub job log exceeds the {MaximumSourceLogBytes} byte source limit.");
        using var input = response.Content.ReadAsStream(); using var retained = new MemoryStream();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920]; long total = 0; int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read; if (total > MaximumSourceLogBytes) throw new InvalidOperationException($"GitHub job log exceeds the {MaximumSourceLogBytes} byte source limit.");
            hash.AppendData(buffer.AsSpan(0, read));
            var remaining = MaximumRetainedLogBytes - (int)retained.Length;
            if (remaining > 0) retained.Write(buffer, 0, Math.Min(read, remaining));
        }
        return (retained.ToArray(), total, total > MaximumRetainedLogBytes, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
