using System.Text.Json;

namespace Example.BusinessTests;

/// <summary>Runs the real application adapter against the fixture database and reads its public result.</summary>
internal static class CliReplay
{
    internal static async Task<ReplayCliResult> RunAsync(string directory, string inputPath, string connectionString, BusinessEvidence? evidence = null)
    {
        var result = await CliProcess.RunAsync(directory, ["apply", "--input", inputPath], connectionString, evidence);
        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrEmpty(result.StandardError), "Successful execution must not emit diagnostics.");
        return JsonSerializer.Deserialize<ReplayCliResult>(result.StandardOutput, JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("The native CLI returned no result.");
    }

    internal static async Task ExpectRejectedAsync(string directory, string inputPath, string connectionString, BusinessEvidence? evidence = null)
    {
        var result = await CliProcess.RunAsync(directory, ["apply", "--input", inputPath], connectionString, evidence);
        Assert.Equal(2, result.ExitCode);
        Assert.True(string.IsNullOrEmpty(result.StandardOutput), "Rejected input must not emit a success result.");
        Assert.True(result.StandardError.Trim() == "Reading application failed. Verify input, database availability and identity conflicts; committed readings can be replayed.",
            "Errors must remain bounded and reveal no input or connection details.");
    }

}

internal sealed record ReplayCliResult(int SchemaVersion, string Mode, int Accepted, int Stored, decimal Quantity);
