using System.Text.RegularExpressions;
using Cis.Abstractions;

namespace Cis.Modules.SolutionDesign;

public sealed partial class SolutionDesignService
{
    /// <summary>Render or replace a reviewed C4 data model without re-authoring the narrative or component sheet.</summary>
    public SolutionDesignResult RenderDiagrams(string workspacePath, string? modelPath = null)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Error("invalid", state, state.Errors);
        try
        {
            foreach (var path in new[] { state.DesignPath!, state.ComponentSheetPath! })
                if (CisPathSafety.ContainsReparsePoint(state.Authority!.RepositoryPath, path) || !File.Exists(path)
                    || ReadFrontMatter(File.ReadAllText(path), "status") is not ("Draft" or "Review Required"))
                    return Error("blocked", state, ["C4 diagram updates require safe Draft or Review Required solution-design artifacts."]);
            var original = File.ReadAllText(state.DesignPath!); var sheet = File.ReadAllText(state.ComponentSheetPath!);
            if (ReadNested(original, "technical_intent_hash") != state.TechnicalIntentVersion || ReadNested(sheet, "technical_intent_hash") != state.TechnicalIntentVersion)
                return Error("blocked", state, ["The technical-intent source changed. Reconcile the architecture draft before updating its diagrams."]);
            // Check the current structure before touching the narrative or assets.
            var validation = ValidateInternal(workspacePath, "validated", false, validateDiagramDisplay: false);
            if (validation.Validation?.Valid != true) return validation;
            var next = original;
            if (modelPath is not null)
            {
                if (new FileInfo(modelPath).Length > 100_000) throw new InvalidDataException("Architecture diagram model exceeds its bounded size.");
                var replacement = ArchitectureDiagramModel.Marker + "\n" + File.ReadAllText(modelPath).Trim() + "\n-->";
                if (ArchitectureDiagramModel.ReadRequired(replacement).SchemaVersion != 2) throw new InvalidDataException("Use a schemaVersion 2 C4 model.");
                var regex = new Regex(@"<!-- cis:architecture-views\r?\n.*?\r?\n-->", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (regex.Matches(original).Count > 1) throw new InvalidDataException("Duplicate architecture models must be resolved before rendering.");
                next = regex.IsMatch(original) ? regex.Replace(original, _ => replacement) : original.TrimEnd() + "\n\n" + replacement + "\n";
            }
            var model = ArchitectureDiagramModel.ReadRequired(next);
            if (model.SchemaVersion != 2) return Error("blocked", state, ["This draft has legacy diagrams. Infer architecture again or supply a reviewed C4 JSON model with --model."]);
            var images = model.Render(); next = ArchitectureDiagramPresentation.Embed(next, images);
            if (File.ReadAllText(state.DesignPath!) != original || File.ReadAllText(state.ComponentSheetPath!) != sheet)
                return Error("collision", state, ["The architecture bundle changed during rendering; no canonical update was applied."]);
            ArchitectureDiagramPresentation.WriteAssets(state.Authority!.RepositoryPath, state.DesignPath!, images);
            if (File.ReadAllText(state.DesignPath!) != original || File.ReadAllText(state.ComponentSheetPath!) != sheet)
                return Error("collision", state, ["The architecture bundle changed during rendering; no canonical update was applied."]);
            if (next != original)
            {
                var temporary = state.DesignPath! + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { Write(temporary, next); File.Move(temporary, state.DesignPath!, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return ValidateInternal(workspacePath, "diagrams-rendered", next != original);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        { return Error("invalid", state, [exception.Message]); }
    }
}
