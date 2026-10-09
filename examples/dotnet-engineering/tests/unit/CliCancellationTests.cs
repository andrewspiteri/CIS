using System.CommandLine;
using System.Globalization;
using Example.Cli;

namespace Example.UnitTests;

public sealed class CliCancellationTests
{
    [Fact]
    public async Task NativeCommandCancellationReturnsTheDocumentedResult()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var stdout = new StringWriter(CultureInfo.InvariantCulture);
        using var stderr = new StringWriter(CultureInfo.InvariantCulture);
        var configuration = new InvocationConfiguration
        {
            Output = stdout,
            Error = stderr,
            EnableDefaultExceptionHandler = false,
        };

        // No input or database is needed: cancellation must precede opening either boundary.
        var command = ReadingCommand.Create().Parse(["apply", "--input", "never-open-this-input.json"]);
        var exitCode = await command.InvokeAsync(configuration, cancellation.Token);

        Assert.Equal(130, exitCode);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Equal("Cancelled; committed readings remain safe to replay." + Environment.NewLine, stderr.ToString());
    }
}
