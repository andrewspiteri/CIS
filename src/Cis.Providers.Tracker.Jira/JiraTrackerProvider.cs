using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Cis.Modules.Tracker;

namespace Cis.Providers.Tracker.Jira;

public sealed class JiraTrackerProvider : ICisTrackerProvider
{
    private readonly HttpClient _client;
    private readonly Func<string, string?> _environment;
    public string Kind => "jira";

    public JiraTrackerProvider() : this(new HttpClient(), Environment.GetEnvironmentVariable) { }
    public JiraTrackerProvider(HttpClient client, Func<string, string?>? environment = null)
    {
        _client = client;
        _environment = environment ?? Environment.GetEnvironmentVariable;
    }

    public TrackerAvailability Probe(TrackerProviderConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.BaseUrl) || !Uri.TryCreate(configuration.BaseUrl, UriKind.Absolute, out _))
            return new(false, "Jira base URL must be an absolute URL.");
        if (string.IsNullOrWhiteSpace(configuration.Target)) return new(false, "Jira target must be a project key.");
        return Credentials(configuration, out _, out _) ? new(true, "Jira configuration and credentials are available.")
            : new(false, "Jira credentials are unavailable; use basic-environment:<email-variable>:<token-variable>.");
    }

    public TrackerRemoteItem? Get(TrackerProviderConfiguration configuration, string remoteId)
    {
        using var response = Send(configuration, HttpMethod.Get, $"issue/{Uri.EscapeDataString(remoteId)}", null);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        EnsureSuccess(response); return Read(configuration, response);
    }

    public TrackerRemoteItem Create(TrackerProviderConfiguration configuration, TrackerTaskProjection task)
    {
        var issueType = string.IsNullOrWhiteSpace(configuration.IssueType) ? "Task" : configuration.IssueType;
        using var response = Send(configuration, HttpMethod.Post, "issue", new { fields = new
        {
            project = new { key = configuration.Target }, summary = task.Title,
            description = Document(task.Body), issuetype = new { name = issueType }, labels = task.Labels,
        }});
        EnsureSuccess(response);
        using var stream = response.Content.ReadAsStream(); using var json = JsonDocument.Parse(stream);
        var key = json.RootElement.GetProperty("key").GetString() ?? throw new InvalidOperationException("Jira create response omitted key.");
        return Get(configuration, key) ?? throw new InvalidOperationException("Jira created issue could not be read back.");
    }

    public TrackerRemoteItem Update(TrackerProviderConfiguration configuration, string remoteId, TrackerTaskProjection task)
    {
        using var response = Send(configuration, HttpMethod.Put, $"issue/{Uri.EscapeDataString(remoteId)}", new { fields = new
        {
            summary = task.Title, description = Document(task.Body), labels = task.Labels,
        }});
        EnsureSuccess(response);
        return Get(configuration, remoteId) ?? throw new InvalidOperationException("Jira updated issue could not be read back.");
    }

    private HttpResponseMessage Send(TrackerProviderConfiguration configuration, HttpMethod method, string relative, object? body)
    {
        if (!Credentials(configuration, out var email, out var token))
            throw new InvalidOperationException("Jira credentials are unavailable; use basic-environment:<email-variable>:<token-variable>.");
        using var request = new HttpRequestMessage(method, $"{configuration.BaseUrl.TrimEnd('/')}/rest/api/3/{relative}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{token}")));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = JsonContent.Create(body);
        return _client.Send(request);
    }

    private static object Document(string text) => new
    {
        type = "doc", version = 1,
        content = new[] { new { type = "paragraph", content = new[] { new { type = "text", text } } } },
    };

    private static TrackerRemoteItem Read(TrackerProviderConfiguration configuration, HttpResponseMessage response)
    {
        using var stream = response.Content.ReadAsStream(); using var json = JsonDocument.Parse(stream);
        var root = json.RootElement; var fields = root.GetProperty("fields"); var key = root.GetProperty("key").GetString() ?? "";
        var labels = fields.TryGetProperty("labels", out var values) ? values.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray() : [];
        var body = fields.TryGetProperty("description", out var description) ? PlainText(description) : "";
        var status = fields.TryGetProperty("status", out var state) && state.TryGetProperty("statusCategory", out var category)
            && category.TryGetProperty("key", out var categoryKey) ? categoryKey.GetString() ?? "open" : "open";
        return new(key, $"{configuration.BaseUrl.TrimEnd('/')}/browse/{Uri.EscapeDataString(key)}",
            fields.GetProperty("summary").GetString() ?? "", body, status, labels,
            fields.TryGetProperty("updated", out var updated) ? updated.GetString() ?? "" : "", "");
    }

    private static string PlainText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String) return element.GetString() ?? "";
        var output = new List<string>(); Collect(element, output); return string.Join("\n", output.Where(x => x.Length > 0));
    }

    private static void Collect(JsonElement element, List<string> output)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) output.Add(text.GetString() ?? "");
            foreach (var property in element.EnumerateObject()) if (property.Name != "text") Collect(property.Value, output);
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) Collect(child, output);
    }

    private bool Credentials(TrackerProviderConfiguration configuration, out string email, out string token)
    {
        var parts = configuration.CredentialSource.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || !parts[0].Equals("basic-environment", StringComparison.OrdinalIgnoreCase))
        { email = token = ""; return false; }
        email = _environment(parts[1]) ?? ""; token = _environment(parts[2]) ?? "";
        return email.Length > 0 && token.Length > 0;
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        throw new InvalidOperationException($"Jira returned {(int)response.StatusCode}: {(detail.Length <= 500 ? detail : detail[..500])}");
    }
}
