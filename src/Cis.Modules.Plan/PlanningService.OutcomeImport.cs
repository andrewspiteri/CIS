using System.Globalization;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Change;

namespace Cis.Modules.Plan;

public sealed partial class PlanningService
{
    private PlanResult ImportDraftOutcomeSpec(ChangeDossier change, Func<PlanResult> import)
    {
        var context = _resolver.Resolve(change.RepositoryPath).Context;
        if (context is null) return Error(change.Id, "The repository context could not be resolved.");
        var dossier = Path.GetDirectoryName(OutcomePlanPath(change))!;
        Dictionary<string, byte[]> original;
        byte[] catalog;
        try
        {
            original = SnapshotTree(dossier);
            catalog = File.ReadAllBytes(context.CatalogPath);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { return Error(change.Id, error.Message); }
        PlanResult Restore(PlanResult failure)
        {
            RestoreTree(dossier, original);
            File.WriteAllBytes(context.CatalogPath, catalog);
            return failure with { Applied = false };
        }

        try
        {
            var imported = import();
            if (imported.ExitCode != 0) return Restore(imported);
            // The successful legacy import owns the complete manifest. Keep its normal
            // exact-plan approval path, rather than retaining a competing draft source.
            File.Delete(OutcomePlanPath(change));
            var validation = ValidateInternal(change, imported.WorkItems, imported.PlanStatus!, imported.Source);
            if (!validation.Valid) return Restore(imported with { Status = "invalid", Validation = validation });
            return imported with { Applied = true, Validation = validation };
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            return Restore(Error(change.Id, error.Message));
        }
    }

    private PlanResult ImportOutcomeSpec(ChangeDossier change, FeatureSpecImportRequest request, string? ignoredReadinessCheck)
    {
        try
        {
            var authority = OutcomeAuthority(change)!;
            if (!authority.Request.OrganizeTasks || !CisOutcomeAuthority.IsController(authority, request.Actor ?? ""))
                return Error(change.Id, "Feature task derivation requires the outcome's delegated controller and task-organization authority.");
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Error(change.Id, "Supply the controller's reason for deriving these feature tasks under the accepted outcome.");
            if (request.AuthorizationScopeIds is null || request.AuthorizationScopeIds.Any(string.IsNullOrWhiteSpace))
                return Error(change.Id, "Declare nonempty accepted scope IDs explicitly.");
            var scopeIds = request.AuthorizationScopeIds.Select(id => id.Trim()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (scopeIds.Length == 0 || scopeIds.Except(authority.Request.Scope.Select(scope => scope.Id), StringComparer.Ordinal).Any())
                return Error(change.Id, "Declare the applicable accepted scope IDs explicitly. Scope expansion needs a targeted human decision; existing tasks retain their authority.");
            var scopeBasis = CisOutcomeAuthority.ScopeBasis(authority, scopeIds);
            var existing = ReadPlan(change);
            if (existing.WorkItems.Any(item => item.ContractErrors.Count > 0))
                return Error(change.Id, existing.WorkItems.SelectMany(item => item.ContractErrors).ToArray());
            if (existing.WorkItems.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != existing.WorkItems.Count)
                return Error(change.Id, "Preserve the retained task records and reconcile their duplicated identities before importing.");
            var exactSource = IsExactImportedSource(request, existing.Source);
            var readiness = EvaluateReadiness(change.RepositoryPath, exactSource ? "technical-intent" : ignoredReadinessCheck);
            if (readiness.Count > 0) return Error(change.Id, readiness.ToArray());
            if (existing.Source is not null && !string.Equals(NormalizePath(request.FeatureSpecPath), existing.Source.Path, StringComparison.Ordinal))
                return Error(change.Id, "Preserve the retained feature source and its task identities; organize separate supporting work for another specification.");
            var capabilities = _capabilities.Read(change.RepositoryPath);
            if (capabilities.Errors.Count > 0) return Error(change.Id, capabilities.Errors.ToArray());
            PlanWorkItem[] importedBefore = existing.Source is null ? [] : existing.WorkItems
                .Where(item => item.InstanceKey?.StartsWith("outcome:", StringComparison.Ordinal) != true).ToArray();
            var pendingMigrations = importedBefore.Where(item => !item.TaskTypeKey.StartsWith("core.", StringComparison.OrdinalIgnoreCase)
                && capabilities.Selections.Any(selection => selection.ReplacesTypeKeys.Contains(item.TaskTypeKey, StringComparer.OrdinalIgnoreCase))).ToArray();
            if (pendingMigrations.Length > 0)
                return Error(change.Id, pendingMigrations.Select(item => $"Task {item.Id} requires its existing authorized task-type migration before re-import.").ToArray());
            var built = FeatureIssuePackBuilder.Build(change.RepositoryPath, request.FeatureSpecPath, change.Id, [], _taskTypes,
                capabilities.Selections, importedBefore, _automationScanner.Scan(change.RepositoryPath), existing.Source?.Sha256,
                task => DefaultVerificationObligations(change.RepositoryPath, task), task => ReadImportedLists(change, task),
                preserveApprovedCoverage: exactSource, reservedTaskIds: existing.WorkItems.Select(item => item.Id).ToArray());
            if (built.Pack is not { } pack) return Error(change.Id, built.Errors.ToArray());
            var applicability = _changes.DocumentApplicability(change, pack.Source.FrontendChanges,
                FeatureIssuePackBuilder.RequiresTestCatalogue(change.RepositoryPath, pack.Source.Path));
            if (applicability.Errors.Count > 0) return Error(change.Id, applicability.Errors.ToArray());

            var imported = new List<PlanWorkItem>();
            foreach (var generated in pack.WorkItems)
            {
                var prior = importedBefore.SingleOrDefault(item => item.Id == generated.Id);
                // An exact source refresh may update derived catalogues, never the
                // retained executable contract, evidence, lifecycle or accounting owner.
                var item = (exactSource && prior is not null ? prior : generated) with
                {
                    AuthorizationScopeIds = scopeIds,
                    AuthorizationScopeBasis = scopeBasis,
                    WorkPaths = prior?.WorkPaths ?? [],
                    Assignee = prior?.Assignee,
                };
                if (prior is not null && CanonicalTaskStatus(prior.Status) != "Draft"
                    && (!exactSource || OutcomeTaskDigest(prior) != OutcomeTaskDigest(item)))
                    return Error(change.Id, $"Task {prior.Id} has a changed executable contract or selected scope. Preserve its result and owner; reconcile only that affected work before re-import.");
                imported.Add(item);
            }
            var selectedIds = imported.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var removed = importedBefore.Where(item => !selectedIds.Contains(item.Id)).ToArray();
            if (removed.Any(item => CanonicalTaskStatus(item.Status) != "Draft"))
                return Error(change.Id, "The revised source removes started or completed task identities. Preserve their contracts and owners and reconcile that specific difference before re-import.");
            var oldImportedIds = importedBefore.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            PlanWorkItem[] seeds = existing.Source is null ? existing.WorkItems.Where(item => IsUnstartedOutcomeSeed(change, item)).ToArray() : [];
            var seedIds = seeds.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var retained = existing.WorkItems.Where(item => !oldImportedIds.Contains(item.Id) && !seedIds.Contains(item.Id)).ToArray();
            var items = retained.Concat(imported).ToArray();
            var ids = items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            if (ids.Count != items.Length || items.Any(item => item.DependsOn.Any(id => !ids.Contains(id))) || HasCycle(items))
                return Error(change.Id, "Retain exact task identities and reconcile dependencies before replacing the affected feature work.");
            foreach (var obsolete in removed.Concat(seeds)) ResolveTaskRetirementPaths(change, obsolete);
            var dossier = Path.GetDirectoryName(OutcomePlanPath(change))!;
            foreach (var item in imported)
                if (!CisPathSafety.TryResolveUnderRoot(dossier, item.TaskPath!, out var taskPath)
                    || CisPathSafety.ContainsReparsePoint(change.RepositoryPath, taskPath))
                    return Error(change.Id, "Imported task documents must remain at unlinked paths inside the dossier.");

            var context = _resolver.Resolve(change.RepositoryPath).Context!;
            var original = SnapshotTree(dossier);
            var catalog = File.ReadAllBytes(context.CatalogPath);
            PlanResult Restore(PlanResult failure)
            {
                RestoreTree(dossier, original);
                File.WriteAllBytes(context.CatalogPath, catalog);
                return failure with { Applied = false };
            }
            try
            {
                var documentErrors = _changes.EnsureApplicableDocuments(change, pack.Source.FrontendChanges, applicability.ManualTestCatalogue);
                if (documentErrors.Count > 0) return Restore(Error(change.Id, documentErrors.ToArray()));
                var applied = false;
                if (applicability.ManualTestCatalogue)
                {
                    applied |= WriteIfChanged(_changes.DossierFile(change, "test-cases.md"), pack.ManualTestCasesMarkdown);
                    applied |= WriteIfChanged(_changes.DossierFile(change, "test-cases.csv"), pack.ManualTestCasesCsv);
                }
                foreach (var item in imported)
                {
                    var taskPath = Path.Combine(dossier, item.TaskPath!);
                    var prior = importedBefore.SingleOrDefault(candidate => candidate.Id == item.Id);
                    if (exactSource && prior is not null && File.Exists(taskPath))
                    {
                        if (!prior.AuthorizationScopeIds.SequenceEqual(item.AuthorizationScopeIds, StringComparer.Ordinal)
                            || prior.AuthorizationScopeBasis != item.AuthorizationScopeBasis)
                            applied |= WriteIfChanged(taskPath, StampOutcomeImportedTask(File.ReadAllText(taskPath), item, authority));
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(taskPath)!);
                    var document = StampOutcomeImportedTask(pack.TaskDocuments[item.TaskPath!], item, authority);
                    applied |= WriteTaskPreservingEvidence(taskPath, document);
                }
                applied |= RetireObsoleteTasks(change, removed.Concat(seeds).ToArray(), items, pack.Source);
                applied |= WriteOutcomePlan(change, new(authority.Id, request.Actor!, items, pack.Source));
                EnsureOutcomeVerification(change);
                applied |= RegisterFeatureSpecCatalogEntry(change, pack.Source);
                if (applicability.ManualTestCatalogue) applied |= RegisterManualTestCasesCatalogEntry(change);
                applied |= RegisterTaskCatalogEntries(change, items);
                _changes.RegisterExistingDocuments(change);
                var result = OutcomeResult(change, items, authority, applied ? "imported" : "unchanged", applied, selectedIds) with { Source = pack.Source };
                if (result.ExitCode != 0) return Restore(result);
                if (applied) _changes.AppendEvent(change, "feature-spec-plan-built", new Dictionary<string, string>
                {
                    ["source"] = pack.Source.Path,
                    ["sourceSha256"] = pack.Source.Sha256,
                    ["authorizationId"] = authority.Id,
                    ["scopeIds"] = string.Join(',', scopeIds),
                    ["controller"] = request.Actor!,
                    ["reason"] = request.Reason!,
                    ["workItems"] = imported.Count.ToString(CultureInfo.InvariantCulture),
                });
                return result;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                return Restore(Error(change.Id, error.Message));
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return Error(change.Id, error.Message);
        }
    }

    private static bool IsUnstartedOutcomeSeed(ChangeDossier change, PlanWorkItem item)
        => item.Id == "WORK-001" && item.InstanceKey?.StartsWith("outcome:", StringComparison.Ordinal) == true
            && item.TaskTypeKey == "core.legacy" && item.ParentId is null && item.Status == "Draft"
            && item.Title == change.Title && item.AcceptanceCriteria == "Deliver the shared outcome and its applicable checks."
            && item.Validation == "Build the affected component and run its connected checks.";

    private static string StampOutcomeImportedTask(string text, PlanWorkItem item, CisOutcomeAuthorization authority)
    {
        text = ReplaceFrontMatter(text, "authority", "derived-outcome-authorization");
        text = ReplaceFrontMatter(text, "task_status", item.Status);
        text = SetOutcomeImportedField(text, "outcome_authorization_id", JsonSerializer.Serialize(authority.Id));
        text = SetOutcomeImportedField(text, "authorization_scope_ids", JsonSerializer.Serialize(item.AuthorizationScopeIds));
        text = SetOutcomeImportedField(text, "authorization_scope_basis", JsonSerializer.Serialize(item.AuthorizationScopeBasis));
        return text;
    }

    private static string SetOutcomeImportedField(string text, string key, string value)
    {
        if (HasTaskMetadata(text, key)) return ReplaceFrontMatter(text, key, value);
        var closing = text.IndexOf("\n---", StringComparison.Ordinal);
        if (!text.StartsWith("---", StringComparison.Ordinal) || closing < 0)
            throw new InvalidDataException("An imported task needs complete front matter before recording its scope binding.");
        return text.Insert(closing, "\n" + key + ": " + value);
    }
}
