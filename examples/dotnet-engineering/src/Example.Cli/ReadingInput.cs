using System.Text.Json;
using Example.Core;

namespace Example.Cli;

/// <summary>Validates the CLI's bounded file contract before a command changes persistent state.</summary>
internal static class ReadingInput
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        Converters = { new LocalEventTimeConverter(TimeZoneInfo.Local) },
    };
    internal static async Task<IReadOnlyList<Reading>> ReadAsync(FileInfo file, CancellationToken cancellationToken)
    {
        var content = await BoundedInputFile.ReadAsync(file, cancellationToken);
        using var document = JsonDocument.Parse(content);
        var readings = document.Deserialize<Reading[]>(JsonOptions)
            ?? throw new ArgumentException("Input must be a reading array.");
        if (readings.Length > 10_000) throw new ArgumentException("Input exceeds 10,000 records.");
        if (readings.Any(reading => reading is null)) throw new ArgumentException("Reading entries cannot be null.");
        foreach (var element in document.RootElement.EnumerateArray()) ReadingJsonPrecision.Validate(element);
        foreach (var reading in readings) reading.Validate();
        return readings;
    }
}
