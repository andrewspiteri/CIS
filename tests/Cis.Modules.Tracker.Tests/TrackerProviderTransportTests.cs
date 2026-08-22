using System.Net;
using System.Text;
using System.Text.Json;
using Cis.Modules.Tracker;
using Cis.Providers.Tracker.GitHub;
using Cis.Providers.Tracker.Jira;

namespace Cis.Modules.Tracker.Tests;

public sealed class TrackerProviderTransportTests
{
    private static readonly TrackerTaskProjection Task = new(
        "repo", "CIS-0001", "WORK-001", "backend", "backend", "Ready", "Implement governed work",
        "Canonical body", ["cis", "backend"], "docs/cis/changes/CIS-0001/agent-tasks/WORK-001.md", "digest");

    [Fact]
    public void GitHubProvider_UsesExpectedIssueContractAndCredentials()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.Created, GitHubIssue(42, "Implement governed work")),
            Json(HttpStatusCode.OK, GitHubIssue(42, "Implement governed work")),
            Json(HttpStatusCode.NotFound, "{}"));
        var provider = new GitHubTrackerProvider(new HttpClient(handler), name => name == "GH_TOKEN" ? "secret" : null);
        var configuration = new TrackerProviderConfiguration("github", "github", true, "owner/repo", "https://api.github.test", "push", "Issue", "environment:GH_TOKEN");

        var created = provider.Create(configuration, Task);
        var read = provider.Get(configuration, "42");
        var missing = provider.Get(configuration, "404");

        Assert.Equal("42", created.Id);
        Assert.NotNull(read);
        Assert.Null(missing);
        Assert.All(handler.Requests, request => Assert.Equal("Bearer secret", request.Authorization));
        Assert.Equal("https://api.github.test/repos/owner/repo/issues", handler.Requests[0].Url);
        Assert.Contains("Canonical body", handler.Requests[0].Body);
    }

    [Fact]
    public void GitHubProvider_FailsClosedWhenCredentialIsUnavailable()
    {
        var provider = new GitHubTrackerProvider(new HttpClient(new QueueHandler()), _ => null);
        var configuration = new TrackerProviderConfiguration("github", "github", true, "owner/repo", "", "push", "Issue", "environment:GH_TOKEN");
        var availability = provider.Probe(configuration);
        Assert.False(availability.Available);
        Assert.Contains("unavailable", availability.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JiraProvider_CreatesWithAdfAndReadsCanonicalProjection()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.Created, "{\"id\":\"10001\",\"key\":\"CIS-7\"}"),
            Json(HttpStatusCode.OK, JiraIssue("CIS-7", "Implement governed work")),
            new HttpResponseMessage(HttpStatusCode.NoContent),
            Json(HttpStatusCode.OK, JiraIssue("CIS-7", "Implement governed work")));
        var provider = new JiraTrackerProvider(new HttpClient(handler), name => name switch { "JIRA_EMAIL" => "dev@example.test", "JIRA_TOKEN" => "token", _ => null });
        var configuration = new TrackerProviderConfiguration("jira", "jira", true, "CIS", "https://jira.test", "both", "Task", "basic-environment:JIRA_EMAIL:JIRA_TOKEN");

        var created = provider.Create(configuration, Task);
        var updated = provider.Update(configuration, "CIS-7", Task);

        Assert.Equal("CIS-7", created.Id);
        Assert.Equal("CIS-7", updated.Id);
        Assert.StartsWith("Basic ", handler.Requests[0].Authorization, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"doc\"", handler.Requests[0].Body);
        Assert.Equal("https://jira.test/rest/api/3/issue", handler.Requests[0].Url);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) => new(code)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static string GitHubIssue(int number, string title) => JsonSerializer.Serialize(new
    {
        number, html_url = $"https://github.test/owner/repo/issues/{number}", title,
        body = "Canonical body", state = "open", labels = new[] { new { name = "cis" } },
        updated_at = "2026-08-14T12:00:00Z",
    });

    private static string JiraIssue(string key, string title) => JsonSerializer.Serialize(new
    {
        key,
        fields = new
        {
            summary = title,
            description = new
            {
                type = "doc", version = 1,
                content = new[] { new { type = "paragraph", content = new[] { new { type = "text", text = "Canonical body" } } } },
            },
            labels = new[] { "cis" }, updated = "2026-08-14T12:00:00.000+0000",
            status = new { statusCategory = new { key = "new" } },
        },
    });

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<RequestRecord> Requests { get; } = [];
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => Take(request, cancellationToken);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => System.Threading.Tasks.Task.FromResult(Take(request, cancellationToken));
        private HttpResponseMessage Take(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? "";
            Requests.Add(new(request.Method.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString() ?? "", body));
            return _responses.Count > 0 ? _responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.InternalServerError)
            { Content = new StringContent("No queued response") };
        }
    }

    private sealed record RequestRecord(string Method, string Url, string Authorization, string Body);
}
