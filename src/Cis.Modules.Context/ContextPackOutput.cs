using System.Text;
using System.Text.Json;

namespace Cis.Modules.Context;

internal static class ContextPackOutput
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static string Render(ContextPackResult result, string format)
    {
        if (format == "json")
        {
            return JsonSerializer.Serialize(result, Json);
        }
        var output = new StringBuilder();
        if (format == "agent")
        {
            output.AppendLine($"status={Safe(result.Status)};exitCode={result.ExitCode};freshness={Safe(result.Freshness)};applied={result.Applied.ToString().ToLowerInvariant()};sources={result.Sources.Count};omissions={result.Omissions.Count};metadataOmitted={result.OmittedMetadataItems};contentTruncated={result.ContentTruncated.ToString().ToLowerInvariant()}");
            output.AppendLine($"output={Safe(result.OutputPath)};detail={Safe(result.DetailPath)};build={Safe(result.Build?.Id)}");
            output.AppendLine($"deliveredCharacters={result.DeliveredCharacters};deliveredUtf8Bytes={result.DeliveredUtf8Bytes};omittedErrors={result.OmittedErrors.ToString().ToLowerInvariant()}");
            foreach (var source in result.Sources)
            {
                output.AppendLine($"source={Safe(source.Path)};class={Safe(source.FactClass)};included={source.IncludedCharacters};reason={Safe(string.Join(',', source.Reasons))}");
            }
            foreach (var omission in result.Omissions)
            {
                output.AppendLine($"omission={Safe(omission.Path)};reason={Safe(omission.Reason)}");
            }
            foreach (var diagnostic in result.Diagnostics)
            {
                output.AppendLine($"diagnostic={Safe(diagnostic.Code)};severity={Safe(diagnostic.Severity)};message={Safe(diagnostic.Message)}");
            }
        }
        else
        {
            output.AppendLine($"Context pack: {result.Status}; freshness: {result.Freshness}.");
            output.AppendLine($"Sources: {result.Sources.Count}; omitted metadata: {result.OmittedMetadataItems}; truncated: {result.ContentTruncated}.");
            output.AppendLine($"Output: {result.OutputPath ?? "unavailable"}; detail: {result.DetailPath ?? "unavailable"}.");
            output.AppendLine($"Complete delivery: {result.DeliveredCharacters} characters; {result.DeliveredUtf8Bytes} UTF-8 bytes; omitted errors: {result.OmittedErrors}.");
            foreach (var diagnostic in result.Diagnostics)
            {
                output.AppendLine($"[{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}");
            }
        }
        return output.ToString().TrimEnd('\r', '\n');
    }

    private static string Safe(string? value) => (value ?? "unknown").Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ');
}
