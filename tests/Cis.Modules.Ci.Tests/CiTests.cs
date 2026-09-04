using System.Net;
using System.Text;
using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Ci;
using Cis.Modules.Repository;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Ci.Tests;

public sealed class CiTests
{
    [Fact]
    public void Commands_AreRegistered()
    {
        using var application = new CisHostBuilder().AddModule(new RepositoryModule()).AddModule(new CiModule()).AddModule(new FakeProviderModule()).Build();
        foreach (var command in new[] { "providers", "status", "runs", "jobs", "logs", "artifacts", "diagnose", "reproduce", "rerun-failed" })
            Assert.Equal(0, application.Invoke(["ci", command, "--help"]));
    }

    [Fact]
    public void Diagnose_ClassifiesFailureRedactsLogAndSuggestsFocusedCommand()
    {
        using var repository = Fixture.Create();
        var service = new CiService(new CisRepositoryContextResolver(), [new FakeProvider()]);
        var result = service.Diagnose(repository.Path, "fake", "owner/repo", 42);
        Assert.Equal(0, result.ExitCode);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("assertion-product", failure.Classification);
        Assert.Contains("dotnet test --no-restore", failure.ReproductionCommands);
        var evidence = Assert.Single(result.Evidence);
        var text = File.ReadAllText(System.IO.Path.Combine(repository.Path, evidence.Path.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        Assert.DoesNotContain("ghp_abcdefghijklmnopqrstuvwxyz123456", text, StringComparison.Ordinal);
        Assert.DoesNotContain("json-secret-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("uri-password", text, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Rerun_RequiresExplicitConfirmation()
    {
        using var repository = Fixture.Create(); var provider = new FakeProvider();
        var service = new CiService(new CisRepositoryContextResolver(), [provider]);
        Assert.Equal(2, service.RerunFailed(repository.Path, "fake", "owner/repo", 42, false).ExitCode);
        Assert.False(provider.RerunCalled);
        Assert.Equal(0, service.RerunFailed(repository.Path, "fake", "owner/repo", 42, true).ExitCode);
        Assert.True(provider.RerunCalled);
    }

    [Fact]
    public void GitHubProvider_ParsesRunsJobsChecksAndArtifacts()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/repos/owner/repo/pulls/7" => Json("{\"head\":{\"sha\":\"abc\"}}"),
            "/repos/owner/repo/commits/abc/check-runs" => Json("{\"check_runs\":[{\"id\":1,\"name\":\"tests\",\"status\":\"completed\",\"conclusion\":\"success\",\"html_url\":\"u\"}]}"),
            "/repos/owner/repo/actions/runs" => Json("{\"workflow_runs\":[{\"id\":42,\"name\":\"CI\",\"event\":\"pull_request\",\"status\":\"completed\",\"conclusion\":\"failure\",\"head_sha\":\"abc\",\"head_branch\":\"x\",\"run_attempt\":1,\"html_url\":\"u\"}]}"),
            "/repos/owner/repo/actions/runs/42/jobs" => Json("{\"jobs\":[{\"id\":9,\"name\":\"tests\",\"status\":\"completed\",\"conclusion\":\"failure\",\"runner_name\":\"r\",\"html_url\":\"u\",\"steps\":[]}]}"),
            "/repos/owner/repo/actions/runs/42/artifacts" => Json("{\"artifacts\":[{\"id\":3,\"name\":\"results\",\"size_in_bytes\":10,\"expired\":false,\"archive_download_url\":\"u\"}]}"),
            _ => Json("{}"),
        });
        var provider = new Cis.Providers.Ci.GitHub.GitHubCiProvider(new HttpClient(handler), _ => "token", () => null);
        var target = new CisCiTarget("owner/repo");
        Assert.Single(provider.PullRequestChecks(target, 7)); Assert.Single(provider.Runs(target, 7, 20));
        Assert.Single(provider.Jobs(target, 42)); Assert.Single(provider.Artifacts(target, 42));
    }

    [Theory]
    [InlineData("../repo", "https://api.github.com")]
    [InlineData("owner/repo", "https://example.test")]
    [InlineData("owner/repo", "http://api.github.com")]
    [InlineData("owner/repo", "https://api.github.com:444")]
    public void GitHubProvider_RejectsUnsafeCredentialTargetsWithoutSendingARequest(string repository, string baseUrl)
    {
        var calls = 0;
        var provider = new Cis.Providers.Ci.GitHub.GitHubCiProvider(new HttpClient(new StubHandler(_ =>
        { calls++; return Json("{}"); })), _ => "token", () => null);

        var result = provider.Probe(new CisCiTarget(repository, baseUrl));

        Assert.False(result.Available);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void GitHubProvider_BoundsRetainedJobLogAndPreservesFullDigestMetadata()
    {
        var content = new byte[10 * 1024 * 1024 + 17];
        Array.Fill(content, (byte)'x');
        var provider = new Cis.Providers.Ci.GitHub.GitHubCiProvider(new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) })), _ => "token", () => null);

        var result = provider.JobLog(new CisCiTarget("owner/repo"), 9);

        Assert.Equal(10 * 1024 * 1024, result.Content.Length);
        Assert.Equal(content.LongLength, result.TotalBytes);
        Assert.True(result.Truncated);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content)), result.ContentDigest);
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handler(request));
    }
    private sealed class FakeProviderModule : ICisModule
    {
        public string Name => "ci-fake"; public string Description => "test";
        public void RegisterServices(IServiceCollection services) => services.AddSingleton<ICisCiProvider, FakeProvider>();
        public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services) { }
    }
    private sealed class FakeProvider : ICisCiProvider
    {
        public string Kind => "fake"; public bool RerunCalled { get; private set; }
        public CisCiProviderAvailability Probe(CisCiTarget target) => new(true, "ok");
        public IReadOnlyList<CisCiCheck> PullRequestChecks(CisCiTarget target, int pullRequest) => [];
        public IReadOnlyList<CisCiRun> Runs(CisCiTarget target, int? pullRequest, int limit) => [];
        public IReadOnlyList<CisCiJob> Jobs(CisCiTarget target, long runId) => [new(9, runId, "dotnet tests", "completed", "failure", 1, "runner", "https://example/job", null, null, [new(1, "test", "completed", "failure", null, null)])];
        public CisCiJobLog JobLog(CisCiTarget target, long jobId) => new(jobId, "text/plain", Encoding.UTF8.GetBytes("Assert.Equal failed token=ghp_abcdefghijklmnopqrstuvwxyz123456 json={\"client_secret\":\"json-secret-value\"} url=https://user:uri-password@example.test dotnet test failed:"));
        public IReadOnlyList<CisCiArtifact> Artifacts(CisCiTarget target, long runId) => [];
        public void RerunFailed(CisCiTarget target, long runId) => RerunCalled = true;
    }
    private sealed class Fixture : IDisposable
    {
        public string Path { get; } private Fixture(string path) => Path = path;
        public static Fixture Create()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-ci-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(System.IO.Path.Combine(root, ".cis")); Directory.CreateDirectory(System.IO.Path.Combine(root, "docs"));
            File.WriteAllText(System.IO.Path.Combine(root, ".cis", "repository.yml"), "schema_version: 1\nrepository:\n  id: ci-fixture\ndocumentation_root: docs\n"); return new(root);
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
