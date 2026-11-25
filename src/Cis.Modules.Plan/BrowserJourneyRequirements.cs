using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Plan;

/// <summary>Reads required journey identities from existing catalogue and task contracts, never from test discovery.</summary>
internal static partial class BrowserJourneyRequirements
{
    internal sealed record Journey(string Id, string RequirementId, string FrontendType);

    internal static IReadOnlyList<Journey> ReadCatalogue(string repository, string path)
    {
        if (!CisPathSafety.IsUnderRoot(repository, path) || CisPathSafety.ContainsReparsePoint(repository, path)
            || !File.Exists(path) || new FileInfo(path).Length > 2 * 1024 * 1024)
            throw new InvalidDataException("Required browser journeys need an existing bounded test catalogue inside the authority repository.");
        var markdown = File.ReadAllText(path);
        var matches = Heading().Matches(markdown).Cast<Match>().ToArray();
        var result = new List<Journey>();
        foreach (var (match, index) in matches.Select((match, index) => (match, index)))
        {
            var section = markdown[match.Index..(index + 1 < matches.Length ? matches[index + 1].Index : markdown.Length)];
            var requirements = Regex.Matches(section, @"(?im)^- Requirement:\s*`(?<id>[^`\r\n]+)`\s*$");
            var frontends = Regex.Matches(section, @"(?im)^- Frontend type:\s*`(?<type>[^`\r\n]+)`\s*$");
            if (requirements.Count != 1 || frontends.Count != 1
                || frontends[0].Groups["type"].Value is not ("public" or "customer" or "backoffice" or "not-applicable"))
                throw new InvalidDataException($"Test case {match.Groups["id"].Value} has no unambiguous requirement/frontend binding; refresh its catalogue.");
            result.Add(new(match.Groups["id"].Value.ToUpperInvariant(), requirements[0].Groups["id"].Value, frontends[0].Groups["type"].Value));
        }
        if (result.GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1))
            throw new InvalidDataException("Required browser journey identities are duplicated in the test catalogue.");
        return result;
    }

    internal static IReadOnlyList<string> ForTask(string repository, string cataloguePath, PlanWorkItem task)
    {
        var explicitIds = (task.VerificationObligations ?? [])
            .Where(obligation => obligation is { EvidenceRoute: "native" }
                && obligation.SharedGateIds?.Contains("browser", StringComparer.Ordinal) == true)
            .SelectMany(obligation => (obligation.BasisReferences ?? []).Concat(obligation.EvidenceExpectations ?? []))
            .SelectMany(Ids).ToArray();
        if (!File.Exists(cataloguePath)) return explicitIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var catalogue = ReadCatalogue(repository, cataloguePath);
        foreach (var requirement in task.RequirementIds)
            if (!catalogue.Any(item => item.RequirementId.Equals(requirement, StringComparison.Ordinal)))
                throw new InvalidDataException($"Required browser journey mapping is missing for task requirement {requirement}; refresh its authority catalogue.");
        return catalogue.Where(item => task.RequirementIds.Contains(item.RequirementId, StringComparer.Ordinal)
                && item.FrontendType is "public" or "customer" or "backoffice"
                && (task.FrontendType is null || item.FrontendType == task.FrontendType))
            .Select(item => item.Id).Concat(explicitIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static IEnumerable<string> Ids(string text)
        => CaseId().Matches(text).Select(match => match.Value.ToUpperInvariant());

    internal static string? SourcePath(string repository, string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        try
        {
            var path = Path.IsPathRooted(source) ? Path.GetFullPath(source)
                : Path.GetFullPath(Path.Combine(repository, source.Replace('\\', Path.DirectorySeparatorChar)));
            if (!CisPathSafety.IsUnderRoot(repository, path) || CisPathSafety.ContainsReparsePoint(repository, path) || !File.Exists(path)) return null;
            return Path.GetRelativePath(repository, path).Replace('\\', '/');
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    [GeneratedRegex(@"(?im)^###\s+(?<id>TC-[A-Z0-9]+(?:-[A-Z0-9]+)*):[^\r\n]*\r?$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"(?<![A-Z0-9_-])TC-[A-Z0-9]+(?:-[A-Z0-9]+)*(?![A-Z0-9_-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CaseId();
}
