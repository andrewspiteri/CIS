using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.Definition;

public sealed partial class DefinitionWizardService
{
    public CisDefinitionWizardResult ApproveArchitecture(string workspacePath, string reviewer)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Error(state, state.Errors);
        if (string.IsNullOrWhiteSpace(reviewer)) return Error(state, ["Reviewer identity is required."]);
        // Ordinary solution approval requires Active upstream authority. Never approve
        // business or technical choices implicitly as part of an architecture click.
        var solution = _solutionDesign.Validate(workspacePath);
        var diagrams = ReadDiagrams(state, solution);
        var blockers = solution.Errors.Concat(solution.Validation?.Errors ?? []).ToList();
        if (solution.Validation is not { Valid: true, Current: true })
        {
            blockers.Add("The solution design must be valid and current against Active business requirements and technical intent before approval.");
            blockers.AddRange(solution.Validation?.Warnings ?? ["The solution-design bundle is not ready for approval."]);
        }
        if (diagrams.Count == 0 || diagrams.Any(item => item.Status == "Stale"))
            blockers.Add("Prepare or refresh the architecture diagrams before approving the architecture.");
        if (blockers.Count > 0) return StatusInternal(state, "blocked", false) with { Errors = blockers.Distinct().ToArray() };

        var paths = new[] { ResolveDocument(state, solution.DesignPath), ResolveDocument(state, solution.ComponentSheetPath), state.DiagramPath!, state.CatalogPath! };
        if (paths.Any(path => !File.Exists(path) || CisPathSafety.ContainsReparsePoint(state.AuthorityRepositoryPath!, path)))
            return Error(state, ["The architecture approval files are missing or use unsafe links."]);
        var snapshots = paths.ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
        const string reason = "Architecture, component sheet and diagrams approved through the high-level product-definition wizard.";
        try
        {
            var approved = _solutionDesign.Approve(workspacePath, reviewer.Trim(), reason);
            RequireApproved(approved.Validation?.EffectiveStatus, "solution design");
            BindDiagramSources(state, approved);
            ActivateDerived(state.DiagramPath!, reviewer.Trim(), reason);
            var id = $"{state.AuthorityRepositoryId}:architecture:high-level-diagrams";
            WriteAtomic(state.CatalogPath!, Regex.Replace(File.ReadAllText(state.CatalogPath!),
                $@"(?ms)(^\s*-\s+id:\s*{Regex.Escape(id)}\s*$.*?^\s+status:\s*)[^\r\n]+", "${1}active"));
            var graph = _graph.Build(state.AuthorityRepositoryPath!);
            if (graph.ExitCode != 0) throw new InvalidOperationException("Context graph rebuild failed after architecture approval.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            foreach (var snapshot in snapshots) WriteBytesAtomic(snapshot.Key, snapshot.Value);
            return Error(state, [$"Architecture approval failed and its canonical files were restored: {exception.Message}"]);
        }
        return StatusInternal(state, "architecture-approved", true);
    }
}
