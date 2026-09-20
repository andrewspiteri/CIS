using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Cis.Abstractions;

namespace Cis.Host;

internal sealed class CisCommandDispatcher(RootCommand root) : ICisCommandDispatcher
{
    [SuppressMessage("Design", "CA1031", Justification = "Isolate projection failures using the same sanitized process-boundary diagnostics as CisApplication.")]
    public CisCommandCapture Capture(string[] arguments)
    {
        lock (CisApplication.ConsoleSync)
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            var exitCode = 1;
            try
            {
                Console.SetOut(output);
                Console.SetError(error);
                exitCode = root.Parse(arguments).Invoke(new InvocationConfiguration
                {
                    EnableDefaultExceptionHandler = false,
                    Output = output,
                    Error = error,
                });
            }
            catch (OperationCanceledException)
            {
                exitCode = 130;
                error.WriteLine("CIS command was cancelled.");
            }
            catch (Exception exception)
            {
                error.WriteLine($"CIS-HOST-UNHANDLED: {exception.GetType().Name}. Run `cis repo doctor` and retain the tool-usage record.");
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
            return new(exitCode, output.ToString(), error.ToString());
        }
    }
}
