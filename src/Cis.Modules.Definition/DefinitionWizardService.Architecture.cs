using System.Net;
using System.Text;
using Cis.Abstractions;
using Cis.Modules.SolutionDesign;

namespace Cis.Modules.Definition;

public sealed partial class DefinitionWizardService
{
    private static string RenderInferredDiagrams(State state, SolutionDesignResult solution, ArchitectureDiagramModel model)
    {
        var images = model.Render();
        var directory = Path.GetDirectoryName(state.DiagramPath!)!;
        // Validate the complete set before writing any asset. Changed content receives a new path.
        foreach (var image in images)
        {
            var path = Path.Combine(directory, image.RelativePath);
            if (CisPathSafety.ContainsReparsePoint(state.AuthorityRepositoryPath!, path)) throw new InvalidDataException("Unsafe architecture diagram asset path.");
            if (File.Exists(path) && File.ReadAllText(path) != image.Content) throw new InvalidDataException("Preserved modified architecture SVG; restore or move it before regeneration: " + image.RelativePath);
        }
        foreach (var image in images)
        {
            var path = Path.Combine(directory, image.RelativePath);
            if (!File.Exists(path)) WriteAtomic(path, image.Content);
        }
        var document = new StringBuilder($"""
---
title: "{state.AuthorityRepositoryId} High-Level Architecture Diagrams"
type: architecture-diagram-set
status: Review Required
scope: Workspace
owner: Product owner and architecture maintainers
last_reviewed: null
cis:
  stable_id: {state.AuthorityRepositoryId}:architecture:high-level-diagrams
  diagram_schema: 1
  source_hash: {DiagramSourceHash(state, solution)}
  approved_by: null
  approved_at: null
  approval_reason: null
  approved_content_hash: null
---

# High-Level Architecture Diagrams

These views explain the existing system from the current architecture draft. Blue nodes and solid
relationships represent observations; green nodes represent proposals; yellow nodes and dashed
relationships identify unresolved or proposed details. See each view's evidence limitations.
Source provenance stays in comments in the overall design. The component sheet and design remain
the authority for ownership and direction. No live environment or deployment has been verified.

""");
        foreach (var image in images)
        {
            document.AppendLine($"\n## {WebUtility.HtmlEncode(image.Title)}\n");
            document.AppendLine($"![{WebUtility.HtmlEncode(image.Title)}]({image.RelativePath})\n");
            document.AppendLine(WebUtility.HtmlEncode(image.Notes));
            document.AppendLine($"\n[Open full-size diagram]({image.RelativePath})\n");
        }
        return document.ToString();
    }
}
