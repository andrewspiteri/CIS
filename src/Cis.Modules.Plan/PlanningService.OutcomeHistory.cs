using Cis.Abstractions;
using Cis.Modules.Change;

namespace Cis.Modules.Plan;

public sealed partial class PlanningService
{
    private static string? OutcomeSeedReadiness(PlanWorkItem task, IReadOnlyList<PlanWorkItem> items, string? target = null)
    {
        var container = task.Complexity == "high" && items.Count(item => item.ParentId == task.Id) >= 2;
        return CisOutcomeTaskReadiness.UnresolvedSeed(task.InstanceKey, task.Status, task.AcceptanceCriteria, task.Validation, container)
            ?? (target is null ? null : CisOutcomeTaskReadiness.UnresolvedSeed(task.InstanceKey, target,
                task.AcceptanceCriteria, task.Validation, container));
    }

    private static bool OutcomeRetainsHistoricalAuthority(PlanWorkItem task)
        => CanonicalStatus(task.Status) is "Complete" or "Cancelled" or "Deferred" or "Decomposed"
            || PlanTaskDispositionPolicy.IsTerminal(task.Status, task.Category);

    private static CisOutcomeAuthorization? OutcomeTaskValidationAuthority(CisOutcomeAuthorization authority,
        PlanWorkItem task, bool allowHistorical)
    {
        static bool Matches(CisOutcomeAuthorization candidate, PlanWorkItem item)
            => item.AuthorizationScopeIds.Count > 0
                && !item.AuthorizationScopeIds.Except(candidate.Request.Scope.Select(scope => scope.Id), StringComparer.Ordinal).Any()
                && item.AuthorizationScopeBasis == CisOutcomeAuthority.ScopeBasis(candidate, item.AuthorizationScopeIds)
                && OutcomeDefinitionErrors(candidate, new(item.Title, item.AuthorizationScopeIds,
                    item.AcceptanceCriteria, item.Validation)
                { Paths = item.WorkPaths, Complexity = item.Complexity == "high" ? "medium" : item.Complexity, Category = item.Category }).Count == 0;

        if (Matches(authority, task)) return authority;
        if (!allowHistorical || !OutcomeRetainsHistoricalAuthority(task)) return null;
        // ScopeBasis already records the approved outcome, constraints and selected
        // scope. Applicability such as VisualUi can change without changing that
        // digest, so the retained task must also fit the actual revision's definition.
        // Resolve from real amendments, never fabricate approval for an old contract.
        foreach (var revision in authority.History.Reverse())
        {
            var historical = authority with
            {
                Request = revision.Request, Reviewer = revision.Reviewer, Reason = revision.Reason,
                AuthorizedAtUtc = revision.AuthorizedAtUtc, Digest = revision.Digest,
                VisualUiDerivedFromAcceptedSource = revision.VisualUiDerivedFromAcceptedSource,
                History = []
            };
            if (Matches(historical, task)) return historical;
        }
        return null;
    }

    private PlanValidation ValidateOutcomeDisposition(ChangeDossier change, IReadOnlyList<PlanWorkItem> items,
        CisOutcomeAuthorization authority, PlanWorkItem task, string target)
    {
        if (target is not ("Cancelled" or "Deferred"))
            throw new InvalidDataException("Historical scope may only admit cancellation or deferral of unfinished work.");
        var selected = items.Where(item => item.Id == task.Id).ToArray();
        if (selected.Length != 1)
            return new(false, ["The retained task identity is missing or duplicated; preserve its original contract."], [], 0, 0, 0);
        var retained = ReadOutcomeTaskContract(change, selected[0], authority);
        var errors = retained.ContractErrors.ToList();
        var historical = OutcomeTaskValidationAuthority(authority, retained with { Status = target }, allowHistorical: true);
        if (historical is null)
            errors.Add(task.Id + ": no retained approved scope covers this task's historical disposition.");
        else
            errors.AddRange(OutcomeDefinitionErrors(historical, new(retained.Title, retained.AuthorizationScopeIds,
                retained.AcceptanceCriteria, retained.Validation)
            { Paths = retained.WorkPaths, Complexity = retained.Complexity == "high" ? "medium" : retained.Complexity, Category = retained.Category }));
        // A cancellation/deferral records retained work; it grants no execution or
        // completion and therefore cannot depend on today's readiness or UI gates.
        return new(errors.Count == 0, errors, [], 0, 0, 0);
    }

    private bool HistoricalOutcomeCatalogueRequired(ChangeDossier change, PlanSource source,
        CisOutcomeAuthorization authority, IReadOnlyList<PlanWorkItem> tasks)
    {
        if (source.FrontendChanges || tasks.Any(task =>
            OutcomeTaskValidationAuthority(authority, task, allowHistorical: true)?.Request.ManualTestCatalogue == true)) return true;
        var path = _changes.DossierFile(change, "test-cases.md");
        if (File.Exists(path))
        {
            var catalogue = File.ReadAllText(path);
            // A populated catalogue bound to the accepted source is retained evidence.
            // An inactive legacy template alone creates no retrospective obligation.
            if (catalogue.Contains($"feature_spec_path: {System.Text.Json.JsonSerializer.Serialize(source.Path)}", StringComparison.Ordinal)
                && catalogue.Contains($"feature_spec_sha256: {System.Text.Json.JsonSerializer.Serialize(source.Sha256)}", StringComparison.Ordinal)
                && System.Text.RegularExpressions.Regex.IsMatch(catalogue, @"(?im)^###\s+TC-[A-Z0-9]+(?:-[A-Z0-9]+)*:")) return true;
        }
        // Reuse inferred obligations only when their original source bytes are available.
        return ValidateSourceCurrency(change, source).Count == 0
            && FeatureIssuePackBuilder.RequiresTestCatalogue(change.RepositoryPath, source.Path);
    }
}
