using System.Text.Json;
using System.Xml.Linq;
using Cis.Abstractions;

namespace Cis.Modules.Repository;

/// <summary>Recognizes literal MTP runner selection without evaluating repository build code.</summary>
internal static class DotNetRunnerSettings
{
    internal static bool Read(string repository, string directory, IEnumerable<XDocument> inputs, ICollection<string> warnings)
    {
        var selected = inputs.SelectMany(input => input.Descendants()).Any(element =>
            element.Name.LocalName is "TestingPlatformDotnetTestSupport" or "UseMicrosoftTestingPlatformRunner" or "EnableNUnitRunner" or "EnableMSTestRunner"
            && (element.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
                || element.AncestorsAndSelf().Any(parent => parent.Attribute("Condition") is not null)));
        for (var current = directory; current is not null && CisPathSafety.IsUnderRoot(repository, current, allowRoot: true); current = Path.GetDirectoryName(current))
        {
            var path = Path.Combine(current, "global.json");
            if (!File.Exists(path)) continue;
            try
            {
                if (CisPathSafety.ContainsReparsePoint(repository, path) || new FileInfo(path).Length > 65536)
                    throw new InvalidDataException("Runner configuration must be a bounded regular file.");
                using var json = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                if (json.RootElement.TryGetProperty("test", out var test) && test.TryGetProperty("runner", out var runner))
                    selected |= runner.ValueKind != JsonValueKind.String || runner.GetString() != "VSTest";
            }
            catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or InvalidDataException)
            { warnings.Add($"Unable to establish test runner from {Path.GetRelativePath(repository, path)}: {error.Message}"); selected = true; }
            break;
        }
        return selected;
    }
}
