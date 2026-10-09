using Example.Cli;
using System.Text.Json;

namespace Example.UnitTests;

public sealed class JsonPrecisionTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");

    [Theory(DisplayName = "TC-FEAT-FR-001-001-001 LossyJsonValuesAreRejected")]
    [InlineData("observedAt", "\"1970-01-01T00:00:00.00000001Z\"")]
    [InlineData("observedAt", "\"1970-01-01T00:00:00.123456001Z\"")]
    [InlineData("observedAt", "\"2026-01-15T12:30:00.00000001\"")]
    [InlineData("observedAt", "\"2026-01-15T12:30:00.123456001\"")]
    [InlineData("quantity", "99999999999999999999.999999991")]
    [InlineData("quantity", "0.00000000000000000000000000001")]
    [InlineData("quantity", "\"99999999999999999999.999999991\"")]
    [InlineData("quantity", "\"0.00000000000000000000000000001\"")]
    [InlineData("quantity", "1e-29")]
    [InlineData("quantity", "\"1e-29\"")]
    public async Task LossyJsonValuesAreRejected(string field, string value)
    {
        var json = "[{\"id\":\"00000000-0000-0000-0000-000000000001\",\"observedAt\":\"1970-01-01T00:00:00Z\",\"quantity\":1}]";
        json = field == "observedAt" ? json.Replace("\"1970-01-01T00:00:00Z\"", value, StringComparison.Ordinal)
            : json.Replace("\"quantity\":1", "\"quantity\":" + value, StringComparison.Ordinal);
        await File.WriteAllTextAsync(_file, json, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<JsonException>(() => ReadingInput.ReadAsync(new FileInfo(_file), TestContext.Current.CancellationToken));
    }

    public void Dispose() => File.Delete(_file);
}
