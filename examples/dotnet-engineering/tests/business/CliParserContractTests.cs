namespace Example.BusinessTests;

public sealed class CliParserContractTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("root")]
    [InlineData("apply")]
    public async Task HelpRemainsDiscoverableWithoutAConnection(string scope)
    {
        var evidence = new BusinessEvidence(output);
        string[] arguments = scope == "root" ? ["--help"] : ["apply", "--help"];
        var result = await CliProcess.RunAsync(evidence.ExampleDirectory, arguments, evidence: evidence);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        Assert.Contains("apply", result.StandardOutput, StringComparison.Ordinal);
        if (scope == "apply")
        {
            Assert.Contains("--input", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("1 MiB", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("10,000", result.StandardOutput, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("unknown-command")]
    [InlineData("missing-option")]
    [InlineData("missing-value")]
    [InlineData("unknown-option")]
    public async Task InvalidUsageKeepsTheNativeParserFailureContract(string scenario)
    {
        var evidence = new BusinessEvidence(output);
        string[] arguments = scenario switch
        {
            "unknown-command" => ["not-a-command"],
            "missing-option" => ["apply"],
            "missing-value" => ["apply", "--input"],
            "unknown-option" => ["apply", "--unknown"],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        var result = await CliProcess.RunAsync(evidence.ExampleDirectory, arguments, evidence: evidence);
        Assert.Equal(1, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardError));
        Assert.DoesNotContain("schemaVersion", result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Reading application failed.", result.StandardError, StringComparison.Ordinal);
        // Native parser diagnostics may echo usage tokens; exact localized prose is not the wire contract.
    }
}
