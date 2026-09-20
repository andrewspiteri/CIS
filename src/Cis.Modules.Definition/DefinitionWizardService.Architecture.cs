using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Abstractions;
using Cis.Modules.SolutionDesign;

namespace Cis.Modules.Definition;

public sealed partial class DefinitionWizardService
{
    private static CisDefinitionPageGuidance? ArchitectureGuidance(SolutionDesignResult solution)
        => solution.Validation?.InferenceReconciliationRequired != true ? null : new(
            "Reconcile the architecture with the updated technical direction.",
            "reconcile-architecture",
            "Use Reconcile architecture with technical direction to review the owned repositories and update the design, component sheet and C4 diagrams together. The existing draft is the starting point; human notes and component identities are preserved. Review the resulting changes before approval.",
            ["This architecture was inferred from an earlier technical baseline. Prepare preserves that narrative; it cannot apply changed technical decisions. Refresh only rechecks status."],
            [new("reconcile-architecture", "Reconcile architecture with technical direction", "Needed", "Update the inferred architecture against the current technical intent, then regenerate its diagrams.")]);

    private static void BindDiagramSources(State state, SolutionDesignResult solution)
    {
        // Both architecture approval and consolidated activation change source approval
        // metadata. Bind the reviewed views before signing their final approved content.
        var diagram = File.ReadAllText(state.DiagramPath!);
        WriteAtomic(state.DiagramPath!, ReplaceNested(diagram, "source_hash", JsonSerializer.Serialize(DiagramSourceHash(state, solution))));
    }

    private static bool IsUnchangedActivatedDiagram(State state, SolutionDesignResult solution, string content)
    {
        // Older activations signed the pre-approval source hash. Recognize only the exact
        // unchanged, consolidated baseline whose views still match the current renderer.
        // This is a read-only compatibility check, never a renewed human approval.
        if (solution.Validation is not { Valid: true, Current: true, EffectiveStatus: "Active" }
            || ReadFrontMatter(content, "status") != "Active"
            || ReadNestedValue(content, "approval_reason") != "Approved through the high-level product-definition wizard.") return false;
        var session = ReadSession(state);
        if (session is not { Active: false } || string.IsNullOrWhiteSpace(session.ActivatedAtUtc)
            || string.IsNullOrWhiteSpace(session.SemanticBaselineHash)) return false;
        var baseline = ProductDefinitionAuthority.ComputeBaselineHash(state.DocumentationPath!, out var missing);
        if (missing.Count > 0 || baseline != session.SemanticBaselineHash) return false;
        try
        {
            return DiagramPresentation(content) == DiagramPresentation(RenderDiagrams(state, solution, writeAssets: false));
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string DiagramPresentation(string content)
    {
        // Ignore only generated source/approval fields in front matter. Keep the body,
        // renderer output, identities, schema and all other metadata bound to review.
        content = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var end = content.StartsWith("---\n", StringComparison.Ordinal) ? content.IndexOf("\n---\n", 4, StringComparison.Ordinal) : -1;
        if (end < 0) return content;
        var header = content[..end];
        foreach (var key in new[] { "status", "last_reviewed" })
            header = Regex.Replace(header, $"(?m)^{key}:.*$", $"{key}: <metadata>");
        foreach (var key in new[] { "source_hash", "approved_by", "approved_at", "approval_reason", "approved_content_hash" })
            header = Regex.Replace(header, $"(?m)^  {key}:.*$", $"  {key}: <metadata>");
        return header + content[end..];
    }

    private static string RenderInferredDiagrams(State state, SolutionDesignResult solution, ArchitectureDiagramModel model, bool writeAssets = true)
    {
        var images = model.Render();
        var directory = Path.GetDirectoryName(state.DiagramPath!)!;
        // Validate the complete set before writing any asset. Changed content receives a new path.
        foreach (var image in images)
        {
            var path = Path.Combine(directory, image.RelativePath);
            if (CisPathSafety.ContainsReparsePoint(state.AuthorityRepositoryPath!, path)) throw new InvalidDataException("Unsafe architecture diagram asset path.");
            if (File.Exists(path) && File.ReadAllText(path) != image.Content) throw new InvalidDataException("Preserved modified architecture SVG; restore or move it before regeneration: " + image.RelativePath);
            if (!writeAssets && !File.Exists(path)) throw new InvalidDataException("Architecture diagram asset is missing: " + image.RelativePath);
        }
        foreach (var image in writeAssets ? images : [])
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

These views explain the existing system from the current architecture draft. Each diagram identifies
its scope, element types and directed relationships. Solid relationships represent observations;
green represents proposals and amber represents unresolved details. See each view's evidence limitations.
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
