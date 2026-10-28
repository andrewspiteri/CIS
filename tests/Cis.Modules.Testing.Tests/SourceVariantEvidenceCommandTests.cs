using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed partial class TestingServiceTests
{
    [Theory]
    [InlineData("invalid-json")]
    [InlineData("duplicate-root")]
    [InlineData("duplicate-nested")]
    [InlineData("ambiguous-case")]
    [InlineData("unknown-approval")]
    [InlineData("unknown-evidence-field")]
    [InlineData("unknown-phase-field")]
    [InlineData("wrong-version")]
    [InlineData("null-evidence")]
    [InlineData("null-phase")]
    [InlineData("null-string")]
    [InlineData("empty-phase")]
    [InlineData("repository-mismatch")]
    [InlineData("relative-phase-escape")]
    [InlineData("manifest-escape")]
    [InlineData("oversized")]
    public void SourceVariantCommandRejectsBadManifestsBeforeExecutingNativeWork(string fault)
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var options = new SourceVariantEvidenceOptions(fixture.Path, "native-report.json", new string('a', 64), "src/A.cs", "bin/Subject.dll", "0", Guid.NewGuid().ToString(),
            new(Path.Combine(fixture.Path, "original"), "profile.json"), new(Path.Combine(fixture.Path, "variant"), "profile.json"),
            new(Path.Combine(fixture.Path, "restored"), "profile.json"));
        var value = JsonSerializer.SerializeToNode(new { schemaVersion = 1, evidence = options }, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        string? raw = null;
        string manifest = "source-variant.json";
        switch (fault)
        {
            case "invalid-json": raw = "{"; break;
            case "duplicate-root": raw = value.ToJsonString().Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal); break;
            case "duplicate-nested": raw = value.ToJsonString().Replace("\"mutantId\":\"0\"", "\"mutantId\":\"0\",\"mutantId\":\"1\"", StringComparison.Ordinal); break;
            case "ambiguous-case": raw = value.ToJsonString().Replace("\"nativeTestId\":", "\"NativeTestId\":", StringComparison.Ordinal); break;
            case "unknown-approval": value["qualified"] = true; break;
            case "unknown-evidence-field": value["evidence"]!["approved"] = true; break;
            case "unknown-phase-field": value["evidence"]!["variant"]!["completed"] = true; break;
            case "wrong-version": value["schemaVersion"] = 2; break;
            case "null-evidence": value["evidence"] = null; break;
            case "null-phase": value["evidence"]!["variant"] = null; break;
            case "null-string": value["evidence"]!["sourceFile"] = null; break;
            case "empty-phase": value["evidence"]!["variant"]!["profilePath"] = ""; break;
            case "repository-mismatch": value["evidence"]!["repositoryPath"] = Path.Combine(fixture.Path, "different"); break;
            case "relative-phase-escape": value["evidence"]!["variant"]!["repositoryPath"] = "../outside"; break;
            case "manifest-escape": manifest = "../outside-source-variant.json"; break;
            case "oversized": raw = new string(' ', 262145); break;
            default: throw new InvalidOperationException("Unknown manifest control.");
        }
        if (fault != "manifest-escape") { fixture.Write(manifest, raw ?? value.ToJsonString()); }

        var (exitCode, output) = InvokeSourceVariantCommand(fixture, fixture.Path, manifest, "json");

        Assert.Equal(2, exitCode);
        using var result = JsonDocument.Parse(output);
        Assert.Equal("invalid-source-variant-manifest", result.RootElement.GetProperty("status").GetString());
        Assert.False(result.RootElement.GetProperty("qualified").GetBoolean());
        Assert.Empty(fixture.Calls());
        Assert.False(Directory.Exists(fixture.Full(IncrementalTestService.LocalRoot)));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("human")]
    [InlineData("agent")]
    public void SourceVariantCommandReportsUnavailableEvidenceWithoutRunningOrCreatingState(string format)
    {
        using var fixture = new IncrementalTestServiceTests.Fixture();
        var options = new SourceVariantEvidenceOptions(fixture.Path, "missing-native-report.json", new string('a', 64), "src/A.cs", "bin/Subject.dll", "0", Guid.NewGuid().ToString(),
            new(fixture.Path, "profile.json"), new(Path.Combine(fixture.Path, "variant"), "profile.json"), new(Path.Combine(fixture.Path, "restored"), "profile.json"));
        fixture.Write("source-variant.json", JsonSerializer.Serialize(new { schemaVersion = 1, evidence = options }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var (exitCode, output) = InvokeSourceVariantCommand(fixture, fixture.Path, "source-variant.json", format);

        Assert.Equal(4, exitCode);
        Assert.Contains("source-variant-verification-failed", output, StringComparison.Ordinal);
        Assert.Empty(fixture.Calls());
        Assert.False(Directory.Exists(fixture.Full(IncrementalTestService.LocalRoot)));
    }

    internal static (int ExitCode, string Output) InvokeSourceVariantCommand(ICisGraphSnapshotReader graph, string repository, string manifest, string format)
    {
        var registry = new MutationCommandRegistry();
        new TestingModule().RegisterCommands(registry, new IncrementalCommandServices(Service(), graph));
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var previous = Console.Out;
        try
        {
            Console.SetOut(output);
            int exit = registry.Root.Parse(["test", "incremental", "verify-source-variant", "--repo", repository, "--manifest", manifest, "--format", format]).Invoke();
            return (exit, output.ToString());
        }
        finally { Console.SetOut(previous); }
    }
}
