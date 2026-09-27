using Cis.Abstractions;

namespace Cis.Modules.Repository;

public sealed partial class RepositoryInitializer
{
    private static void PlanMinimalEntryPoints(RepositoryInitRequest request, string root,
        IReadOnlyDictionary<string, ManagedStarterArtifact> previous,
        IDictionary<string, ManagedStarterArtifact> next,
        ICollection<PlannedFile> files, ICollection<string> retained, ICollection<string> collisions,
        ICollection<RepositoryFileMerge> merges)
    {
        foreach (var path in new[] { "AGENTS.md", RepositoryAgentsMerge.CopilotPath })
        {
            var absolute = Path.Combine(root, path);
            if (CisPathSafety.ContainsReparsePoint(root, absolute) || Directory.Exists(absolute))
            { collisions.Add($"Entry point cannot be updated through a symbolic path or directory: {path}"); continue; }
            if (path != "AGENTS.md" && !File.Exists(absolute)) continue;
            var current = File.Exists(absolute) ? File.ReadAllText(absolute) : "";
            var templateHash = RepositoryAgentsMerge.TemplateHash(RepositoryImportAssessmentBuilder.EntryPoint, []);
            if (File.Exists(absolute) && previous.TryGetValue(path, out var accepted) && accepted.GuidanceTemplateHash == templateHash)
            {
                retained.Add(path);
                next[path] = accepted with { AppliedHash = ComputeHash(current) };
                continue;
            }
            if (!RepositoryAgentsMerge.TryCreate(root, current, RepositoryImportAssessmentBuilder.EntryPoint,
                    out var merge, relativePath: path))
            { collisions.Add($"Entry point has incomplete or duplicated CIS markers: {path}"); continue; }
            if (merge!.ProposedContent == current) { retained.Add(path); continue; }
            var proposed = request.ReviewedGuidanceContents?.GetValueOrDefault(path) ?? merge.ProposedContent;
            next[path] = new ManagedStarterArtifact("guidance.import.entry-point." + path, path,
                "guidance.import.entry-point", 1, ComputeHash(proposed), "human") { GuidanceTemplateHash = templateHash };
            if (File.Exists(absolute)) merges.Add(merge with { ProposedContent = proposed });
            if (proposed != current) files.Add(new(path, absolute, proposed, File.Exists(absolute) ? current : null,
                File.Exists(absolute) ? PlannedFileAction.Update : PlannedFileAction.Create));
        }
    }

    internal static bool UsesMinimalImport(string root)
    {
        var path = Path.Combine(root, RepositoryImportAssessmentBuilder.ModePath);
        return !CisPathSafety.ContainsReparsePoint(root, path) && File.Exists(path)
            && new FileInfo(path).Length < 32 && File.ReadAllText(path).Trim() == "minimal";
    }

    private static string CreateMinimalDocumentationReadme(string id) =>
        $"---\ntitle: \"{id} CIS documentation\"\ntype: navigation\nstatus: Active\nowner: Repository maintainer\nreview_cadence: on change\ncis:\n  stable_id: {id}:docs:root\n---\n\n"
        + "# CIS documentation\n\nThis is the selected root for new CIS artifacts. Existing project documents remain authoritative until explicitly reconciled.\n\n"
        + "Import installs CIS runtime configuration, scoped guidance and test bindings where a supported harness was detected. Existing profiles are preserved. Validate inferred commands before use; security scanner bindings, additional specifications, standards and capability packs remain explicit follow-up work.\n";
}
