using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Example.Core;
using Example.Web;

namespace Example.WebTests;

public sealed class ApiAcceptanceTests(WebFixture server, ITestOutputHelper output) : IClassFixture<WebFixture>
{
    [Fact]
    [Trait("Layer", "integration")]
    [Trait("Layer", "regression")]
    public async Task ApiPersistsExactValuesAndReplayIsIdempotent()
    {
        using var client = server.Client(server.Token());
        var reading = new Reading(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 0.00000001m);
        using var first = await client.PostAsJsonAsync("/api/readings", reading, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(new ReadingAccepted(1, 1), await first.Content.ReadFromJsonAsync<ReadingAccepted>(TestContext.Current.CancellationToken));
        using var replay = await client.PostAsJsonAsync("/api/readings", reading, TestContext.Current.CancellationToken);
        Assert.Equal(new ReadingAccepted(1, 0), await replay.Content.ReadFromJsonAsync<ReadingAccepted>(TestContext.Current.CancellationToken));
        using var conflict = await client.PostAsJsonAsync("/api/readings", reading with { Quantity = 1 }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var actual = await client.GetFromJsonAsync<Reading[]>("/api/readings", TestContext.Current.CancellationToken);
        Assert.Equal(reading, Assert.Single(actual!, item => item.Id == reading.Id));
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("read-only", HttpStatusCode.Forbidden)]
    [Trait("Layer", "security")]
    public async Task UnauthorizedWritesCannotPersistOrDiscloseInternals(string identity, HttpStatusCode expected)
    {
        var token = identity switch { "expired" => server.Token(expired: true), "read-only" => server.Token(canWrite: false), _ => null };
        using var denied = server.Client(token);
        var reading = new Reading(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 1);
        using var response = await denied.PostAsJsonAsync("/api/readings", reading, TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var reader = server.Client(server.Token(canWrite: false));
        var stored = await reader.GetFromJsonAsync<Reading[]>("/api/readings", TestContext.Current.CancellationToken);
        Assert.DoesNotContain(stored!, item => item.Id == reading.Id);
    }

    [Fact]
    [Trait("Layer", "api-compatibility")]
    public async Task NativeOpenApiRetainsReadWriteAndErrorContracts()
    {
        using var client = server.Client(server.Token(canWrite: false));
        var text = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(text);
        var contract = document.RootElement.GetProperty("paths").GetProperty("/api/readings");
        Assert.True(contract.TryGetProperty("get", out _));
        var responses = contract.GetProperty("post").GetProperty("responses");
        foreach (var status in new[] { "200", "400", "409", "401", "403" }) Assert.True(responses.TryGetProperty(status, out _));
        Assert.True(contract.GetProperty("post").GetProperty("security")[0].TryGetProperty("Bearer", out _));
        var directory = server.EvidenceDirectory;
        await File.WriteAllTextAsync(Path.Combine(directory, "openapi.json"), text, TestContext.Current.CancellationToken);
        output.WriteLine($"Native OpenAPI evidence: {directory}");
    }
}
