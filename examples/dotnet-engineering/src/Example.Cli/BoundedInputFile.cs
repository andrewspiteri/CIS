using System.Text;

namespace Example.Cli;

/// <summary>Bounds bytes actually read, including when cached file metadata is out of date.</summary>
internal static class BoundedInputFile
{
    private const int MaximumBytes = 1_048_576;

    internal static async Task<string> ReadAsync(FileInfo file, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var input = file.OpenRead();
        var bytes = new byte[MaximumBytes + 1];
        var count = await input.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken);
        if (count > MaximumBytes)
            throw new ArgumentException("Input must be an existing JSON file of at most 1 MiB.");

        // StreamReader preserves the previous text reader's BOM detection and decoding behavior.
        using var reader = new StreamReader(new MemoryStream(bytes, 0, count, writable: false), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
