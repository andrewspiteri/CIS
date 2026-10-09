using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.SolutionDesign;

public sealed partial class SolutionDesignService
{
    /// <summary>Records a deliberate review of an unchanged design against new technical direction.</summary>
    public SolutionDesignResult Reconcile(string workspacePath, string expectedDesignSha256,
        string expectedComponentsSha256, string expectedTechnicalVersion, string actor, string reason)
    {
        var state = Resolve(workspacePath);
        if (state.Errors.Count > 0) return Error("invalid", state, state.Errors);
        if (state.ReadinessErrors.Count > 0) return Error("blocked", state, state.ReadinessErrors);
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(reason))
            return Error("invalid", state, ["Reconciliation actor and reason are required; this action does not approve the bundle."]);
        if (state.TechnicalIntentVersion != expectedTechnicalVersion)
            return Error("blocked", state, ["Technical direction changed since review. Inspect its current version before reconciling."]);

        var paths = new[] { state.DesignPath!, state.ComponentSheetPath!, state.Context!.CatalogPath, state.TechnicalIntentPath! };
        if (paths.Any(path => !File.Exists(path) || CisPathSafety.ContainsReparsePoint(state.Authority!.RepositoryPath, path)))
            return Error("blocked", state, ["Reconciliation requires existing, safe canonical documents and catalogue."]);
        var originals = paths.ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        if (!MatchesSha256(originals[state.DesignPath!], expectedDesignSha256)
            || !MatchesSha256(originals[state.ComponentSheetPath!], expectedComponentsSha256))
            return Error("blocked", state, ["The architecture bundle changed since review. Inspect both files before reconciling."]);

        var assessed = Validate(workspacePath);
        if (assessed.Validation is not { Valid: true }) return assessed with { Status = "blocked" };
        if (assessed.Validation.Current) return assessed with { Status = "unchanged" };
        var design = File.ReadAllText(state.DesignPath!);
        var components = File.ReadAllText(state.ComponentSheetPath!);
        if (!ParseComponents(components).Select(item => item.Id).Order(StringComparer.Ordinal)
            .SequenceEqual(state.Components.Select(item => item.Id).Order(StringComparer.Ordinal)))
            return Error("blocked", state, ["Component identities changed. Revise the architecture before reconciling its source baseline."]);

        // This is an explicit assertion that the retained narrative still fits the source.
        // It records reconciliation, never implementation inference or approval provenance.
        var receipt = JsonSerializer.Serialize(new
        {
            actor = actor.Trim(), reason = reason.Trim(), at = _clock().ToUniversalTime(),
            previousDesignSource = ReadNested(design, "technical_intent_hash"),
            previousComponentsSource = ReadNested(components, "technical_intent_hash"),
            technicalIntentVersion = expectedTechnicalVersion,
            designSha256 = expectedDesignSha256, componentsSha256 = expectedComponentsSha256,
        });
        string Refresh(string content) => ResetApproval(ReplaceNested(content,
            "technical_intent_hash", JsonSerializer.Serialize(expectedTechnicalVersion)))
            .Replace(ReconciliationPendingMarker, "", StringComparison.Ordinal).TrimEnd()
            + "\n\n<!-- cis:solution-design-reconciliation\n" + receipt + "\n-->\n";
        var next = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [state.DesignPath!] = Refresh(design),
            [state.ComponentSheetPath!] = Refresh(components),
            [state.Context.CatalogPath] = UpdateCatalogStatus(UpdateCatalogStatus(
                File.ReadAllText(state.Context.CatalogPath), $"{state.Authority!.Id}:architecture:overall-solution-design", "review-required"),
                $"{state.Authority.Id}:reference:component-sheet", "review-required"),
        };
        if (paths.Any(path => !File.ReadAllBytes(path).SequenceEqual(originals[path])))
            return Error("blocked", state, ["Canonical inputs changed during reconciliation; nothing was applied."]);

        return WriteReconciledBundle(workspacePath, state, originals, next);
    }

    private SolutionDesignResult WriteReconciledBundle(string workspacePath, State state,
        IReadOnlyDictionary<string, byte[]> originals, IReadOnlyDictionary<string, string> next)
    {
        var backupDirectory = Path.Combine(state.Authority!.RepositoryPath, ".cis", "local", "solution-design-reconciliation", Guid.NewGuid().ToString("N"));
        if (CisPathSafety.ContainsReparsePoint(state.Authority.RepositoryPath, backupDirectory))
            return Error("blocked", state, ["Reconciliation backup path is unsafe; nothing was applied."]);
        var bytes = next.ToDictionary(item => item.Key,
            item => Encoding.UTF8.GetBytes(item.Value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n"), StringComparer.Ordinal);
        SolutionDesignResult? validated = null;
        var errors = new ReconciliationFileTransaction().Apply(backupDirectory, originals, bytes, () =>
        {
            validated = ValidateInternal(workspacePath, "reconciled", true);
            return File.ReadAllBytes(state.TechnicalIntentPath!).SequenceEqual(originals[state.TechnicalIntentPath!])
                && validated.Validation is { Valid: true, Current: true, DesignStatus: "Review Required" };
        });
        return errors.Count == 0 ? validated! : Error("blocked", state, errors);
    }

    private static bool MatchesSha256(byte[] bytes, string expected)
        => expected.Length == 64 && Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase);
}
