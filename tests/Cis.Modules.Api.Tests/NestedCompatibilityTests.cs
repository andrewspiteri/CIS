using System.Text.Json;
using Cis.Modules.Repository;

namespace Cis.Modules.Api.Tests;

public sealed partial class ApiGovernanceTests
{
    private static string SchemaDocument(string schema, string definitions = "{}")
        => "{\"openapi\":\"3.0.1\",\"components\":{\"schemas\":" + definitions + "},\"paths\":{\"/feed\":{\"post\":{\"requestBody\":{\"content\":{\"application/json\":{\"schema\":" + schema + "}}},\"responses\":{\"200\":{\"content\":{\"application/json\":{\"schema\":" + schema + "}}}}}}}}";

    [Fact]
    public void NestedReferencesAndArraysDetectRemovedPropertiesTypesAndRequiredFields()
    {
        using var repository = ApiRepository.Create(validPublic: true);
        const string schema = """{"type":"object","properties":{"feeds":{"type":"array","items":{"$ref":"#/components/schemas/Feed"}}}}""";
        repository.Write("docs/openapi/baseline.json", SchemaDocument(schema,
            """{"Feed":{"type":"object","properties":{"count":{"type":"integer"},"removed":{"type":"string"}}}}"""));
        repository.Write("docs/openapi/current.json", SchemaDocument(schema,
            """{"Feed":{"type":"object","required":["count"],"properties":{"count":{"type":"string"},"accepted_count":{"type":"integer"},"rejected_count":{"type":"integer"}}}}"""));
        var result = new OpenApiCompatibilityService(new CisRepositoryContextResolver()).Diff(repository.Path, null, null);
        Assert.True(result.CoverageComplete);
        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Findings, item => item.Classification == "response-property-removed" && item.Message.Contains("feeds/[]/removed"));
        Assert.Contains(result.Findings, item => item.Classification == "request-required-added" && item.Message.Contains("feeds/[]/count"));
        Assert.Contains(result.Findings, item => item.Classification == "response-type-changed");
        Assert.Equal(2, result.Findings.Count(item => item.Classification == "response-property-added"));
    }

    [Fact]
    public void RelaxingNestedResponseRequirementIsBreakingEvenWhenThePropertyRemains()
    {
        using var repository = ApiRepository.Create(validPublic: true);
        repository.Write("docs/openapi/baseline.json", SchemaDocument("""{"properties":{"feed":{"required":["id"],"properties":{"id":{"type":"string"}}}}}"""));
        repository.Write("docs/openapi/current.json", SchemaDocument("""{"properties":{"feed":{"properties":{"id":{"type":"string"}}}}}"""));
        var result = new OpenApiCompatibilityService(new CisRepositoryContextResolver()).Diff(repository.Path, null, null);
        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Findings, item => item.Classification == "response-required-relaxed" && item.Message.Contains("feed/id"));
    }

    [Theory]
    [InlineData("{\"type\":\"string\",\"nullable\":true}", "{}")]
    [InlineData("{\"oneOf\":[{\"type\":\"string\"},{\"type\":\"integer\"}]}", "{}")]
    [InlineData("{\"$ref\":\"#/components/schemas/Node\"}", "{\"Node\":{\"properties\":{\"child\":{\"$ref\":\"#/components/schemas/Node\"}}}}")]
    [InlineData("{\"$ref\":\"https://example.invalid/schema\"}", "{}")]
    [InlineData("{\"$ref\":\"#/components/schemas/Missing\"}", "{}")]
    public void UnsupportedAndRecursiveSchemasArePartialEvenWithZeroFindings(string schema, string definitions)
    {
        using var repository = ApiRepository.Create(validPublic: true);
        repository.Write("docs/openapi/baseline.json", SchemaDocument(schema, definitions));
        repository.Write("docs/openapi/current.json", SchemaDocument(schema, definitions));
        var result = new OpenApiCompatibilityService(new CisRepositoryContextResolver()).Diff(repository.Path, null, null);
        Assert.False(result.CoverageComplete);
        Assert.NotEmpty(result.CoverageLimitations);
        Assert.Equal("partial", result.Status);
        Assert.Equal(4, result.ExitCode);
    }
}
