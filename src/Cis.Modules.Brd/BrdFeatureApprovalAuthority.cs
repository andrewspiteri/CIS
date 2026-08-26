using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed class BrdFeatureApprovalAuthority(BrdBacklogService backlog) : ICisFeatureApprovalAuthority
{
    public CisFeatureApproval Evaluate(string repositoryPath, string featureSpecificationPath)
    {
        var repository = Path.GetFullPath(repositoryPath);
        var absolute = Path.GetFullPath(Path.Combine(repository,
            featureSpecificationPath.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!absolute.StartsWith(repository.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
            return new(false, false, null, null, null, null, null,
                [$"Feature specification path '{featureSpecificationPath}' escapes repository '{repository}'."]);
        if (!File.Exists(absolute))
            return new(false, false, null, null, null, null, null,
                [$"Feature specification '{absolute}' does not exist."]);

        var content = File.ReadAllText(absolute);
        var itemId = Value(content, "high_level_item");
        if (string.IsNullOrWhiteSpace(itemId) || !Regex.IsMatch(itemId, "^HLT-[A-Z0-9-]+$", RegexOptions.CultureInvariant))
            return new(false, false, null, null, null, null, null,
                [$"Feature specification '{absolute}' has no valid high_level_item metadata."]);

        var status = backlog.FeatureStatus(repositoryPath, itemId);
        var relative = Path.GetRelativePath(repository, absolute).Replace('\\', '/');
        var errors = new List<string>(status.Errors);
        if (status.RelativePath is null || !status.RelativePath.Equals(relative, comparison))
            errors.Add($"Approved backlog item {itemId} resolves to '{status.RelativePath ?? "no feature"}', not '{relative}'.");
        if (status.Validation is not { Valid: true, Current: true, EffectiveStatus: "Active" })
            errors.Add($"Feature specification {itemId} must be Active, current, and valid before authority can carry forward.");

        var reviewer = Value(content, "approved_by");
        var rationale = Value(content, "approval_reason");
        var hash = Value(content, "approved_content_hash");
        if (string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(rationale)
            || string.IsNullOrWhiteSpace(hash) || !hash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            errors.Add($"Feature specification {itemId} is missing approval reviewer, rationale, or content digest.");

        return new(true, errors.Count == 0, itemId, relative, reviewer, rationale, hash, errors);
    }

    private static string? Value(string content, string key)
    {
        var match = Regex.Match(content, $@"(?m)^\s*{Regex.Escape(key)}:\s*(?<value>[^\r\n]+)\r?$",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) return null;
        return match.Groups["value"].Value.Trim().Trim('"', '\'');
    }
}
