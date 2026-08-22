using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cis.Modules.Tracker;

namespace Cis.Providers.Tracker.GitHub;

public sealed class GitHubTrackerProvider : ICisTrackerProvider
{
    private readonly HttpClient _client;
    private readonly Func<string, string?> _environment;
    public string Kind => "github";

    public GitHubTrackerProvider() : this(new HttpClient(), Environment.GetEnvironmentVariable) { }

    public GitHubTrackerProvider(HttpClient client, Func<string, string?>? environment = null)
    {
        _client = client;
        _environment = environment ?? Environment.GetEnvironmentVariable;
    }

    public TrackerAvailability Probe(TrackerProviderConfiguration configuration)
    {
        if (!TryTarget(configuration.Target, out _, out _))
            return new(false, "GitHub target must be owner/repository.");
        return Credential(configuration, out _) is null
            ? new(false, $"GitHub credential is unavailable from '{configuration.CredentialSource}'.")
            : new(true, "GitHub Issues configuration and credential are available.");
    }

    public TrackerRemoteItem? Get(TrackerProviderConfiguration configuration, string remoteId)
    {
        using var response = Send(configuration, HttpMethod.Get, $"issues/{Uri.EscapeDataString(remoteId)}", null);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        EnsureSuccess(response);
        return Read(response);
    }

    public TrackerRemoteItem Create(TrackerProviderConfiguration configuration, TrackerTaskProjection task)
    {
        using var response = Send(configuration, HttpMethod.Post, "issues", new
        {
            title = task.Title,
            body = task.Body,
            labels = task.Labels,
        });
        EnsureSuccess(response);
        return Read(response);
    }

    public TrackerRemoteItem Update(TrackerProviderConfiguration configuration, string remoteId, TrackerTaskProjection task)
    {
        using var response = Send(configuration, HttpMethod.Patch, $"issues/{Uri.EscapeDataString(remoteId)}", new
        {
            title = task.Title,
            body = task.Body,
            labels = task.Labels,
        });
        EnsureSuccess(response);
        return Read(response);
    }

    private HttpResponseMessage Send(TrackerProviderConfiguration configuration, HttpMethod method, string relative, object? body)
    {
        if (!TryTarget(configuration.Target, out var owner, out var repository))
            throw new InvalidOperationException("GitHub target must be owner/repository.");
        var token = Credential(configuration, out var credentialError)
            ?? throw new InvalidOperationException(credentialError);
        var baseUrl = string.IsNullOrWhiteSpace(configuration.BaseUrl) ? "https://api.github.com" : configuration.BaseUrl.TrimEnd('/');
        using var request = new HttpRequestMessage(method,
            $"{baseUrl}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/{relative}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("change-impact-studio/1.0");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (body is not null) request.Content = JsonContent.Create(body);
        return _client.Send(request);
    }

    private static TrackerRemoteItem Read(HttpResponseMessage response)
    {
        using var stream = response.Content.ReadAsStream();
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        var labels = root.TryGetProperty("labels", out var labelArray)
            ? labelArray.EnumerateArray().Select(label => label.ValueKind == JsonValueKind.String
                ? label.GetString() ?? ""
                : label.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "").Where(value => value.Length > 0).ToArray()
            : [];
        return new(
            root.GetProperty("number").GetInt64().ToString(),
            root.GetProperty("html_url").GetString() ?? "",
            root.GetProperty("title").GetString() ?? "",
            root.TryGetProperty("body", out var body) && body.ValueKind != JsonValueKind.Null ? body.GetString() ?? "" : "",
            root.TryGetProperty("state", out var state) ? state.GetString() ?? "open" : "open",
            labels,
            root.TryGetProperty("updated_at", out var updated) ? updated.GetString() ?? "" : "",
            "");
    }

    private string? Credential(TrackerProviderConfiguration configuration, out string error)
    {
        const string prefix = "environment:";
        if (!configuration.CredentialSource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "GitHub credential source must use environment:<variable>.";
            return null;
        }
        var variable = configuration.CredentialSource[prefix.Length..].Trim();
        var value = variable.Length == 0 ? null : _environment(variable);
        error = value is null ? $"Environment variable '{variable}' is not set." : "";
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool TryTarget(string value, out string owner, out string repository)
    {
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        owner = parts.Length == 2 ? parts[0] : "";
        repository = parts.Length == 2 ? parts[1] : "";
        return parts.Length == 2;
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        throw new InvalidOperationException($"GitHub returned {(int)response.StatusCode}: {Limit(detail)}");
    }

    private static string Limit(string value) => value.Length <= 500 ? value : value[..500];
}
