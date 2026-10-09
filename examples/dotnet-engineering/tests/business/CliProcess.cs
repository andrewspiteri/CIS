using System.Diagnostics;

namespace Example.BusinessTests;

/// <summary>Configures the real CLI with isolated credentials and the native process-capture helper.</summary>
internal static class CliProcess
{
    internal static Task<CliProcessResult> RunAsync(string directory, IReadOnlyList<string> arguments, string? connectionString = null, BusinessEvidence? evidence = null)
    {
        // Execute the project-reference copy so the native collector can instrument child-process behavior.
        var executable = Path.Combine(AppContext.BaseDirectory, "Example.Cli.dll");
        var startInfo = new ProcessStartInfo("dotnet")
        { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { executable }.Concat(arguments)) startInfo.ArgumentList.Add(argument);
        startInfo.Environment.Remove("EXAMPLE_DATABASE_CONNECTION");
        if (connectionString is not null) startInfo.Environment["EXAMPLE_DATABASE_CONNECTION"] = connectionString;
        return ProcessCapture.RunAsync(startInfo, TimeSpan.FromSeconds(20), evidence, connectionString, TestContext.Current.CancellationToken);
    }
}

internal sealed record CliProcessResult(int ExitCode, string StandardOutput, string StandardError);
