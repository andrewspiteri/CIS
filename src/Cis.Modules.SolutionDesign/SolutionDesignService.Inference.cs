using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.SolutionDesign;

public sealed partial class SolutionDesignService
{
    internal const string InferredMarker = "<!-- cis:solution-design-implementation-authored -->";
    private static readonly string[] DesignHeadings = ["Architecture drivers", "System context and boundaries", "Logical component topology",
        "Data ownership and consistency", "Integration architecture", "Security and trust boundaries", "Deployment, operations, and recovery",
        "Quality and verification architecture", "UI design handoff", "Traceability", "Design decisions and accepted exceptions"];
    private static readonly string[] ComponentHeadings = ["Component catalogue", "Component responsibility profiles", "Component interaction catalogue",
        "Ownership rules", "Component-specific notes and accepted exceptions"];

    public CisSolutionDesignDraftResult PrepareExistingDraft(string workspacePath)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return new(state.Errors);
        foreach (var path in new[] { state.DesignPath!, state.ComponentSheetPath! })
            if (CisPathSafety.ContainsReparsePoint(state.Authority!.RepositoryPath, path)
                || File.Exists(path) && ReadFrontMatter(File.ReadAllText(path), "status") is not ("Draft" or "Review Required"))
                return new(["Architecture inference may update only safe Draft or Review Required solution-design paths."]);
        var result = InitializeCore(workspacePath, true);
        return new(result.Errors, result.Applied);
    }

    public CisSolutionDesignDraftResult ApplyExistingDraft(string workspacePath, string originalDesign,
        string originalComponents, string proposedDesign, string proposedComponents)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return new(state.Errors);
        var errors = new List<string>();
        var pairs = new[] { (Path: state.DesignPath!, Original: originalDesign, Proposed: proposedDesign, Headings: DesignHeadings,
            Start: ManagedStart, End: ManagedEnd), (Path: state.ComponentSheetPath!, Original: originalComponents, Proposed: proposedComponents,
            Headings: ComponentHeadings, Start: ComponentsStart, End: ComponentsEnd) };
        foreach (var pair in pairs)
        {
            if (CisPathSafety.ContainsReparsePoint(state.Authority!.RepositoryPath, pair.Path)
                || !File.Exists(pair.Path) || File.ReadAllText(pair.Path) != pair.Original)
                errors.Add("Canonical architecture changed during inference; the isolated bundle was not applied.");
            if (ReadFrontMatter(pair.Original, "status") is not ("Draft" or "Review Required")
                || FrontMatterBlock(pair.Original) != FrontMatterBlock(pair.Proposed))
                errors.Add("Architecture inference cannot change lifecycle, source baseline or approval metadata.");
            foreach (var heading in pair.Headings)
                if (string.IsNullOrWhiteSpace(ExtractSection(pair.Proposed, heading))) errors.Add("Missing architecture section: " + heading);
            if (!pair.Proposed.Contains(pair.Start, StringComparison.Ordinal) || !pair.Proposed.Contains(pair.End, StringComparison.Ordinal))
                errors.Add("Architecture managed markers must be retained.");
            if (!PreservesHumanSections(pair.Original, pair.Proposed, pair.Start, pair.End))
                errors.Add("Preserve human content outside the managed architecture block; add review questions without replacing existing notes.");
            if (Placeholder(pair.Proposed)) errors.Add("Replace architecture placeholders with evidence or explicit unresolved observations.");
            if (CisBrdPresentation.HasVisibleLinksOrSourceIds(pair.Proposed)) errors.Add("Keep architecture source links inside HTML comments.");
        }
        var components = ParseComponents(proposedComponents);
        if (!components.Select(item => item.Id).Order(StringComparer.Ordinal).SequenceEqual(state.Components.Select(item => item.Id).Order(StringComparer.Ordinal)))
            errors.Add("Preserve the technical intent's component identities; propose ownership changes for upstream review.");
        if (components.Any(item => !proposedDesign.Contains($"`{item.Id}`", StringComparison.Ordinal)))
            errors.Add("The overall architecture must explain every component identity in the component sheet.");
        try { ArchitectureDiagramModel.ReadRequired(proposedDesign); }
        catch (InvalidDataException exception) { errors.Add(exception.Message); }
        if (errors.Count > 0) return new(errors.Distinct(StringComparer.Ordinal).ToArray());
        var nextDesign = Stamp(proposedDesign); var nextComponents = Stamp(proposedComponents);
        try
        {
            Write(state.DesignPath!, nextDesign);
            Write(state.ComponentSheetPath!, nextComponents);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Restore the pair on a bounded write failure; never report a partial bundle as applied.
            Write(state.DesignPath!, originalDesign); Write(state.ComponentSheetPath!, originalComponents);
            return new(["Architecture bundle was restored after a write failure: " + exception.Message]);
        }
        return new([], true);
    }

    private static string Stamp(string content) => content.Contains(InferredMarker, StringComparison.Ordinal)
        ? content : content.TrimEnd() + "\n\n" + InferredMarker + "\n";
    private static string FrontMatterBlock(string content) => Regex.Match(content, @"\A---\r?\n.*?\r?\n---(?:\r?\n|\z)",
        RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Value;

    private static bool PreservesHumanSections(string original, string proposed, string start, string end)
    {
        static string Clean(string text) => Regex.Replace(text.Replace(InferredMarker, "", StringComparison.Ordinal),
            @"<!-- (?:cis:architecture-views|cis-implementation-coverage)\r?\n.*?\r?\n-->", "",
            RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var originalStart = original.IndexOf(start, StringComparison.Ordinal); var originalEnd = original.IndexOf(end, StringComparison.Ordinal);
        var proposedStart = proposed.IndexOf(start, StringComparison.Ordinal); var proposedEnd = proposed.IndexOf(end, StringComparison.Ordinal);
        if (originalStart < 0 || proposedStart < 0 || originalEnd < originalStart || proposedEnd < proposedStart
            || proposed.IndexOf(start, proposedStart + start.Length, StringComparison.Ordinal) >= 0
            || proposed.IndexOf(end, proposedEnd + end.Length, StringComparison.Ordinal) >= 0) return false;
        return Clean(original[..originalStart]) == Clean(proposed[..proposedStart])
            && Clean(proposed[(proposedEnd + end.Length)..]).Contains(Clean(original[(originalEnd + end.Length)..]), StringComparison.Ordinal);
    }
}
