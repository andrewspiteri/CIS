using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Mcp;

namespace Cis.Modules.Mcp.Tests;

public sealed class McpToolServiceTests
{
    [Fact]
    public void ToolCatalogue_HidesMutationsAndPublishesClosedSchemas()
    {
        var service = Service(new FixedResolver(), new RecordingQueries());

        var readOnly = service.ListTools(false);
        var enabled = service.ListTools(true);

        Assert.Equal(6, readOnly.Count);
        Assert.DoesNotContain(readOnly, tool => tool.Mutating);
        Assert.Equal(2, enabled.Count(tool => tool.Mutating));
        Assert.All(enabled, tool => Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean()));
    }

    [Fact]
    public void RepositoryStatus_UsesTheFixedResolvedScope()
    {
        var outcome = Service(new FixedResolver(), new RecordingQueries()).Call("C:/requested", false,
            "cis_repository_status", Json("{}"));
        var result = JsonSerializer.SerializeToElement(outcome.StructuredContent);

        Assert.False(outcome.IsError);
        Assert.Equal("resolved", result.GetProperty("status").GetString());
        Assert.Equal("C:/resolved", result.GetProperty("repository").GetString());
        Assert.Equal("docs/cis", result.GetProperty("documentationRoot").GetString());
    }

    [Fact]
    public void GraphCalls_ValidateRequiredValuesAndClampBounds()
    {
        var queries = new RecordingQueries();
        var service = Service(new FixedResolver(), queries);

        var find = service.Call("C:/requested", false, "cis_graph_find", Json("{\"limit\":5000}"));
        var missing = service.Call("C:/requested", false, "cis_graph_related", Json("{}"));
        var related = service.Call("C:/requested", false, "cis_graph_related",
            Json("{\"id\":\"node-1\",\"direction\":\"sideways\",\"depth\":99,\"limit\":0,\"edgeTypes\":[\"calls\",4]}"));

        Assert.False(find.IsError);
        Assert.Equal(1000, queries.FindRequest!.Limit);
        Assert.True(missing.IsError);
        Assert.Contains("Missing required arguments", JsonSerializer.Serialize(missing.StructuredContent), StringComparison.Ordinal);
        Assert.False(related.IsError);
        Assert.Equal("both", queries.RelatedRequest!.Direction);
        Assert.Equal(10, queries.RelatedRequest.Depth);
        Assert.Equal(1, queries.RelatedRequest.Limit);
        Assert.Equal(["calls"], queries.RelatedRequest.EdgeTypes);
    }

    [Fact]
    public void MutationsRequireBothServerAuthorityAndPerCallConfirmation()
    {
        var service = Service(new FixedResolver(), new RecordingQueries());
        var arguments = Json("{\"confirm\":true}");

        var disabled = service.Call("C:/requested", false, "cis_graph_build", arguments);
        var unknown = service.Call("C:/requested", false, "not-a-tool", Json("{}"));

        Assert.True(disabled.IsError);
        Assert.Contains("mutation-disabled", JsonSerializer.Serialize(disabled.StructuredContent), StringComparison.Ordinal);
        Assert.True(unknown.IsError);
        Assert.Contains("unknown-tool", JsonSerializer.Serialize(unknown.StructuredContent), StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidRepositoryFailsBeforeAnyToolDispatch()
    {
        var service = Service(new FixedResolver(valid: false), new RecordingQueries());
        var outcome = service.Call("C:/requested", false, "cis_repository_status", Json("{}"));
        Assert.True(outcome.IsError);
        Assert.Contains("invalid-repository", JsonSerializer.Serialize(outcome.StructuredContent), StringComparison.Ordinal);
    }

    private static McpToolService Service(ICisRepositoryContextResolver resolver, ICisGraphQueryService queries)
        => new(resolver, queries, null!, null!, null!);

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private sealed class FixedResolver(bool valid = true) : ICisRepositoryContextResolver
    {
        public CisRepositoryContextResolution Resolve(string repositoryPath) => valid
            ? new(new("C:/resolved", "fixture", "docs/cis", "C:/resolved/docs/cis", "C:/resolved/docs/cis/catalog.yml"), [])
            : new(null, ["not initialized"]);
    }

    private sealed class RecordingQueries : ICisGraphQueryService
    {
        public CisGraphFindRequest? FindRequest { get; private set; }
        public CisGraphRelatedRequest? RelatedRequest { get; private set; }

        public CisGraphQueryResult Find(string repositoryPath, CisGraphFindRequest request)
        { FindRequest = request; return Query(repositoryPath); }

        public CisGraphQueryResult Related(string repositoryPath, CisGraphRelatedRequest request)
        { RelatedRequest = request; return Query(repositoryPath); }

        public CisGraphTraceResult Trace(string repositoryPath, CisGraphTraceRequest request)
            => new("ok", repositoryPath, "graph.db", null, "current", new Dictionary<string, string>(), [], false, [], true, true, true);

        private static CisGraphQueryResult Query(string repositoryPath)
            => new("ok", repositoryPath, "graph.db", null, "current", new Dictionary<string, string>(), [], [], false, [], true, true, true);
    }
}
