using System.Text.Json;

namespace Example.TestSupport;

/// <summary>Flushes bounded diagnostic events so a lost test process leaves inspectable partial evidence.</summary>
internal sealed class TestSignalJournal : IDisposable
{
    private readonly object _sync = new();
    private readonly StreamWriter _writer;
    private int _events;
    private bool _closed;
    internal string Path { get; }

    internal TestSignalJournal(string source, bool enabled)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "NativeHarness.slnx")))
            directory = directory.Parent;
        var root = directory?.FullName ?? throw new InvalidOperationException("Native example solution was not found.");
        Path = System.IO.Path.Combine(TestEvidenceDirectory.Create(root, "diagnostics"), source + ".jsonl");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        _writer = new StreamWriter(new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        _writer.WriteLine(JsonSerializer.Serialize(new { kind = "collector-open", source, collectorsEnabled = enabled }));
    }

    internal void Append(string kind, object value)
    {
        lock (_sync)
        {
            if (_closed) return;
            if (++_events <= 768) _writer.WriteLine(JsonSerializer.Serialize(new { kind, value }));
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_closed) return;
            _closed = true;
            // Collector closure is not a passing test verdict. Native xUnit/TRX owns that verdict.
            _writer.WriteLine(JsonSerializer.Serialize(new { kind = "collector-closed", truncated = _events > 768 }));
            _writer.Dispose();
        }
    }
}
