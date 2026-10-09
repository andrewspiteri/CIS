using System.Net.Http.Headers;
using Example.Persistence;
using Example.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Example.WebTests;

public sealed class WebFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:16.8-alpine").Build();
    private readonly SyntheticIdentity _identity = new();
    private WebApplication? _app;
    public Uri Address { get; private set; } = null!;
    public string EvidenceDirectory { get; } = CreateEvidenceDirectory();

    private static string CreateEvidenceDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "WebHarness.slnx"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("The web example root is missing.");
        var path = Path.Combine(root.FullName, ".cis/local/web", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync();
        _app = ReadingWebHost.Create(_database.GetConnectionString(), _identity.SigningKey);
        await _app.Services.GetRequiredService<PostgresReadingStore>().InitializeAsync(TestContext.Current.CancellationToken);
        await _app.StartAsync(TestContext.Current.CancellationToken);
        Address = new Uri(Assert.Single(_app.Urls));
    }

    public string Token(bool canWrite = true, bool expired = false)
        => _identity.Token(canWrite, expired);

    public HttpClient Client(string? token = null)
    {
        var client = new HttpClient { BaseAddress = Address, Timeout = TimeSpan.FromSeconds(10) };
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
        await _database.DisposeAsync();
        _identity.Dispose();
    }
}
