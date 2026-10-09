using System.Text;
using Example.Cli;

namespace Example.UnitTests;

public sealed class ReadingInputTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "foundation-input-" + Guid.NewGuid().ToString("N"));
    private const string ReadingJson = "{\"id\":\"00000000-0000-0000-0000-000000000001\",\"observedAt\":\"1970-01-01T00:00:00Z\",\"quantity\":1}";

    public ReadingInputTests() => Directory.CreateDirectory(_directory);

    [Theory(DisplayName = "TC-FEAT-FR-001-001-001 BomEncodedInputPreservesSuppliedValues")]
    [InlineData("utf8")]
    [InlineData("utf16")]
    [InlineData("utf16be")]
    [InlineData("utf32")]
    public async Task BomEncodedInputPreservesSuppliedValues(string encodingName)
    {
        var encoding = encodingName switch
        {
            "utf8" => new UTF8Encoding(true),
            "utf16" => Encoding.Unicode,
            "utf16be" => Encoding.BigEndianUnicode,
            "utf32" => Encoding.UTF32,
            _ => throw new ArgumentOutOfRangeException(nameof(encodingName)),
        };
        var file = await WriteAsync("[" + ReadingJson + "]", encoding);
        var reading = Assert.Single(await ReadingInput.ReadAsync(file, TestContext.Current.CancellationToken));
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000001"), reading.Id);
        Assert.Equal(DateTimeOffset.UnixEpoch, reading.ObservedAt);
        Assert.Equal(1m, reading.Quantity);
    }

    [Fact]
    public async Task ExactByteLimitIsAccepted()
    {
        var file = await WriteAsync("[]".PadRight(1_048_576));
        Assert.Empty(await ReadingInput.ReadAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GrowthAfterMetadataInspectionCannotBypassByteLimit()
    {
        var file = await WriteAsync("[]");
        Assert.Equal(2, file.Length); // Populate the same stale FileInfo cache used by the original implementation.
        await File.WriteAllTextAsync(file.FullName, "[]".PadRight(1_048_577), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ArgumentException>(() => ReadingInput.ReadAsync(file, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    public async Task RecordLimitIsEnforcedIndependentlyOfByteLimit(int count, bool accepted)
    {
        var file = await WriteAsync("[" + string.Join(',', Enumerable.Repeat(ReadingJson, count)) + "]");
        Assert.True(file.Length < 1_048_576);
        if (accepted)
            Assert.Equal(count, (await ReadingInput.ReadAsync(file, TestContext.Current.CancellationToken)).Count);
        else
            await Assert.ThrowsAsync<ArgumentException>(() => ReadingInput.ReadAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancellationIsPreserved()
    {
        var file = await WriteAsync("[]");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadingInput.ReadAsync(file, cancellation.Token));
    }

    private async Task<FileInfo> WriteAsync(string text, Encoding? encoding = null)
    {
        var path = Path.Combine(_directory, "input.json");
        await File.WriteAllTextAsync(path, text, encoding ?? new UTF8Encoding(false), TestContext.Current.CancellationToken);
        return new FileInfo(path);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
