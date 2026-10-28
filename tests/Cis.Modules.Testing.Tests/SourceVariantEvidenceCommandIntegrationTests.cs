using System.Text.Json;
using Xunit;

namespace Cis.Modules.Testing.Tests;

[Collection("Source-variant native evidence")]
public sealed class SourceVariantEvidenceCommandIntegrationTests
{
    [Fact]
    public void SourceVariantCommandVerifiesActualNativePhaseStatesInEveryOutputFormatWithoutApplyingCredit()
    {
        using var fixture = new SourceVariantEvidenceTests.NativeFixture();
        fixture.RunPhases();
        string manifest = fixture.WriteManifest();
        var before = fixture.AllArtifacts();
        foreach (string format in new[] { "json", "human", "agent" })
        {
            var (exitCode, output) = TestingServiceTests.InvokeSourceVariantCommand(fixture, fixture.Options.RepositoryPath, manifest, format);
            Assert.True(exitCode == 0, output);
            if (format == "json")
            {
                using var result = JsonDocument.Parse(output);
                Assert.Equal("verified-source-variant", result.RootElement.GetProperty("status").GetString());
                Assert.False(result.RootElement.GetProperty("qualified").GetBoolean());
                Assert.True(result.RootElement.GetProperty("partial").GetBoolean());
                Assert.Equal(3, result.RootElement.GetProperty("phases").GetArrayLength());
                Assert.False(result.RootElement.TryGetProperty("effective", out _));
                Assert.False(result.RootElement.TryGetProperty("credits", out _));
            }
            else { Assert.Contains("verified-source-variant", output, StringComparison.Ordinal); }
        }
        foreach (var artifact in before)
        {
            using var stream = File.OpenRead(artifact.Key);
            Assert.Equal(artifact.Value, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream)));
        }
    }
}
