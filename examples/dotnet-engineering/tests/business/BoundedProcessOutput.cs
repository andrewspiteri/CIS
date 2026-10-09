using System.Text;

namespace Example.BusinessTests;

/// <summary>Keeps an inspectable bounded prefix even when reading a process pipe fails.</summary>
internal sealed class BoundedProcessOutput
{
    internal const int Limit = 16_384;
    private readonly StringBuilder _text = new();
    private readonly object _sync = new();
    internal string Snapshot { get { lock (_sync) return _text.ToString(); } }

    internal async Task ReadAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[1024];
        int count;
        while ((count = await reader.ReadAsync(buffer, cancellationToken)) != 0)
        {
            lock (_sync)
            {
                var retained = Math.Min(count, Limit - _text.Length);
                _text.Append(buffer, 0, retained);
                if (retained != count) throw new InvalidDataException("CLI output exceeded the diagnostic capture limit.");
            }
        }
    }
}
