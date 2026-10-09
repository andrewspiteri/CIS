using System.Text.Json;
using Example.TestSupport;

namespace Example.BusinessTests;

/// <summary>Retains fixture inputs and diagnostic summaries without replacing native xUnit/TRX results.</summary>
internal sealed class BusinessEvidence
{
    internal string ExampleDirectory { get; } = FindExample();
    internal string DirectoryPath { get; }

    internal BusinessEvidence(ITestOutputHelper output)
    {
        DirectoryPath = Path.Combine(TestEvidenceDirectory.Create(ExampleDirectory, "business"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        output.WriteLine($"Native business evidence: {DirectoryPath}");
    }

    internal Task<string> WriteAsync(string name, object value)
        => WriteDiagnosticAsync(name, value, TestContext.Current.CancellationToken);

    internal async Task<string> WriteDiagnosticAsync(string name, object value, CancellationToken cancellationToken)
    {
        var path = Path.Combine(DirectoryPath, name);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value), cancellationToken);
        return path;
    }

    private static string FindExample()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NativeHarness.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Native example solution was not found.");
    }
}
