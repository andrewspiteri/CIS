using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Change;

namespace Cis.Modules.Plan;

public sealed partial class PlanningService
{
    private static IReadOnlyList<string> ValidateInlinePublicEndpointVerification(IReadOnlyList<PlanWorkItem> workItems)
    {
        var errors = new List<string>();
        var separateVerification = workItems.Any(item => item.Category == "verification");
        var separateReview = workItems.Any(item => item.Category == "assurance");
        foreach (var owner in workItems.Where(item => item.Category is "security" or "contract" or "backend" or "observability"))
        {
            var obligations = owner.VerificationObligations ?? [];
            if (!separateVerification)
                errors.AddRange(CisStoryEngineeringContract.ValidateVerificationObligations(obligations,
                    [owner.AcceptanceCriteria], required: true).Select(error =>
                    $"PUBLIC-ENDPOINT-CACHE task {owner.Id} needs verification within its accepted work: {error}"));
            if (!separateReview && !obligations.Any(obligation => obligation is not null
                && obligation.EvidenceRoute == "review"
                && obligation.Criteria?.Contains(owner.AcceptanceCriteria, StringComparer.Ordinal) == true
                && owner.RequirementIds.All(requirement => obligation.BasisReferences?.Contains(requirement, StringComparer.OrdinalIgnoreCase) == true)))
                errors.Add($"PUBLIC-ENDPOINT-CACHE task {owner.Id} needs independent review evidence within its accepted work or an explicitly owned assurance deliverable.");
        }
        return errors;
    }

    private IReadOnlyList<string> ReviewInlineDeliveryCompletion(ChangeDossier change,
        IReadOnlyList<PlanWorkItem> workItems, PlanWorkItem task)
    {
        try
        {
            var code = task.Category is not ("coordination" or "wireframe" or "design" or "documentation" or "verification" or "assurance" or "delivery");
            var ownsWorkload = code && HasWorkloadObligation(change, task);
            var finalDelivery = task.Category != "coordination" && workItems
                .Where(other => other.Id != task.Id && other.Category != "coordination")
                .All(other => PlanTaskDispositionPolicy.IsTerminal(other.Status, other.Category));
            if (!ownsWorkload && !finalDelivery) return [];
            // New compact plans record positive workload scope explicitly. Raw prose
            // such as "no bulk operations" must not turn an exclusion into a workload.
            var suggested = workItems.Any(candidate => HasWorkloadObligation(change, candidate));
            // The owning implementation checks applicable workload evidence. The final
            // actual deliverable also reconciles the existing overall readiness record;
            // an unrelated early leaf does not acquire a synthetic final-sweep gate.
            return WorkloadEvidenceReview.Review(change.RepositoryPath,
                _changes.DossierFile(change, "workload-evidence.json"), suggested);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { return ["Task workload scope could not be read from its retained contract: " + error.Message]; }
    }

    private bool HasWorkloadObligation(ChangeDossier change, PlanWorkItem task)
    {
        // Authored evidence routes can replace generated obligations. They cannot
        // erase accepted positive workload scope retained by the existing import baseline.
        var generated = ReadImportedLists(change, task)?.VerificationObligations ?? [];
        return (task.VerificationObligations ?? []).Concat(generated).Any(obligation => obligation?.EvidenceExpectations?.Any(expectation =>
            expectation.Contains("workload-evidence.json", StringComparison.OrdinalIgnoreCase)) == true);
    }
}
