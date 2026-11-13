using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Change;

namespace Cis.Modules.Plan;

public sealed partial class PlanningService
{
    private (PlanWorkItem Task, IReadOnlyList<CisTaskVerificationObligation> Baseline) PrepareTaskTypeMigration(
        ChangeDossier change, PlanSource? source, IReadOnlyList<PlanWorkItem> workItems, PlanWorkItem previous,
        PlanWorkItem candidate, CisTaskTypeDefinition definition)
    {
        // Read and classify against the retained generation baseline before changing
        // either canonical plan format. Human-authored obligations are not defaults.
        _ = ReadTaskContract(change, previous);
        var baseline = ReadImportedLists(change, previous)?.VerificationObligations;
        if (baseline is null)
        {
            var expected = DefaultVerificationObligations(change.RepositoryPath, previous);
            baseline = expected.Where(generated => previous.VerificationObligations.Any(actual =>
                FeatureIssuePackBuilder.SameObligation(actual, generated))).ToArray();
        }
        var authored = previous.VerificationObligations.Where(actual => !baseline.Any(generated =>
            FeatureIssuePackBuilder.SameObligation(actual, generated))).ToArray();
        var refreshed = source is null
            ? candidate with { VerificationObligations = DefaultVerificationObligations(change.RepositoryPath, candidate) }
            : FeatureIssuePackBuilder.RefreshTaskProtocol(change.RepositoryPath, source, candidate, definition, workItems,
                task => DefaultVerificationObligations(change.RepositoryPath, task));
        var authoredErrors = CisStoryEngineeringContract.ValidateVerificationObligations(authored,
            [refreshed.AcceptanceCriteria], required: false);
        if (authoredErrors.Count > 0)
            throw new InvalidDataException($"Task {previous.Id} retains authored verification obligations that require reconciliation with the target contract: "
                + string.Join("; ", authoredErrors));
        var generatedBaseline = refreshed.VerificationObligations;
        var authoredIds = authored.Select(obligation => obligation.Id).ToHashSet(StringComparer.Ordinal);
        var removedIds = baseline.Where(generated => !previous.VerificationObligations.Any(actual =>
            actual.Id == generated.Id)).Select(obligation => obligation.Id).ToHashSet(StringComparer.Ordinal);
        return (refreshed with
        {
            VerificationObligations = generatedBaseline.Where(generated => !authoredIds.Contains(generated.Id)
                && !removedIds.Contains(generated.Id)).Concat(authored).ToArray()
        }, generatedBaseline);
    }

    private static string StampMigratedProtocolBaseline(string text, IReadOnlyList<CisTaskVerificationObligation> baseline)
        => SetOutcomeImportedField(text, "imported_verification_obligations", JsonSerializer.Serialize(baseline));
}
