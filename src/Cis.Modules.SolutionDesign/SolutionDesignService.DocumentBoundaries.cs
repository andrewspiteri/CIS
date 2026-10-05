using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Cis.Modules.SolutionDesign;

public sealed partial class SolutionDesignService
{
    private static readonly IReadOnlyDictionary<string, string[]> SourceSectionAliases = new Dictionary<string, string[]>
    {
        ["Design goals and principles"] = ["Purpose and decision status", "Design principles"],
        ["Runtime architecture and trust boundaries"] = ["Runtime architecture", "System architecture"],
        ["Application model and invariants"] = ["Invariants carried into the implementation", "Business invariants"],
        ["Integration point catalog"] = ["Service contract", "APIs and contracts"],
        ["Security and privacy intent"] = ["Trusted-local API and configuration", "Trust and security"],
        ["Operations, migration, and recovery intent"] = ["Shutdown, upgrades and recovery", "Observability and diagnosis"],
        ["Quality attributes and verification direction"] = ["Technical acceptance and BRD traceability", "Acceptance and verification"],
    };

    // Imported direction can define boundaries without CIS IDs or implementation dictionaries.
    // Only explicit ownership tables in architecture sections qualify; prose is never guessed.
    private static IReadOnlyList<SolutionComponent> ReadDocumentedBoundaries(string content)
    {
        content = Regex.Replace(content, @"<!--.*?-->", "", RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        content = Regex.Replace(content, @"(?ms)^\s*(`{3,}|~{3,})[^\r\n]*\r?\n.*?^\s*\1\s*$", "", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var result = new List<SolutionComponent>();
        foreach (var heading in new[] { "Product module architecture", "Runtime architecture", "System architecture", "Runtime architecture and trust boundaries", "Component ownership" })
        {
            var name = -1; var owns = -1; var excludes = -1; var responsibility = -1; var kind = "";
            foreach (var line in ExtractSection(content, heading).Split('\n'))
            {
                if (!line.TrimStart().StartsWith('|')) { name = -1; continue; }
                var cells = Regex.Split(line.Trim().Trim('|'), @"(?<!\\)\|").Select(cell => cell.Trim().Replace("\\|", "|", StringComparison.Ordinal)).ToArray();
                if (name < 0)
                {
                    name = Array.FindIndex(cells, cell => cell is "Process" or "Component" or "Module");
                    owns = Array.FindIndex(cells, cell => cell is "Owns" or "Ownership");
                    excludes = Array.FindIndex(cells, cell => cell is "Excluded work" or "Excludes" or "Must not own");
                    responsibility = Array.FindIndex(cells, cell => cell is "Responsibility" or "Responsibilities");
                    if (name < 0 || owns < 0 || excludes < 0) { name = -1; continue; }
                    kind = cells[name] == "Process" ? "Runtime process (technical intent)" : "Logical component (technical intent)";
                    continue;
                }
                if (cells.Length <= new[] { name, owns, excludes, responsibility }.Max()
                    || cells.All(cell => cell.All(c => c is '-' or ':' or ' '))) continue;
                var label = cells[name].Replace("`", "", StringComparison.Ordinal);
                if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(cells[owns]) || string.IsNullOrWhiteSpace(cells[excludes])) continue;
                var id = "TI-MOD-DOC-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(label)))[..12];
                if (result.Any(component => component.Id == id)) continue;
                result.Add(new(id, label, kind, responsibility >= 0 ? cells[responsibility] : cells[owns], cells[owns], cells[excludes], []));
            }
        }
        return result;
    }
}
