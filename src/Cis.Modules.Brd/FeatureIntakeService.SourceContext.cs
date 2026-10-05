using System.Text;
using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    private static CisFeatureBrdSource BrdSource(string originalPath, string retainedPath, string hash,
        IReadOnlyList<string>? requirements = null) => new(Path.GetFileName(originalPath), originalPath,
            retainedPath, hash, requirements ?? []);

    private static IReadOnlyList<CisFeatureBrdSource> SourceBrds(CisFeatureIntakePlan plan)
        => plan.SourceBrds ?? [new(plan.BacklogItemId is null ? "Feature BRD" : "Requirement snapshot",
            "", plan.SourcePath, plan.SourceHash, [])];

    // Read only the exact retained versions. The product's currently selected BRD
    // must never silently replace the source of a previously introduced feature.
    private static string ReadSourceContext(CisWorkspaceRepository authority, CisFeatureIntakePlan plan,
        Dictionary<string, string> inputs)
    {
        var context = new List<string>();
        foreach (var source in SourceBrds(plan))
        {
            if (!Regex.IsMatch(source.Hash, "^sha256:[a-f0-9]{64}$")
                || !(source.Path == plan.SourcePath
                    || source.Path == $".cis/inputs/brds/{source.Hash[7..]}/source.md")
                || !CisPathSafety.TryResolveUnderRoot(authority.RepositoryPath, source.Path, out var path)
                || !SafeAbsolutePath(path) || !File.Exists(path) || new FileInfo(path).Length > 2_097_152)
                throw new InvalidDataException("A linked source BRD is missing or uses an unsafe path. Restore its retained version before planning.");
            var bytes = File.ReadAllBytes(path);
            if (Hash(bytes) != source.Hash) throw new InvalidDataException("A linked source BRD has changed. Restore its retained version before planning.");
            inputs[path] = source.Hash;
            var text = Encoding.UTF8.GetString(bytes);
            var excerpt = string.Join("\n\n", new[]
            {
                SourceExcerpt(text, "constraint|assumption|exclusion|out.of.scope"),
                SourceExcerpt(text, "non.functional|security|success"),
                SourceExcerpt(text, "purpose|overview|business objectives|stakeholder|actor")
            }.Where(part => part.Length > 0));
            if (excerpt.Length > 0) context.Add($"Source BRD: {source.Title}\nRetained version: {source.Hash}\n\n{excerpt}");
        }
        return string.Join("\n\n", context);
    }

    private static string FeatureDescription(string source)
    {
        // Feature-specific requirements and acceptance take precedence over a
        // generic scope note or the shared product overview.
        var requirements = SourceExcerpt(source, "functional requirements|feature description|user stories");
        var business = SourceExcerpt(source, "purpose|overview|scope|business objectives");
        var acceptance = SourceExcerpt(source, "acceptance|success criteria");
        var description = string.Join("\n\n", new[] { requirements, business, acceptance }.Where(text => text.Length > 0));
        if (description.Length > 0) return BoundExcerpt(description, 16000);
        var visible = Regex.Replace(source, @"<!--.*?-->", "", RegexOptions.Singleline, TimeSpan.FromSeconds(1)).Trim();
        visible = Regex.Replace(visible, @"\A\s*---\r?\n.*?\r?\n---\s*", "", RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        return BoundExcerpt(visible, 6000);
    }
}
