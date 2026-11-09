using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cis.Modules.Impact;

namespace Cis.Modules.Plan;

internal static partial class FeatureIssuePackBuilder
{
    public static FeatureIssuePackBuildResult Build(
        string repositoryPath,
        string requestedPath,
        string changeId,
        IReadOnlyList<ImpactFinding> accepted,
        TaskTypeRegistry registry,
        IReadOnlyList<TaskTypeCapabilitySelection> selections,
        IReadOnlyList<PlanWorkItem> existingItems,
        IReadOnlyDictionary<string, IReadOnlyList<string>> automationReferences,
        string? existingSourceHash = null,
        Func<PlanWorkItem, IReadOnlyList<Cis.Abstractions.CisTaskVerificationObligation>>? defaultObligations = null,
        Func<PlanWorkItem, ImportedTaskLists?>? readImportedLists = null)
    {
        var parsed = Parse(repositoryPath, requestedPath);
        if (parsed.Spec is null)
        {
            return new(null, parsed.Errors, []);
        }

        var spec = parsed.Spec;
        var source = new PlanSource(
            spec.RelativePath,
            spec.Sha256,
            spec.DocumentType,
            spec.Requirements.Count,
            spec.Frontend,
            spec.Requirements.Select(item => item.Id).ToArray(),
            spec.Targets,
            spec.Stack,
            spec.References,
            spec.FrontendTypes,
            spec.PublicEndpoints);
        var applicable = registry.Definitions.Where(definition => IsApplicable(definition, spec)).ToArray();
        var resolution = registry.ResolveApplicable(applicable, selections);
        if (!resolution.IsSuccess)
        {
            var errors = resolution.Errors.Concat(resolution.Conflicts.Select(conflict =>
                $"Task capability conflict '{conflict.CapabilityKey}': {string.Join(", ", conflict.CandidateTypeKeys)}. {conflict.Reason}"))
                .ToArray();
            return new(null, errors, resolution.Conflicts);
        }
        var generatedObligations = new Dictionary<string, IReadOnlyList<Cis.Abstractions.CisTaskVerificationObligation>>(StringComparer.Ordinal);
        var items = BuildItems(spec, accepted, resolution.Definitions, existingItems, existingSourceHash,
            defaultObligations, readImportedLists, generatedObligations, out var instanceErrors);
        if (instanceErrors.Count > 0) return new(null, instanceErrors, []);
        var documents = items.ToDictionary(
            item => item.TaskPath!,
            item => RenderTask(spec, source, item, items, accepted, generatedObligations[item.Id]),
            StringComparer.OrdinalIgnoreCase);
        var manualTests = BuildManualTestCases(changeId, spec, source, automationReferences);
        return new(new FeatureIssuePack(source, items, documents, spec.Title,
            manualTests.Markdown, manualTests.Csv, manualTests.Count, manualTests.AutomatedCount), [], []);
    }

    private static IReadOnlyList<PlanWorkItem> BuildItems(
        FeatureSpec spec,
        IReadOnlyList<ImpactFinding> accepted,
        IReadOnlyList<Cis.Abstractions.CisTaskTypeDefinition> definitions,
        IReadOnlyList<PlanWorkItem> existingItems,
        string? existingSourceHash,
        Func<PlanWorkItem, IReadOnlyList<Cis.Abstractions.CisTaskVerificationObligation>>? defaultObligations,
        Func<PlanWorkItem, ImportedTaskLists?>? readImportedLists,
        IDictionary<string, IReadOnlyList<Cis.Abstractions.CisTaskVerificationObligation>> generatedObligations,
        out IReadOnlyList<string> errors)
    {
        var allRequirements = spec.Requirements.Select(item => item.Id).ToArray();
        var allImpacts = accepted.Select(item => item.Id).ToArray();
        var applicable = definitions.ToArray();
        var instances = applicable.SelectMany(definition =>
            (IsFrontendCategory(definition.Category) ? spec.FrontendTypes.Cast<string?>() : [null])
            .SelectMany(frontendType =>
            {
                var storyIds = IsStoryScopedCategory(definition.Category)
                    ? spec.Requirements.Where(requirement => requirement.StoryId is not null
                        && (frontendType is null || requirement.FrontendType == frontendType)
                        && MatchesCategory(requirement, definition.Category, definition.TriggerTerms))
                        .Select(requirement => requirement.StoryId!).Distinct(StringComparer.Ordinal).ToArray()
                    : [];
                if (storyIds.Length == 0) return new[] { new TaskInstance(definition, frontendType, null) };
                var scopes = storyIds.Select(story => new TaskInstance(definition, frontendType, story));
                if (spec.Requirements.Any(requirement => requirement.StoryId is null
                    && (frontendType is null || requirement.FrontendType == frontendType)
                    && MatchesCategory(requirement, definition.Category, definition.TriggerTerms)))
                    scopes = scopes.Append(new TaskInstance(definition, frontendType, null));
                return scopes;
            })).ToArray();
        var problems = new List<string>();
        var mappedScopes = new Dictionary<string, PlanWorkItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var retained in existingItems)
        {
            var replacementScopes = instances.Where(instance =>
                instance.Definition.Key.Equals(retained.TaskTypeKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(instance.FrontendType, retained.FrontendType, StringComparison.OrdinalIgnoreCase))
                .Select(instance => new
                {
                    Instance = instance,
                    Requirements = InstanceRequirements(instance).ToHashSet(StringComparer.OrdinalIgnoreCase)
                }).Where(scope => scope.Requirements.Overlaps(retained.RequirementIds)).ToArray();
            if (replacementScopes.Length == 0) continue; // New independent requirements do not replace historical work.
            var sameScope = replacementScopes.Count(scope => scope.Instance.StoryId == retained.StoryId);
            var retainedTargets = replacementScopes.All(scope => existingItems.Count(item =>
                item.TaskTypeKey.Equals(scope.Instance.Definition.Key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.FrontendType, scope.Instance.FrontendType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.StoryId, scope.Instance.StoryId, StringComparison.Ordinal)
                && scope.Requirements.Overlaps(item.RequirementIds)) == 1);
            if (sameScope == 1 && retainedTargets && (retained.StoryId is not null || replacementScopes.Length == 1)) continue;
            if (replacementScopes.Length == 1 && replacementScopes[0].Requirements.SetEquals(retained.RequirementIds))
            {
                var scope = replacementScopes[0].Instance;
                var identity = Identity(scope.Definition.Key, scope.FrontendType, scope.StoryId);
                if (!mappedScopes.TryAdd(identity, retained with { StoryId = scope.StoryId, InstanceKey = retained.InstanceKey ?? identity }))
                    problems.Add($"Legacy tasks have an ambiguous retained mapping to story {scope.StoryId}; retain their separate IDs and accounting owners before re-import.");
            }
            else
                problems.Add($"Retained task {retained.Id} (accounting owner {retained.WorkOwnerTaskId ?? retained.Id}) cannot be replaced automatically by story scopes {string.Join(", ", replacementScopes.Select(scope => scope.Instance.StoryId ?? "shared"))}. Explicitly map its retained ID and accounting owner before re-import; its history and reservations must not become fresh suffixed tasks.");
        }
        var existingGroups = existingItems.GroupBy(item => Identity(item.TaskTypeKey, item.FrontendType, item.StoryId), StringComparer.OrdinalIgnoreCase).ToArray();
        var mappedIds = mappedScopes.Values.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var duplicate in existingGroups.Where(group => group.Count() > 1 && group.Any(item => !mappedIds.Contains(item.Id))))
            problems.Add($"Task instances {string.Join(", ", duplicate.Select(item => item.Id))} have an ambiguous legacy type/story/frontend binding '{duplicate.Key}'. Retain their IDs and assign distinct explicit story scopes before re-import.");
        var existingByIdentity = existingGroups.Where(group => group.Count() == 1 && !mappedIds.Contains(group.Single().Id))
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
        foreach (var (identity, mapped) in mappedScopes)
            if (!existingByIdentity.TryAdd(identity, mapped))
                problems.Add($"Task {mapped.Id} cannot replace an already retained story instance '{identity}'; resolve the exact owner mapping before re-import.");
        var orderCounts = instances.GroupBy(instance => instance.Definition.Order)
            .ToDictionary(group => group.Key, group => group.Count());
        var reservedIds = existingItems.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reservedKeys = existingItems.Where(item => item.InstanceKey is not null).Select(item => item.InstanceKey!).ToHashSet(StringComparer.Ordinal);
        var idByIdentity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var keyByIdentity = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var instance in instances.OrderBy(instance => Identity(instance.Definition.Key, instance.FrontendType, instance.StoryId), StringComparer.Ordinal))
        {
            var identity = Identity(instance.Definition.Key, instance.FrontendType, instance.StoryId);
            if (existingByIdentity.TryGetValue(identity, out var existing))
            {
                idByIdentity[identity] = existing.Id;
                keyByIdentity[identity] = existing.InstanceKey;
                continue;
            }
            var key = identity;
            if (!reservedKeys.Add(key))
            {
                var unique = identity + "|instance:" + ShortIdentityHash(identity + "\n" + string.Join("\n", InstanceRequirements(instance).Order(StringComparer.Ordinal)));
                key = unique;
                for (var suffix = 2; !reservedKeys.Add(key); suffix++) key = unique + ":" + suffix;
            }
            var baseId = $"WORK-{instance.Definition.Order:000}";
            var id = instance.StoryId is not null ? $"{baseId}-{TaskKeySuffix(instance.StoryId)}-{ShortIdentityHash(identity)[..8]}"
                : instance.FrontendType is not null ? $"{baseId}-{FrontendTypeSuffix(instance.FrontendType)}"
                : orderCounts[instance.Definition.Order] == 1 ? baseId : $"{baseId}-{TaskKeySuffix(instance.Definition.Key)}";
            if (!reservedIds.Add(id))
            {
                var unique = id + "-" + ShortIdentityHash(key);
                id = unique;
                for (var suffix = 2; !reservedIds.Add(id); suffix++) id = unique + "-" + suffix;
            }
            idByIdentity[identity] = id;
            keyByIdentity[identity] = key;
        }
        var idsByType = instances.GroupBy(instance => instance.Definition.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(instance =>
                (instance.FrontendType, instance.StoryId, Id: idByIdentity[Identity(instance.Definition.Key, instance.FrontendType, instance.StoryId)])).ToArray(), StringComparer.OrdinalIgnoreCase);
        var coordinationId = idsByType["core.coordination.scope-guard"].Single().Id;
        var items = new List<PlanWorkItem>();

        foreach (var instance in instances)
        {
            var definition = instance.Definition;
            var identity = Identity(definition.Key, instance.FrontendType, instance.StoryId);
            var id = idByIdentity[identity];
            var existing = existingByIdentity.GetValueOrDefault(identity);
            var dependencies = ResolveDependencies(definition, instance.FrontendType, instance.StoryId, applicable, idsByType, existing?.DependsOn ?? [], problems);
            var requirementIds = InstanceRequirements(instance);
            var impactIds = definition.Category switch
            {
                "documentation" => Impacts(accepted, "documentation", "contract"),
                "verification" or "assurance" => Impacts(accepted, "verification", "implementation"),
                _ => allImpacts,
            };
            var highParent = definition.Key == "core.coordination.scope-guard";
            var importedLists = GeneratedLists(spec, definition.Category, instance.StoryId);
            ImportedTaskLists? priorLists = null;
            try { if (existing is not null) priorLists = readImportedLists?.Invoke(existing); }
            catch (InvalidDataException error) { problems.Add($"{existing!.Id}: {error.Message}"); }
            if (existing is not null && priorLists is null && existingSourceHash is not null && existingSourceHash != spec.Sha256)
                problems.Add($"Task {existing.Id} has no retained imported-list baseline for the revised source. Retain its authored contract and re-import the exact previously recorded source to establish its generated lists before applying this revision.");
            if (existingSourceHash is null && priorLists is null) priorLists = new([], []);
            var acceptance = spec.PublicEndpoints && PublicEndpointPolicyCategory(definition.Category)
                ? definition.AcceptanceCriteria + " " + PublicEndpointAcceptance(definition.Category)
                : definition.AcceptanceCriteria;
            var validation = spec.PublicEndpoints && PublicEndpointPolicyCategory(definition.Category)
                ? definition.Validation + " " + PublicEndpointValidation(definition.Category)
                : definition.Validation;
            if (instance.StoryId is not null)
            {
                acceptance += " " + string.Join(" ", spec.Requirements.Where(requirement => requirementIds.Contains(requirement.Id)).Select(requirement => requirement.AcceptanceCriteria));
                validation = $"For story {instance.StoryId}: {validation}";
            }
            if (!spec.Frontend && definition.Category == "verification")
                validation = validation.Replace("browser journeys, ", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (definition.Key == "core.verification" && RequiresTestCatalogue(spec))
                validation += " Maintain required catalogue TC-* traceability into recognized automated tests.";
            var item = new PlanWorkItem(
                id,
                definition.Category,
                highParent ? "high" : definition.DefaultComplexity,
                highParent ? null : coordinationId,
                existing?.TaskPath ?? $"agent-tasks/{id}.md",
                instance.FrontendType is null ? definition.Title : $"{definition.Title} ({instance.FrontendType})",
                requirementIds,
                impactIds,
                dependencies,
                acceptance,
                validation,
                highParent ? "decomposed" : "draft",
                definition.Key,
                definition.Version,
                definition.ApprovalGate,
                ResolveTargets(spec, definition.Category),
                existing?.DecisionIds ?? [],
                ReconcileImportedList(existing?.NegativeCriteria, priorLists?.NegativeCriteria, importedLists.NegativeCriteria),
                ReconcileImportedList(existing?.RequiredOutputs, priorLists?.RequiredOutputs, importedLists.RequiredOutputs),
                instance.FrontendType) with
            {
                Status = existing?.Status ?? (highParent ? "decomposed" : "draft"),
                StoryId = instance.StoryId,
                InstanceKey = keyByIdentity[identity],
                WorkOwnerTaskId = existing?.WorkOwnerTaskId,
            };
            var generated = defaultObligations?.Invoke(item) ?? DefaultVerificationObligations(item, []);
            generatedObligations[id] = generated;
            var priorGenerated = priorLists?.VerificationObligations;
            if (existing is not null && priorGenerated is null)
            {
                var reconstructed = defaultObligations?.Invoke(existing) ?? DefaultVerificationObligations(existing, []);
                if (reconstructed.All(expected => existing.VerificationObligations.Any(prior => SameObligation(prior, expected))))
                    priorGenerated = reconstructed;
            }
            if (existing is not null && priorGenerated is null && existing.VerificationObligations.Count > 0
                && generated.Any(current => !existing.VerificationObligations.Any(prior => SameObligation(prior, current))))
            {
                problems.Add($"Task {id} has no retained generated verification baseline for the changed contract or policy. Preserve its authored obligations and review/replan this affected task before re-import; the previous evidence route or gates cannot be guessed.");
            }
            var authored = existing?.VerificationObligations.Where(obligation =>
                !(priorGenerated ?? generated).Any(prior => SameObligation(obligation, prior))).ToArray() ?? [];
            if (existing is not null && existing.AcceptanceCriteria != acceptance && authored.Any(obligation =>
                obligation.Criteria.Contains(existing.AcceptanceCriteria, StringComparer.Ordinal)))
                problems.Add($"Task {id} has an authored verification obligation citing a replaced acceptance criterion. Preserve that obligation and reconcile its accepted criterion before re-import.");
            var authoredIds = authored.Select(obligation => obligation.Id).ToHashSet(StringComparer.Ordinal);
            var removedIds = priorGenerated?.Where(prior => !existing!.VerificationObligations.Any(obligation => obligation.Id == prior.Id))
                .Select(prior => prior.Id).ToHashSet(StringComparer.Ordinal) ?? [];
            item = item with
            {
                VerificationObligations = generated.Where(obligation => !authoredIds.Contains(obligation.Id) && !removedIds.Contains(obligation.Id))
                    .Concat(authored).ToArray(),
            };
            items.Add(item);
        }

        errors = problems;
        return items;

        string[] InstanceRequirements(TaskInstance instance)
        {
            var assigned = instance.Definition.CreationPolicy == "always" ? allRequirements
                : RequirementIds(spec, instance.Definition.Category, instance.FrontendType, instance.Definition.TriggerTerms.ToArray());
            if (instance.StoryId is not null)
                return assigned.Where(id => spec.Requirements.Any(requirement => requirement.Id == id && requirement.StoryId == instance.StoryId)).ToArray();
            return IsStoryScopedCategory(instance.Definition.Category) && instances.Any(candidate => candidate.Definition.Key == instance.Definition.Key && candidate.StoryId is not null)
                ? assigned.Where(id => spec.Requirements.Any(requirement => requirement.Id == id && requirement.StoryId is null)).ToArray() : assigned;
        }
    }

    private static ImportedTaskLists GeneratedLists(FeatureSpec spec, string category, string? storyId)
    {
        IReadOnlyList<string> negative = spec.Exclusions.Count > 0 ? spec.Exclusions
            : ["Do not expand scope beyond accepted impacts and the imported feature specification."];
        if (spec.PublicEndpoints && PublicEndpointPolicyCategory(category))
            negative = negative.Concat(PublicEndpointNegativeCriteria).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var outputs = RequiredOutputs(category);
        if (category == "verification" && !RequiresTestCatalogue(spec))
            outputs = outputs.Where(output => !output.StartsWith("test-cases.", StringComparison.Ordinal)).ToArray();
        return new(outputs.Select(output => storyId is null ? output : $"{storyId}: {output}").ToArray(), negative);
    }

    private static bool RequiresTestCatalogue(FeatureSpec spec)
        => spec.Data || spec.Lifecycle || spec.Integration || spec.Requirements.Any(requirement =>
            requirement.Frontend || requirement.HasContract
            || MatchesCategory(requirement, "security", [])
            || ContainsAny(PositiveCapabilityText($"{requirement.Text} {requirement.AcceptanceCriteria}"),
                "manual case", "manual test", "test catalogue", "test catalog"));

    private static IReadOnlyList<string> ReconcileImportedList(IReadOnlyList<string>? existing,
        IReadOnlyList<string>? priorGenerated, IReadOnlyList<string> generated)
    {
        if (existing is null) return generated;
        if (priorGenerated is null || priorGenerated.SequenceEqual(generated, StringComparer.Ordinal)) return existing;
        // Retain the authored sequence and removals. Replace only known generated
        // entries, anchoring new entries where the generated portion last occurred.
        var retained = new List<string>();
        var insertion = -1;
        foreach (var value in existing)
        {
            if (priorGenerated.Contains(value, StringComparer.Ordinal))
            {
                if (generated.Contains(value, StringComparer.Ordinal)) retained.Add(value);
                insertion = retained.Count;
            }
            else retained.Add(value);
        }
        var additions = generated.Where(value => !priorGenerated.Contains(value, StringComparer.Ordinal)
            && !retained.Contains(value, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
        retained.InsertRange(insertion < 0 ? retained.Count : insertion, additions);
        return retained;
    }

    private static string ShortIdentityHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];

    private static string TaskKeySuffix(string key)
    {
        var value = Regex.Replace(key.ToUpperInvariant(), "[^A-Z0-9]+", "-").Trim('-');
        return value.Length <= 36 ? value : value[^36..].Trim('-');
    }

    private static string FrontendTypeSuffix(string frontendType) => frontendType switch
    {
        "public" => "PUBLIC",
        "customer" => "CUSTOMER",
        "backoffice" => "BACKOFFICE",
        _ => TaskKeySuffix(frontendType),
    };

    private static bool IsFrontendCategory(string category) => category is "wireframe" or "design" or "frontend";
    private static string Identity(string taskTypeKey, string? frontendType, string? storyId) => $"{taskTypeKey}|{frontendType ?? "none"}|{storyId ?? "shared"}";
    private static bool IsStoryScopedCategory(string category) => category is not ("coordination" or "documentation" or "wireframe" or "design" or "security" or "verification" or "assurance" or "delivery");
    private static bool IsNonCodeCategory(string category) => category is "coordination" or "documentation" or "wireframe" or "design" or "assurance" or "delivery";

    internal static IReadOnlyList<Cis.Abstractions.CisTaskVerificationObligation> DefaultVerificationObligations(PlanWorkItem task, IReadOnlyList<string> nativeGates)
    {
        var native = !IsNonCodeCategory(task.Category) && nativeGates.Count > 0;
        return [new("task-contract", [task.AcceptanceCriteria], task.Validation, native ? "native" : "review", [task.Validation])
        { BasisReferences = task.RequirementIds, SharedGateIds = native ? nativeGates : [] }];
    }

    private static bool SameObligation(Cis.Abstractions.CisTaskVerificationObligation left,
        Cis.Abstractions.CisTaskVerificationObligation right)
        => left.Id == right.Id && left.Description == right.Description && left.EvidenceRoute == right.EvidenceRoute
            && left.Criteria.SequenceEqual(right.Criteria, StringComparer.Ordinal)
            && left.EvidenceExpectations.SequenceEqual(right.EvidenceExpectations, StringComparer.Ordinal)
            && left.BasisReferences.SequenceEqual(right.BasisReferences, StringComparer.Ordinal)
            && left.SharedGateIds.SequenceEqual(right.SharedGateIds, StringComparer.Ordinal);

    private static bool IsApplicable(Cis.Abstractions.CisTaskTypeDefinition definition, FeatureSpec spec)
    {
        if (definition.CreationPolicy.Equals("always", StringComparison.OrdinalIgnoreCase)) return true;
        if (definition.Category is "wireframe" or "design" or "frontend") return spec.Frontend;
        if (spec.PublicEndpoints && definition.Category is "security" or "contract" or "backend" or "observability") return true;
        if (definition.Category == "data") return spec.Data;
        if (definition.Category == "database-migration") return spec.SchemaMigration;
        if (definition.Category == "contract") return spec.Api;
        if (definition.Category == "backend") return spec.Backend;
        if (definition.Category == "integration") return spec.Integration;
        if (definition.Category == "lifecycle") return spec.Lifecycle;
        if (definition.Category == "security")
            return spec.Requirements.Any(requirement => MatchesCategory(requirement, definition.Category, definition.TriggerTerms));
        if (definition.Category == "infrastructure" && ExplicitNoInfrastructureChange(spec)) return false;
        if (definition.Category is "infrastructure" or "observability")
            return spec.Requirements.Any(requirement => MatchesCategory(
                requirement, definition.Category, definition.TriggerTerms));
        if (definition.Category is "data-backfill" or "rollout")
        {
            var requirementSignals = string.Join(' ', spec.Requirements.Select(PositiveRequirement).Select(requirement =>
                $"{requirement.Surface} {requirement.Text} {requirement.AcceptanceCriteria}"));
            return definition.TriggerTerms.Any(term =>
                requirementSignals.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        return definition.TriggerTerms.Any(term => spec.SignalText.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ExplicitNoFrontendChange(IReadOnlyDictionary<string, string> sections)
    {
        var text = SectionText(sections, "non-goal", "exclusion", "ux", "screen", "accessibility");
        return ContainsAny(text,
            "no material ui",
            "no user-visible change",
            "no product screen",
            "no wireframe or visual design",
            "no wireframe or visual-design",
            "no screen, wireframe, or visual design",
            "no screen, wireframe, or visual-design");
    }

    private static bool ExplicitNoDataChange(IReadOnlyDictionary<string, string> sections)
    {
        var text = SectionText(sections, "non-goal", "exclusion", "domain model", "data", "migration");
        return ContainsAny(text,
            "no product entity",
            "no product-domain entity",
            "no data model change",
            "no persistence change");
    }

    private static bool ExplicitNoInfrastructureChange(FeatureSpec spec)
    {
        var text = Regex.Replace(SectionText(spec.Sections, "non-goal", "out of scope", "exclusion", "infrastructure"), @"\s+", " ",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return ContainsAny(text,
            "no infrastructure change",
            "no infrastructure topology change",
            "no topology change",
            "no new runtime container")
            || Regex.IsMatch(text,
                @"\bno\b[^.\r\n]{0,220}\b(?:infrastructure topology change|new runtime container)\b[^.\r\n]{0,40}\b(?:planned|required|introduced)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
    }

    private static string SectionText(IReadOnlyDictionary<string, string> sections, params string[] headings)
        => string.Join(' ', sections
            .Where(section => headings.Any(heading => section.Key.Contains(heading, StringComparison.OrdinalIgnoreCase)))
            .Select(section => section.Value))
            .ToLowerInvariant();

    private static bool PublicEndpointPolicyCategory(string category)
        => category is "security" or "contract" or "backend" or "observability" or "verification" or "assurance";

    private static readonly string[] PublicEndpointNegativeCriteria =
    [
        "PUBLIC-ENDPOINT-CACHE: Do not serve an unauthenticated public application endpoint through an uncached response path.",
        "PUBLIC-ENDPOINT-CACHE: Do not let a public application route, endpoint handler, controller, or transport adapter query a database or repository directly, including on cache miss.",
    ];

    private static string PublicEndpointAcceptance(string category) => category switch
    {
        "contract" => "PUBLIC-ENDPOINT-CACHE: Every unauthenticated public application operation defines cache key/vary dimensions, TTL and freshness, invalidation, HTTP cache semantics, and safe failure behavior.",
        "backend" => "PUBLIC-ENDPOINT-CACHE: Every public application response traverses the governed cache abstraction; cache population is delegated behind an application/query boundary and the public handler has no direct database or repository dependency.",
        "security" => "PUBLIC-ENDPOINT-CACHE: Cache keys and payloads prevent cross-tenant, personalized, sensitive, poisoned, or unbounded public cache exposure.",
        "observability" => "PUBLIC-ENDPOINT-CACHE: Cache hit, miss, refresh, stale, eviction, latency, and failure behavior are observable without exposing sensitive key or payload data.",
        "verification" or "assurance" => "PUBLIC-ENDPOINT-CACHE: Tests prove cache use, cache-control behavior, safe miss/failure handling, and the absence of a direct database/repository dependency from the unauthenticated endpoint boundary.",
        _ => "PUBLIC-ENDPOINT-CACHE: The public endpoint caching and database-isolation policy is satisfied.",
    };

    private static string PublicEndpointValidation(string category) => category switch
    {
        "contract" => "Validate response-cache headers and key/vary behavior for hit, miss, refresh, stale, and invalidation cases.",
        "backend" => "Run architecture/dependency tests rejecting database or repository access from public endpoint handlers, plus cache hit/miss/stampede/failure tests.",
        "security" => "Run cache-poisoning, sensitive-data, tenant/locale/vary-key, enumeration, and invalidation security tests.",
        "observability" => "Validate cache metrics, traces, alerts, redaction, and an operational cache-bypass/failure drill.",
        "verification" => "Verify cache-path coverage and the public boundary's database isolation.",
        _ => "Independently verify cache-path coverage and the public boundary's database isolation.",
    };

    private static IReadOnlyList<string> ResolveDependencies(
        Cis.Abstractions.CisTaskTypeDefinition definition,
        string? frontendType,
        string? storyId,
        IReadOnlyList<Cis.Abstractions.CisTaskTypeDefinition> applicable,
        IReadOnlyDictionary<string, (string? FrontendType, string? StoryId, string Id)[]> idsByType,
        IReadOnlyList<string> retained,
        List<string> errors)
    {
        var keys = new List<string>(definition.DependsOnTypeKeys);
        if (idsByType.ContainsKey("core.design.visual") && definition.Category is not ("coordination" or "wireframe" or "design"))
        {
            keys.Add("core.design.visual");
        }

        void AddIf(string key)
        {
            if (idsByType.ContainsKey(key)) keys.Add(key);
        }

        switch (definition.Category)
        {
            case "security": AddIf("core.documentation.contracts"); break;
            case "data": AddIf("core.documentation.contracts"); AddIf("core.security.permissions"); break;
            case "database-migration": AddIf("core.data.persistence"); break;
            case "data-backfill": AddIf("core.data.database-migration"); break;
            case "contract": AddIf("core.documentation.contracts"); AddIf("core.security.permissions"); AddIf("core.data.persistence"); break;
            case "backend": AddIf("core.api.contract"); AddIf("core.data.persistence"); AddIf("core.security.permissions"); break;
            case "frontend": AddIf("core.api.contract"); AddIf("core.security.permissions"); break;
            case "integration": AddIf("core.backend.behavior"); AddIf("core.frontend.implementation"); AddIf("core.api.contract"); break;
            case "infrastructure": AddIf("core.integration.handoff"); break;
            case "observability": AddIf("core.backend.behavior"); AddIf("core.infrastructure.deployment"); break;
            case "lifecycle": AddIf("core.backend.behavior"); AddIf("core.data.persistence"); break;
            case "rollout": AddIf("core.infrastructure.deployment"); AddIf("core.operations.observability"); break;
            case "verification":
                keys.AddRange(applicable.Where(candidate => candidate.Order > 20 && candidate.Order < definition.Order)
                    .Select(candidate => candidate.Key));
                break;
            case "delivery":
                keys.AddRange(applicable.Where(candidate =>
                        candidate.Key != definition.Key
                        && candidate.Key != "core.coordination.scope-guard")
                    .Select(candidate => candidate.Key));
                break;
        }

        var allIds = idsByType.Values.SelectMany(value => value).Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        var result = retained.Where(allIds.Contains).ToHashSet(StringComparer.Ordinal);
        foreach (var key in keys.Distinct(StringComparer.OrdinalIgnoreCase).Where(idsByType.ContainsKey))
        {
            var candidates = idsByType[key].Where(candidate => frontendType is null || candidate.FrontendType is null
                || candidate.FrontendType.Equals(frontendType, StringComparison.OrdinalIgnoreCase)).ToArray();
            // Existing feature-wide assurance and the global design barrier
            // deliberately cover all declared instances at their shared scope.
            if (definition.Category is "verification" or "delivery" || key == "core.design.visual" && frontendType is null)
            {
                result.UnionWith(candidates.Select(candidate => candidate.Id));
                continue;
            }
            var explicitMatches = candidates.Where(candidate => result.Contains(candidate.Id)).ToArray();
            if (explicitMatches.Length > 0) continue;
            var scoped = candidates.Where(candidate => candidate.StoryId == storyId).ToArray();
            if (scoped.Length == 0) scoped = candidates.Where(candidate => candidate.StoryId is null).ToArray();
            if (scoped.Length == 1) result.Add(scoped[0].Id);
            else if (scoped.Length > 1)
                errors.Add($"Task type '{definition.Key}' in story '{storyId ?? "shared"}' has ambiguous prerequisite type '{key}': {string.Join(", ", scoped.Select(candidate => candidate.Id))}. Retain an exact task-instance dependency before re-import.");
            else if (candidates.Length > 0)
                errors.Add($"Task type '{definition.Key}' in story '{storyId ?? "shared"}' cannot infer prerequisite '{key}' from other stories. Declare its exact shared or sibling task dependency.");
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> RequiredOutputs(string category) => category switch
    {
        "coordination" => ["Approved dependency ledger", "Coverage and gate inventory", "Final child disposition"],
        "wireframe" => ["wireframes.md", "Validated screen/action/path coverage", "Exact digest bound to design review"],
        "design" => ["Self-contained Sharp/SVG .mjs renderer", "PNG manifest", "Combined wireframe/design approval record"],
        "documentation" => ["Updated canonical specifications, contracts, decisions, and references"],
        "verification" => ["verification.md evidence ledger", "test-cases.md manual test catalogue with automated-test traceability", "test-cases.csv test-management import", "Requirement and negative-criteria coverage"],
        "assurance" => ["Independent findings and disposition", "Residual-risk record"],
        "delivery" => ["Planned-versus-actual report", "Reproducible handoff evidence"],
        _ => [$"Completed {category} artifacts", "Reproducible validation evidence"],
    };

    private static string RenderTask(
        FeatureSpec spec,
        PlanSource source,
        PlanWorkItem item,
        IReadOnlyList<PlanWorkItem> allItems,
        IReadOnlyList<ImpactFinding> accepted,
        IReadOnlyList<Cis.Abstractions.CisTaskVerificationObligation> generatedObligations)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"title: {JsonSerializer.Serialize(item.Id + " " + item.Title)}");
        builder.AppendLine("type: agent-task");
        builder.AppendLine("status: Draft");
        builder.AppendLine($"task_status: {(item.Status == "decomposed" ? "Decomposed" : "NotStarted")}");
        builder.AppendLine($"task_id: {item.Id}");
        builder.AppendLine($"task_type: {item.TaskTypeKey}");
        builder.AppendLine($"task_type_version: {item.TaskTypeVersion}");
        builder.AppendLine($"parent_id: {item.ParentId ?? "none"}");
        builder.AppendLine($"category: {item.Category}");
        builder.AppendLine($"complexity: {item.Complexity}");
        builder.AppendLine($"feature_spec_path: {JsonSerializer.Serialize(source.Path)}");
        builder.AppendLine($"feature_spec_sha256: {JsonSerializer.Serialize(source.Sha256)}");
        builder.AppendLine($"requirement_ids: {JsonSerializer.Serialize(item.RequirementIds)}");
        builder.AppendLine($"impact_ids: {JsonSerializer.Serialize(item.ImpactIds)}");
        builder.AppendLine($"depends_on: {JsonSerializer.Serialize(item.DependsOn)}");
        builder.AppendLine($"approval_gate: {item.ApprovalGate}");
        builder.AppendLine($"frontend_type: {item.FrontendType ?? "not-applicable"}");
        builder.AppendLine($"story_id: {JsonSerializer.Serialize(item.StoryId)}");
        builder.AppendLine($"instance_key: {JsonSerializer.Serialize(item.InstanceKey)}");
        builder.AppendLine($"work_owner_task_id: {JsonSerializer.Serialize(item.WorkOwnerTaskId)}");
        builder.AppendLine($"targets: {JsonSerializer.Serialize(item.Targets ?? [])}");
        builder.AppendLine($"decision_ids: {JsonSerializer.Serialize(item.DecisionIds ?? [])}");
        builder.AppendLine($"verification_obligations: {JsonSerializer.Serialize(item.VerificationObligations)}");
        var importedLists = GeneratedLists(spec, item.Category, item.StoryId);
        builder.AppendLine($"imported_required_outputs: {JsonSerializer.Serialize(importedLists.RequiredOutputs)}");
        builder.AppendLine($"imported_negative_criteria: {JsonSerializer.Serialize(importedLists.NegativeCriteria)}");
        builder.AppendLine($"imported_verification_obligations: {JsonSerializer.Serialize(generatedObligations)}");
        builder.AppendLine("authority: human-approved-plan");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine($"# {item.Id}: {item.Title}");
        builder.AppendLine();
        builder.AppendLine("## Objective");
        builder.AppendLine();
        builder.AppendLine(item.AcceptanceCriteria);
        if (item.FrontendType is not null)
        {
            builder.AppendLine($"This task is bounded to the `{item.FrontendType}` frontend surface.");
        }
        builder.AppendLine();
        builder.AppendLine("## Required changes");
        builder.AppendLine();
        foreach (var line in RequiredChanges(spec, item))
        {
            builder.AppendLine($"- {line}");
        }

        builder.AppendLine();
        builder.AppendLine("## Required outputs");
        builder.AppendLine();
        foreach (var output in item.RequiredOutputs ?? [])
        {
            builder.AppendLine($"- {output}");
        }

        if (item.Category == "verification")
        {
            builder.AppendLine();
            builder.AppendLine("## Structured test-obligation matrix");
            builder.AppendLine();
            builder.AppendLine("| Obligation | Evidence route | Accepted basis and criteria | Required evidence |");
            builder.AppendLine("|---|---|---|---|");
            foreach (var obligation in item.VerificationObligations)
                builder.AppendLine($"| {MarkdownCell(obligation.Id)} | {MarkdownCell(obligation.EvidenceRoute)} | {MarkdownCell(string.Join("; ", obligation.BasisReferences.Concat(obligation.Criteria)))} | {MarkdownCell(obligation.Description)}; {MarkdownCell(string.Join("; ", obligation.EvidenceExpectations))}{(obligation.SharedGateIds.Count == 0 ? "" : "; shared gates: " + MarkdownCell(string.Join(", ", obligation.SharedGateIds)))} |");
            builder.AppendLine();
            builder.AppendLine("These rows are the persisted task verification obligations, not an additional keyword-selected gate set. Applicable adopted completion policy remains binding at its declared task, feature or release boundary. Failed, missing, stale and skipped required evidence block completion; justified inapplicability is not a pass. A release obligation does not itself require a new release campaign for each story.");
        }

        builder.AppendLine();
        builder.AppendLine("## Constraints and exclusions");
        builder.AppendLine();
        foreach (var exclusion in (item.NegativeCriteria ?? spec.Exclusions).DefaultIfEmpty("Do not expand scope beyond the accepted impacts and imported feature specification."))
        {
            builder.AppendLine($"- {CleanLine(exclusion)}");
        }

        builder.AppendLine();
        builder.AppendLine("## Context and evidence");
        builder.AppendLine();
        builder.AppendLine($"- Canonical feature specification: `{source.Path}` (`{source.Sha256}`).");
        foreach (var reference in source.References)
        {
            builder.AppendLine($"- Governed reference: `{reference}`.");
        }
        foreach (var finding in accepted.Where(finding => item.ImpactIds.Contains(finding.Id, StringComparer.Ordinal)))
        {
            builder.AppendLine($"- Accepted impact `{finding.Id}`: `{finding.Target}` - {CleanLine(finding.Label)}.");
        }

        builder.AppendLine();
        builder.AppendLine("## Dependencies and approval gates");
        builder.AppendLine();
        if (item.DependsOn.Count == 0)
        {
            builder.AppendLine("- No task dependencies.");
        }
        else
        {
            foreach (var dependency in item.DependsOn)
            {
                var title = allItems.First(candidate => candidate.Id == dependency).Title;
                builder.AppendLine($"- `{dependency}` - {title}.");
            }
        }
        if (item.Category == "design")
        {
            builder.AppendLine("- Human approval gate: the Sharp/SVG renderer and PNG manifest must be recorded in `../design.md` against the approved guideline digest.");
            builder.AppendLine("- Global delivery barrier: when the design enters ReadyForReview, all non-review work stops until explicit approval. Rejection permits only wireframe/design revision.");
        }
        if (item.Category == "frontend")
        {
            builder.AppendLine("- Production implementation is blocked until `../design.md` records explicit approval and approved asset paths.");
        }
        if (item.ApprovalGate != "none" && item.Category != "design")
        {
            builder.AppendLine($"- Required approval gate: `{item.ApprovalGate}`.");
        }

        builder.AppendLine();
        builder.AppendLine("## Acceptance criteria");
        builder.AppendLine();
        builder.AppendLine($"- [ ] {item.AcceptanceCriteria}");
        if (IncludesOutcomeAcceptance(item.Category))
        {
            foreach (var requirement in spec.Requirements.Where(requirement =>
                         item.RequirementIds.Contains(requirement.Id, StringComparer.OrdinalIgnoreCase)))
            {
                builder.AppendLine($"- [ ] `{requirement.Id}`: {CleanLine(requirement.AcceptanceCriteria)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Targeted validation");
        builder.AppendLine();
        foreach (var check in ValidationChecks(item.Category))
        {
            builder.AppendLine($"- [ ] {check}");
        }
        builder.AppendLine($"- [ ] {item.Validation}");
        if (item.VerificationObligations.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Acceptance verification");
            builder.AppendLine();
            foreach (var obligation in item.VerificationObligations)
                builder.AppendLine($"- `{obligation.Id}` ({obligation.EvidenceRoute}): {obligation.Description} Criteria: {string.Join("; ", obligation.Criteria)} Evidence: {string.Join("; ", obligation.EvidenceExpectations)}");
        }

        builder.AppendLine();
        builder.AppendLine("## Completion evidence");
        builder.AppendLine();
        builder.AppendLine("| Check | Command or artifact | Result | Notes |");
        builder.AppendLine("| --- | --- | --- | --- |");
        builder.AppendLine("| TODO | TODO | Not run | Record exact reproducible evidence. |");
        builder.AppendLine();
        builder.AppendLine("## Deferrals and residual risk");
        builder.AppendLine();
        builder.AppendLine("- None. Any deferral must identify the unmet criterion, reason, owner, follow-up, and approval.");
        builder.AppendLine();
        builder.AppendLine("## External issue links");
        builder.AppendLine();
        builder.AppendLine("| Provider | Remote ID | URL | Canonical digest | Remote digest | Last synchronized UTC | State |");
        builder.AppendLine("|---|---|---|---|---|---|---|");
        builder.AppendLine();
        builder.AppendLine("## External synchronization decisions");
        builder.AppendLine();
        builder.AppendLine("| Provider | Decision | Reviewer | Timestamp UTC | Rationale |");
        builder.AppendLine("|---|---|---|---|---|");
        return builder.ToString();
    }

    private static ManualTestCaseArtifacts BuildManualTestCases(
        string changeId,
        FeatureSpec spec,
        PlanSource source,
        IReadOnlyDictionary<string, IReadOnlyList<string>> automationReferences)
    {
        var cases = spec.Requirements.Select(requirement =>
        {
            var idPart = Regex.Replace(requirement.Id.ToUpperInvariant(), "[^A-Z0-9]+", "-").Trim('-');
            var id = $"TC-{idPart}-001";
            var title = TestTitle(requirement.Text);
            var section = requirement.FrontendType is not null
                ? $"Frontend / {TitleCase(requirement.FrontendType)}"
                : string.IsNullOrWhiteSpace(requirement.Surface)
                    ? "General"
                    : TitleCase(requirement.Surface!);
            var type = TestType(requirement);
            var priority = TitleCase(requirement.Complexity);
            var preconditions = $"The feature build matching {source.Sha256} is deployed in a manual-test environment. "
                + $"The tester has the actor, data, configuration, and permissions needed for {requirement.Id}.";
            var steps = TestSteps(requirement);
            var references = automationReferences.TryGetValue(id, out var found) ? found : [];
            return new ManualTestCase(id, title, section, priority, type, preconditions, steps,
                CleanLine(requirement.AcceptanceCriteria), requirement.Id,
                requirement.FrontendType ?? "not-applicable",
                references.Count > 0 ? "Automated" : "Pending",
                references);
        }).ToArray();

        var csv = RenderManualTestCasesCsv(cases);
        var csvSha256 = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(csv))).ToLowerInvariant();
        var markdown = RenderManualTestCasesMarkdown(changeId, spec, source, cases, csvSha256);
        return new(markdown, csv, cases.Length, cases.Count(test => test.AutomationStatus == "Automated"));
    }

    private static string RenderManualTestCasesMarkdown(
        string changeId,
        FeatureSpec spec,
        PlanSource source,
        IReadOnlyList<ManualTestCase> cases,
        string csvSha256)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"title: {JsonSerializer.Serialize(changeId + " manual test cases")}");
        builder.AppendLine("type: manual-test-cases");
        builder.AppendLine("status: Draft");
        builder.AppendLine($"change_id: {changeId}");
        builder.AppendLine($"feature_spec_path: {JsonSerializer.Serialize(source.Path)}");
        builder.AppendLine($"feature_spec_sha256: {JsonSerializer.Serialize(source.Sha256)}");
        builder.AppendLine($"test_case_count: {cases.Count}");
        builder.AppendLine($"automated_test_case_count: {cases.Count(test => test.AutomationStatus == "Automated")}");
        builder.AppendLine($"automation_pending_count: {cases.Count(test => test.AutomationStatus == "Pending")}");
        builder.AppendLine("csv_path: test-cases.csv");
        builder.AppendLine($"csv_sha256: {JsonSerializer.Serialize(csvSha256)}");
        builder.AppendLine("generation: deterministic");
        builder.AppendLine("authority: derived");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("# Manual test cases");
        builder.AppendLine();
        builder.AppendLine($"Generated from `{source.Path}`. Regenerate through feature planning after the source specification changes; do not record execution results in this derived catalogue.");
        builder.AppendLine();
        builder.AppendLine("The companion `test-cases.csv` uses one row per case and portable field names that can be mapped during import into TestRail or another test-management system.");
        builder.AppendLine();
        builder.AppendLine("## Coverage summary");
        builder.AppendLine();
        builder.AppendLine("| Test case | Title | Section | Priority | Type | Requirement | Frontend type | Automation |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var test in cases)
        {
            builder.AppendLine($"| {MarkdownCell(test.Id)} | {MarkdownCell(test.Title)} | {MarkdownCell(test.Section)} | {MarkdownCell(test.Priority)} | {MarkdownCell(test.Type)} | {MarkdownCell(test.RequirementId)} | {MarkdownCell(test.FrontendType)} | {MarkdownCell(test.AutomationStatus)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Test case definitions");
        foreach (var test in cases)
        {
            builder.AppendLine();
            builder.AppendLine($"### {test.Id}: {test.Title}");
            builder.AppendLine();
            builder.AppendLine($"- Requirement: `{test.RequirementId}`");
            builder.AppendLine($"- Section: {test.Section}");
            builder.AppendLine($"- Priority: {test.Priority}");
            builder.AppendLine($"- Type: {test.Type}");
            builder.AppendLine($"- Frontend type: `{test.FrontendType}`");
            builder.AppendLine($"- Automation status: {test.AutomationStatus}");
            builder.AppendLine($"- Automated test references: {(test.AutomatedTestReferences.Count == 0 ? "None" : string.Join("; ", test.AutomatedTestReferences.Select(reference => $"`{reference}`")))}");
            builder.AppendLine();
            builder.AppendLine("#### Preconditions");
            builder.AppendLine();
            builder.AppendLine(test.Preconditions);
            builder.AppendLine();
            builder.AppendLine("#### Steps");
            builder.AppendLine();
            foreach (var step in test.Steps) builder.AppendLine($"1. {step}");
            builder.AppendLine();
            builder.AppendLine("#### Expected result");
            builder.AppendLine();
            builder.AppendLine(test.ExpectedResult);
        }
        return builder.ToString();
    }

    private static string RenderManualTestCasesCsv(IReadOnlyList<ManualTestCase> cases)
    {
        var rows = new List<string>
        {
            string.Join(',', new[]
            {
                "ID", "Title", "Section", "Priority", "Type", "Preconditions", "Steps",
                "Expected Result", "References", "Frontend Type", "Automation Status", "Automated Test References",
            }.Select(CsvCell)),
        };
        rows.AddRange(cases.Select(test => string.Join(',', new[]
        {
            test.Id, test.Title, test.Section, test.Priority, test.Type, test.Preconditions,
            string.Join("\n", test.Steps.Select((step, index) => $"{index + 1}. {step}")),
            test.ExpectedResult, test.RequirementId, test.FrontendType, test.AutomationStatus,
            string.Join("\n", test.AutomatedTestReferences),
        }.Select(CsvCell))));
        return string.Join("\r\n", rows) + "\r\n";
    }

    private static IReadOnlyList<string> TestSteps(FeatureRequirement requirement)
    {
        var open = requirement.Frontend
            ? $"Open the approved {requirement.FrontendType ?? "customer"} entry point for requirement {requirement.Id}."
            : requirement.HasContract
                ? $"Use the supported application or API client to reach the boundary governed by {requirement.Id}."
                : $"Prepare the supported product or operational boundary governed by {requirement.Id}.";
        return
        [
            open,
            $"Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.",
            $"Perform the required behavior: {CleanLine(requirement.Text)}",
            "Observe the resulting user-visible, API, persistence, and operational state that applies.",
        ];
    }

    private static string TestTitle(string requirement)
    {
        var title = CleanLine(requirement);
        title = Regex.Replace(title, @"^(?:the system|the application|the user|a user)\s+(?:shall|must|can)\s+", string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (title.Length > 120) title = title[..117].TrimEnd() + "...";
        return title.Length == 0 ? "Verify governed requirement" : char.ToUpperInvariant(title[0]) + title[1..];
    }

    private static string TestType(FeatureRequirement requirement)
    {
        var signal = $"{requirement.Surface} {requirement.Text} {requirement.AcceptanceCriteria}".ToLowerInvariant();
        if (ContainsAny(signal, "security", "permission", "authorization", "forbidden", "sensitive")) return "Security";
        if (ContainsAny(signal, "accessibility", "keyboard", "screen reader", "wcag")) return "Accessibility";
        if (ContainsAny(signal, "migration", "schema", "database", "persistence")) return "Data and persistence";
        if (requirement.HasContract) return "API and contract";
        if (requirement.Frontend) return "User interface";
        return "Functional";
    }

    private static string CsvCell(string value)
    {
        var safe = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var first = safe.TrimStart().FirstOrDefault();
        if (first is '=' or '+' or '-' or '@') safe = "'" + safe;
        return $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string MarkdownCell(string value) => value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();
    private static string TitleCase(string value) => string.Join(' ', value.Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));

    private static bool IncludesOutcomeAcceptance(string category)
        => category is not ("documentation" or "security" or "data" or "database-migration" or "data-backfill" or "contract" or "backend" or "observability" or "rollout");

    private static IReadOnlyList<string> RequiredChanges(FeatureSpec spec, PlanWorkItem item)
    {
        if (item.Id == "WORK-000")
        {
            return spec.Requirements.Select(requirement => $"Preserve `{requirement.Id}`: {CleanLine(requirement.Text)}").ToArray();
        }

        var keywords = item.Category switch
        {
            "documentation" => new[] { "reference", "contract", "document", "dictionary", "goal" },
            "data" => new[] { "model", "data", "status", "state", "migration", "audit", "category" },
            "database-migration" => new[] { "migration", "schema", "table", "column", "index", "constraint" },
            "data-backfill" => new[] { "backfill", "existing data", "reindex", "reconcile", "transform" },
            "contract" => new[] { "api", "permission", "contract", "visibility", "security" },
            "security" => new[] { "permission", "security", "visibility", "sensitive", "internal", "authorization" },
            "backend" => new[] { "model", "workflow", "status", "state", "audit", "behavior", "behaviour" },
            "wireframe" or "design" or "frontend" => new[] { "ui", "screen", "view", "detail", "frontend", "accessibility" },
            "integration" => new[] { "handoff", "workflow", "review", "task", "integration", "event" },
            "search" => new[] { "search", "projection", "retrieval", "index" },
            "lifecycle" => new[] { "carry", "conversion", "lifecycle", "archive", "history" },
            "infrastructure" => new[] { "infrastructure", "terraform", "deployment", "configuration", "queue", "secret" },
            "observability" => new[] { "logging", "metric", "trace", "alert", "dashboard", "runbook" },
            "rollout" => new[] { "rollout", "release", "feature flag", "rollback", "canary" },
            "assurance" => new[] { "assurance", "security", "accessibility", "mutation", "architecture" },
            "verification" => new[] { "test", "verification", "acceptance" },
            _ => new[] { "goal", "test", "validation" },
        };
        var classificationScopedFrontend = item.FrontendType is not null
            && item.Category is "wireframe" or "design" or "frontend";
        var sectionLines = classificationScopedFrontend || item.StoryId is not null
            ? []
            : spec.Sections
                .Where(section => IsBehaviorSection(section.Key))
                .Where(section => keywords.Any(keyword => ContainsTerm(section.Key, keyword)))
                .SelectMany(section => ExtractScopeLines(section.Value));
        var lines = sectionLines
            .Concat(spec.Requirements
                .Where(requirement => item.RequirementIds.Contains(requirement.Id, StringComparer.OrdinalIgnoreCase))
                .Select(requirement => $"`{requirement.Id}`: {CleanLine(requirement.Text)}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .ToArray();
        var result = lines.Length > 0 ? lines : ["Implement only the behavior assigned to this bounded workstream."];
        if (spec.PublicEndpoints && PublicEndpointPolicyCategory(item.Category))
        {
            result = result.Concat(PublicEndpointRequiredChanges(item.Category)).ToArray();
        }
        return result;
    }

    private static IReadOnlyList<string> PublicEndpointRequiredChanges(string category) => category switch
    {
        "contract" =>
        [
            "`PUBLIC-ENDPOINT-CACHE`: identify every unauthenticated public application operation and define its cache key/vary dimensions, TTL, freshness/staleness, invalidation, HTTP cache headers, and failure behavior.",
        ],
        "backend" =>
        [
            "`PUBLIC-ENDPOINT-CACHE`: route every unauthenticated public application response through a governed cache abstraction.",
            "`PUBLIC-ENDPOINT-CACHE`: keep database contexts, database clients, query providers, and repositories out of the public route/controller/handler; delegate cache population behind an application/query boundary.",
            "`PUBLIC-ENDPOINT-CACHE`: define stampede protection, bounded cache-miss behavior, refresh, and stale/failure handling.",
        ],
        "security" =>
        [
            "`PUBLIC-ENDPOINT-CACHE`: prove public cached payloads contain no personalized, tenant-restricted, secret, or sensitive data and that every representation dimension participates in the cache key or Vary policy.",
        ],
        "observability" =>
        [
            "`PUBLIC-ENDPOINT-CACHE`: emit redacted hit/miss/refresh/stale/eviction/failure metrics and define alert/runbook behavior.",
        ],
        _ =>
        [
            "`PUBLIC-ENDPOINT-CACHE`: verify cache hits, misses, refresh/invalidation, stampede/failure behavior, cache headers, payload safety, and architecture rules preventing direct database/repository access from the unauthenticated endpoint boundary.",
        ],
    };

    private static IEnumerable<string> ExtractScopeLines(string content)
        => content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n')
            .Select(CleanLine)
            .Where(line => !string.IsNullOrWhiteSpace(line)
                && !line.StartsWith("| ---", StringComparison.Ordinal)
                && line is not "```text" and not "```http" and not "```")
            .Select(line => line.TrimStart('-', '*', ' '));

    private static IReadOnlyList<string> ValidationChecks(string category) => category switch
    {
        "documentation" => ["Strict documentation and front-matter validation pass.", "Affected contract/reference drift checks pass."],
        "data" => ["Model and repository invariants are tested.", "Persistence constraints, concurrency, audit, and retention behavior are tested."],
        "database-migration" => ["Forward, idempotency, compatibility, and rollback-position checks pass."],
        "data-backfill" => ["Dry-run, batching, restart, idempotency, and reconciliation checks pass."],
        "contract" => ["Positive, validation-failure, unauthorized, forbidden, and compatibility cases pass."],
        "security" => ["Positive and prohibited access paths are tested.", "Sensitive data and secrets are not exposed."],
        "backend" => ["Focused domain/application unit tests pass.", "Affected backend integration tests pass."],
        "wireframe" => ["Primary and alternate flows are represented.", "Empty, loading, failure, denied, responsive, and lifecycle states are covered."],
        "design" => ["Rendered assets cover required states and are nonblank.", "Human design approval is recorded with approver and date."],
        "frontend" => ["Component and interaction tests pass.", "Accessibility, lint, typecheck, and production build results are recorded."],
        "integration" => ["Cross-module links, transitions, idempotency, and failure behavior are tested."],
        "search" => ["Projection/index lifecycle tests pass.", "Prohibited retrieval paths are proven negative by tests."],
        "lifecycle" => ["Transition, carry-forward, provenance, audit, visibility, and non-duplication tests pass."],
        "infrastructure" => ["Formatting, validation, policy, plan, smoke, and rollback evidence is recorded."],
        "observability" => ["Telemetry, redaction, alert, dashboard, runbook, and failure-detection evidence is recorded."],
        "rollout" => ["Activation, compatibility, rollback, communication, and post-release criteria are tested."],
        "assurance" => ["Independent findings, disposition, rework, and residual risk are recorded."],
        "verification" => [],
        _ => ["Final repository completion and planned-versus-actual checks are recorded."],
    };

    private static FeatureParseResult Parse(string repositoryPath, string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath) || Path.IsPathRooted(requestedPath))
        {
            return new(null, ["Feature specification path must be repository-relative."]);
        }
        var repository = Path.GetFullPath(repositoryPath);
        var absolute = Path.GetFullPath(Path.Combine(repository, requestedPath.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!absolute.StartsWith(repository + Path.DirectorySeparatorChar, comparison)
            || !File.Exists(absolute)
            || !Path.GetExtension(absolute).Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            return new(null, ["Feature specification must be an existing Markdown file inside the repository."]);
        }
        var relative = Path.GetRelativePath(repository, absolute).Replace('\\', '/');
        if (relative.StartsWith(".cis/local/", StringComparison.OrdinalIgnoreCase))
        {
            return new(null, ["Derived .cis/local files cannot be imported as feature specifications."]);
        }

        var content = File.ReadAllText(absolute);
        var frontMatter = ParseFrontMatter(content);
        var type = frontMatter.Scalars.GetValueOrDefault("type");
        var title = frontMatter.Scalars.GetValueOrDefault("title") ?? Path.GetFileNameWithoutExtension(relative);
        if (!string.Equals(type, "feature-specification", StringComparison.OrdinalIgnoreCase)
            && !(string.Equals(type, "specification", StringComparison.OrdinalIgnoreCase)
                && (title.Contains("feature", StringComparison.OrdinalIgnoreCase)
                    || content.Contains("# " + title, StringComparison.OrdinalIgnoreCase))))
        {
            return new(null, ["Feature specification front matter must declare `type: feature-specification`, or `type: specification` with a feature title."]);
        }

        var sections = ParseSections(content);
        var requirements = ParseStructuredRequirements(content, out var errors).ToList();
        if (requirements.Count == 0)
        {
            requirements.AddRange(ParseNarrativeRequirements(sections));
        }
        if (requirements.Count == 0)
        {
            errors.Add("Feature specification requires a functional-requirements table or a numbered Goals section.");
        }
        if (errors.Count > 0)
        {
            return new(null, errors);
        }

        var targets = frontMatter.Lists.GetValueOrDefault("targets") ?? [];
        var stack = frontMatter.Lists.GetValueOrDefault("stack") ?? [];
        var references = frontMatter.Lists.GetValueOrDefault("references") ?? [];
        var targetRoles = ReadTargetRoles(repository, targets);
        var positiveRequirements = requirements.Select(PositiveRequirement).ToArray();
        var requirementSignal = string.Join(' ', positiveRequirements.Select(requirement =>
            $"{requirement.Surface} {requirement.Text} {requirement.AcceptanceCriteria}")).ToLowerInvariant();
        var explicitNoFrontendChange = ExplicitNoFrontendChange(sections);
        var frontendSignal = !explicitNoFrontendChange && (positiveRequirements.Any(requirement => Surface(requirement.Surface, "frontend", "full-stack", "mobile", "native"))
            || targetRoles.Values.SelectMany(value => value).Any(IsFrontendRole));
        var backend = positiveRequirements.Any(requirement => Surface(requirement.Surface, "backend", "full-stack", "api", "data"))
            || targetRoles.Values.SelectMany(value => value).Any(IsBackendRole);
        var data = positiveRequirements.Any(IsPositiveDataRequirement);
        if (data && !positiveRequirements.Any(requirement => Surface(requirement.Surface, "data"))
            && ExplicitNoDataChange(sections))
            data = false;
        var schemaMigration = positiveRequirements.Any(ContainsSchemaMigrationSignal);
        var api = positiveRequirements.Any(requirement => Surface(requirement.Surface, "api", "contract", "full-stack")
            || ContainsApiRequirementSignal(requirement));
        var integration = positiveRequirements.Any(requirement => !Surface(requirement.Surface, "frontend")
            && ContainsIntegrationSignal($" {requirement.Text} {requirement.AcceptanceCriteria}".ToLowerInvariant()));
        var search = ContainsAny(requirementSignal, "search", "projection", "retrieval", "indexing", "document type");
        var lifecycle = positiveRequirements.Any(requirement => !Surface(requirement.Surface, "frontend", "mobile", "native")
                && ContainsLifecycleRequirementSignal(requirement))
            || HasActiveLifecycleSection(sections);
        var classified = requirements
            .Select(requirement => Classify(requirement, frontendSignal, backend, title, targets))
            .Select(requirement => explicitNoFrontendChange ? requirement with { Frontend = false } : requirement)
            .ToArray();
        var frontend = classified.Any(requirement => requirement.Frontend);
        backend |= classified.Any(requirement => requirement.Backend);
        var frontendTypes = classified.Where(requirement => requirement.Frontend)
            .Select(requirement => requirement.FrontendType ?? "customer")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(FrontendTypeOrder)
            .ToArray();
        var publicEndpoints = requirements.Any(IsPublicEndpointRequirement)
            || ContainsAny(requirementSignal, "public application endpoint", "public endpoint", "public api", "unauthenticated endpoint");
        var exclusions = ParseListSection(sections, "non-goal", "out of scope", "exclusion");
        var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        return new(new FeatureSpec(title, type!, relative, hash, requirementSignal, classified, targets, stack, references, sections,
            exclusions, frontend, backend, data, schemaMigration, api, integration, search, lifecycle, frontendTypes,
            publicEndpoints, targetRoles), []);
    }

    private static IReadOnlyList<FeatureRequirement> ParseStructuredRequirements(string content, out List<string> errors)
    {
        errors = [];
        var lines = Normalize(content).Split('\n');
        var headings = new List<(int Level, string Text)>();
        var inFrontMatter = lines.Length > 0 && lines[0].Trim() == "---";
        var inCode = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (inFrontMatter)
            {
                if (index > 0 && line == "---") inFrontMatter = false;
                continue;
            }
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                inCode = !inCode;
                continue;
            }
            if (inCode) continue;
            var heading = Regex.Match(line, @"^(?<level>#{1,6})\s+(?<text>.+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (heading.Success)
            {
                var level = heading.Groups["level"].Length;
                headings.RemoveAll(existing => existing.Level >= level);
                headings.Add((level, heading.Groups["text"].Value));
                continue;
            }
            if (headings.Any(heading => heading.Level > 1 && !IsBehaviorSection(heading.Text))) continue;
            var headers = SplitRow(lines[index]);
            var idAt = Find(headers, "ID");
            var requirementAt = Find(headers, "Requirement");
            var acceptanceAt = Find(headers, "Acceptance criteria");
            var surfaceAt = Find(headers, "Surface", "Affected surfaces");
            var frontendTypeAt = Find(headers, "Frontend type", "Frontend classification", "Audience");
            var storyAt = Find(headers, "Story ID");
            if (idAt < 0 || requirementAt < 0 || acceptanceAt < 0) continue;
            var result = new List<FeatureRequirement>();
            for (var row = index + 2; row < lines.Length && lines[row].TrimStart().StartsWith('|'); row++)
            {
                var cells = SplitRow(lines[row]);
                if (cells.Length <= Math.Max(idAt, Math.Max(requirementAt, acceptanceAt))) continue;
                var id = cells[idAt].Trim();
                var requirement = cells[requirementAt].Trim();
                var acceptance = cells[acceptanceAt].Trim();
                var surface = surfaceAt >= 0 && cells.Length > surfaceAt ? cells[surfaceAt].Trim() : null;
                var frontendType = frontendTypeAt >= 0 && cells.Length > frontendTypeAt ? NormalizeFrontendType(cells[frontendTypeAt]) : null;
                var storyId = storyAt >= 0 ? cells.ElementAtOrDefault(storyAt)?.Trim() : null;
                if (string.IsNullOrWhiteSpace(storyId) || IsNotApplicable(storyId)) storyId = null;
                if (storyId is not null && (!Regex.IsMatch(storyId, @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,79}\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) || IsPlaceholder(storyId)))
                {
                    errors.Add($"Functional requirement '{id}' needs a stable Story ID containing only letters, digits, dots, underscores or hyphens.");
                    continue;
                }
                if (IsPlaceholder(id) || IsPlaceholder(requirement) || IsPlaceholder(acceptance))
                {
                    errors.Add($"Functional requirement row '{id}' must replace all TODO or placeholder values.");
                    continue;
                }
                if (frontendTypeAt >= 0 && !string.IsNullOrWhiteSpace(cells.ElementAtOrDefault(frontendTypeAt))
                    && !IsNotApplicable(cells[frontendTypeAt]) && frontendType is null)
                {
                    errors.Add($"Functional requirement '{id}' has unsupported frontend type '{cells[frontendTypeAt]}'. Use public, customer, or backoffice.");
                    continue;
                }
                result.Add(new FeatureRequirement(id, requirement, acceptance, surface, false, false, false, "medium", frontendType, storyId));
            }
            foreach (var duplicate in result.GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            {
                errors.Add($"Functional requirement ID is duplicated: {duplicate.Key}");
            }
            return result;
        }
        return [];
    }

    private static IEnumerable<FeatureRequirement> ParseNarrativeRequirements(IReadOnlyDictionary<string, string> sections)
    {
        var goals = sections.FirstOrDefault(section => IsBehaviorSection(section.Key)
            && section.Key.Contains("goal", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(goals.Value)) yield break;
        var ordinal = 0;
        foreach (var line in Normalize(goals.Value).Split('\n'))
        {
            var match = NumberedItemPattern().Match(line.Trim());
            if (!match.Success || IsPlaceholder(match.Groups["text"].Value)) continue;
            ordinal++;
            var text = match.Groups["text"].Value.Trim();
            yield return new FeatureRequirement($"GOAL-{ordinal:000}", text, text, null, false, false, false, "medium", null, null);
        }
    }

    private static FeatureRequirement Classify(
        FeatureRequirement requirement,
        bool featureFrontend,
        bool featureBackend,
        string title,
        IReadOnlyList<string> targets)
    {
        var positive = PositiveRequirement(requirement);
        var frontend = Surface(positive.Surface, "frontend", "full-stack", "mobile", "native")
            || IsFrontendTarget(positive.Surface ?? string.Empty)
            || ContainsFrontendSignal(positive.Text);
        var backend = Surface(positive.Surface, "backend", "full-stack", "api", "data")
            || (!frontend && ContainsAny(positive.Text.ToLowerInvariant(), "model", "api", "persist", "service", "event", "permission", "search", "audit"))
            || (!frontend && featureBackend);
        if (!frontend && !backend)
        {
            frontend = featureFrontend && ContainsAnyTerm(positive.Text.ToLowerInvariant(), "display", "show", "action", "navigate");
        }
        var contract = Surface(positive.Surface, "api", "contract", "full-stack")
            || ContainsApiRequirementSignal(positive);
        var score = (frontend && backend ? 2 : 0)
            + (requirement.Text.Length > 180 ? 1 : 0)
            + (ContainsAny(positive.Text.ToLowerInvariant(), "integration", "migration", "security", "permission", "search", "workflow") ? 2 : 0);
        var frontendType = frontend
            ? requirement.FrontendType ?? InferFrontendType($"{title} {requirement.Surface} {requirement.Text} {string.Join(' ', targets)}")
            : null;
        return requirement with
        {
            Frontend = frontend,
            Backend = backend,
            HasContract = contract,
            Complexity = score >= 4 ? "high" : score >= 2 ? "medium" : "low",
            FrontendType = frontendType,
        };
    }

    private static string InferFrontendType(string value)
    {
        var text = value.ToLowerInvariant();
        if (ContainsAny(text, "backoffice", "back-office", "admin", "internal operator", "staff", "operations portal")) return "backoffice";
        if (ContainsAny(text, "public", "anonymous", "visitor", "marketing", "landing page", "unauthenticated")) return "public";
        return "customer";
    }

    private static bool IsPublicEndpointRequirement(FeatureRequirement requirement)
    {
        var accepted = AcceptedRequirement(requirement);
        var text = $" {accepted.Text} {accepted.AcceptanceCriteria}".ToLowerInvariant();
        var explicitEndpoint = Regex.IsMatch(text,
            @"(?i)\b(?:endpoints?|apis?|route\s+handlers?|http)\b|\b(?:get|post|put|patch|delete)\s+/",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (requirement.FrontendType == "public"
            && (Surface(accepted.Surface, "backend", "api", "contract", "full-stack") || explicitEndpoint)) return true;
        return Regex.IsMatch(text,
            @"\b(?:endpoints?|apis?|route\s+handlers?)\b\s+(?:(?:(?:does\s+not|doesn't|need\s+not)\s+require\s+authentication)|(?:(?:shall|must|will)\s+)?(?:(?:be|is|are)\s+(?:available|accessible|served|provided)\s+)?without\s+(?:requiring\s+)?authentication)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static string? NormalizeFrontendType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "public" or "customer" or "backoffice" ? normalized : null;
    }

    private static bool IsNotApplicable(string value) => value.Trim().ToLowerInvariant() is "not-applicable" or "not applicable" or "n/a";

    private static int FrontendTypeOrder(string value) => value switch
    {
        "public" => 0,
        "customer" => 1,
        "backoffice" => 2,
        _ => 3,
    };

    private static FrontMatter ParseFrontMatter(string content)
    {
        var scalars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var lines = Normalize(content).Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---") return new(scalars, lists);
        string? listKey = null;
        var list = new List<string>();
        void Flush()
        {
            if (listKey is not null) lists[listKey] = list.ToArray();
            listKey = null;
            list = [];
        }
        foreach (var line in lines.Skip(1).TakeWhile(line => line.Trim() != "---"))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) && listKey is not null)
            {
                list.Add(Unquote(trimmed[2..].Trim()));
                continue;
            }
            Flush();
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length == 0)
            {
                listKey = key;
            }
            else if (value.StartsWith('[') && value.EndsWith(']'))
            {
                lists[key] = value.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Unquote).ToArray();
            }
            else
            {
                scalars[key] = Unquote(value);
            }
        }
        Flush();
        return new(scalars, lists);
    }

    private static IReadOnlyDictionary<string, string> ParseSections(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? heading = null;
        var excludedNestedLevel = 0;
        var inCode = false;
        var body = new StringBuilder();
        void Flush()
        {
            if (heading is not null) result[NormalizeHeading(heading)] = body.ToString().Trim();
            body.Clear();
        }
        foreach (var line in Normalize(content).Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal) || line.TrimStart().StartsWith("~~~", StringComparison.Ordinal))
            {
                inCode = !inCode;
                continue;
            }
            if (inCode) continue;
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                heading = line[3..].Trim();
                excludedNestedLevel = 0;
            }
            else if (heading is not null)
            {
                var nested = Regex.Match(line, @"^(?<level>#{3,6})\s+(?<text>.+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (nested.Success)
                {
                    var level = nested.Groups["level"].Length;
                    if (level <= excludedNestedLevel) excludedNestedLevel = 0;
                    if (IsBehaviorSection(heading) && !IsBehaviorSection(nested.Groups["text"].Value))
                        excludedNestedLevel = excludedNestedLevel == 0 ? level : excludedNestedLevel;
                }
                if (excludedNestedLevel > 0) continue;
                body.AppendLine(line);
            }
        }
        Flush();
        return result;
    }

    private static IReadOnlyList<string> ParseListSection(
        IReadOnlyDictionary<string, string> sections,
        params string[] headings)
        => sections.Where(section => headings.Any(heading => section.Key.Contains(heading, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(section => Normalize(section.Value).Split('\n'))
            .Select(line => NumberedItemPattern().Replace(line.TrimStart('-', '*', ' '), "${text}").Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("This feature does not", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static string[] RequirementIds(FeatureSpec spec, string category, string? frontendType, params string[] keywords)
    {
        var candidates = frontendType is null
            ? spec.Requirements
            : spec.Requirements.Where(requirement => requirement.Frontend
                && requirement.FrontendType?.Equals(frontendType, StringComparison.OrdinalIgnoreCase) == true).ToArray();
        var matches = candidates.Where(requirement => MatchesCategory(requirement, category, keywords))
            .Select(requirement => requirement.Id).ToArray();
        return matches.Length > 0 ? matches : candidates.Select(item => item.Id).ToArray();
    }

    private static bool MatchesCategory(FeatureRequirement requirement, string category, IReadOnlyList<string> fallbackKeywords)
    {
        requirement = category == "security" ? AcceptedRequirement(requirement) : PositiveRequirement(requirement);
        var text = $" {requirement.Text} {requirement.AcceptanceCriteria}".ToLowerInvariant();
        return category switch
        {
            "wireframe" or "design" or "frontend" => requirement.Frontend,
            "security" => Surface(requirement.Surface, "security") || Regex.IsMatch(text,
                @"\b(?:auth|(?:un)?(?:authenticat|authoriz|authoris)\w*|sessions?|identity|cookies?|secrets?|permissions?|cors|csrf|origin|redirects?|sensitive|exposure|trust)\b|\bonly\s+(?:the\s+)?(?:owner|member|actor|user)\b",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
            "data" => IsPositiveDataRequirement(requirement),
            "database-migration" => ContainsSchemaMigrationSignal(requirement),
            "data-backfill" => ContainsAny(text, "backfill", "existing data", "reindex", "reconcile data", "transform records"),
            "contract" => Surface(requirement.Surface, "api", "contract") || ContainsApiSignal(text),
            "backend" => requirement.Backend,
            "integration" => ContainsIntegrationSignal(text),
            "infrastructure" => Surface(requirement.Surface, "delivery") && ContainsAnyTerm(text,
                "configuration", "deployment", "runtime", "compose", "environment"),
            "observability" => ContainsObservabilityRequirementSignal(requirement),
            "lifecycle" => !requirement.Frontend
                && (string.IsNullOrWhiteSpace(requirement.Surface)
                    || Surface(requirement.Surface, "data", "backend", "full-stack"))
                && ContainsLifecycleRequirementSignal(requirement),
            "rollout" => ContainsAny(text, "rollout", "release", "canary", "feature flag", "compatibility window", "post-release"),
            _ => fallbackKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || (requirement.Surface?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)),
        };
    }

    private static bool IsBehaviorSection(string heading)
        => !Regex.IsMatch(heading, @"\b(?:non[- ]?goals?|out[- ]of[- ]scope|exclusions?|examples?|references?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static FeatureRequirement PositiveRequirement(FeatureRequirement requirement)
    {
        var text = PositiveCapabilityText(requirement.Text);
        var acceptance = PositiveCapabilityText(requirement.AcceptanceCriteria);
        return requirement with
        {
            Text = text,
            AcceptanceCriteria = acceptance,
            Surface = text.Length == 0 ? null : requirement.Surface,
        };
    }

    private static FeatureRequirement AcceptedRequirement(FeatureRequirement requirement)
    {
        var text = AcceptedBehaviorText(requirement.Text);
        var acceptance = AcceptedBehaviorText(requirement.AcceptanceCriteria);
        return requirement with { Text = text, AcceptanceCriteria = acceptance, Surface = text.Length == 0 ? null : requirement.Surface };
    }

    // Conditional access qualifiers remain intact for security and endpoint
    // requirements. Only capability creation uses the narrower exclusion view.
    private static string PositiveCapabilityText(string text)
    {
        // Cue-located qualifiers stay excluded. If a fronted clause misses
        // the cue, consume only explicit qualifier continuations and keep
        // the whole remaining suffix. Unknown fragments stay positive;
        // nominal forms and the -ing continuation cue are bounded heuristics.
        const string action = @"(?!(?:and|or|but|the|an?|it|them|this|that|these|those)\b)(?![a-z]*ing\b)[a-z]+\s+(?:the|an?|it|them|this|these|those)\b";
        const string mainClause = @"(?:(?:(?:the|an?|this|that|these|those)\s+[a-z]+|it|they|we)\s+)?" + action;
        const string nominal = @"(?:[a-z]+|(?:the|an?|this|that|these|those)\s+[a-z]+(?:\s+[a-z]+)?)";
        const string coordinatedNominal = @"(?:and|or|nor)\s+" + nominal;
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        var timeout = TimeSpan.FromSeconds(1);
        var accepted = AcceptedBehaviorText(text);
        return Regex.Replace(accepted,
            @"\b(?:without|excluding|except\s+for)\b[^;]*?(?:(?<boundary>,\s*|\s+and\s+)(?=" + mainClause + @")|(?=;|$))",
            match =>
            {
                var preceding = accepted[..match.Index];
                var fronted = string.IsNullOrWhiteSpace(preceding[(preceding.LastIndexOf(';') + 1)..]);
                if (fronted && !match.Groups["boundary"].Success)
                {
                    var fragments = match.Value.Split(',');
                    var gerund = Regex.IsMatch(fragments[0], @"^(?:without|excluding|except\s+for)\s+[a-z]+ing\b", options, timeout);
                    var firstPositive = 1;
                    // The final fragment is always retained, as in the original fallback.
                    for (; firstPositive < fragments.Length - 1; firstPositive++)
                    {
                        var fragment = fragments[firstPositive].Trim();
                        var continuation = gerund
                            ? @"^(?:(?:and|or|nor)\s+)?[a-z]+ing\b"
                            : @"^(?:" + coordinatedNominal + @"|" + nominal + @"\s+" + coordinatedNominal + @")$";
                        if (Regex.IsMatch(fragment, continuation, options, timeout)) continue;
                        if (!gerund && Regex.IsMatch(fragment, @"^" + nominal + @"$", options, timeout)
                            && firstPositive + 1 < fragments.Length
                            && Regex.IsMatch(fragments[firstPositive + 1].Trim(), @"^" + coordinatedNominal + @"$", options, timeout)) continue;
                        break;
                    }
                    return string.Join(',', fragments.Skip(firstPositive));
                }
                return " ";
            },
            options, timeout).Trim(' ', ';');
    }

    private static string AcceptedBehaviorText(string text)
    {
        const string absentPredicate = @"(?:(?:the|this)\s+(?:component|feature|change|implementation)\s+)?(?:do(?:es)?\s+not|will\s+not|doesn't|don't|won't)\s+(?:include|implement|provide|introduce|require|manage|perform|support|create|use|add)\b";
        var clauses = Regex.Split(text,
            @"[.;\r\n]+|\b(?:but|however|whereas)\b|\b(?:and|while)\s+(?=(?:no\b|" + absentPredicate + "))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return string.Join("; ", clauses.Select(clause => clause.Trim().TrimStart('-', '*', ' '))
            .Where(clause => clause.Length > 0 && !Regex.IsMatch(clause,
                @"^(?:not\s+applicable\b|n/?a\b|none\b|exclude(?:s|d)?\b|omit(?:s|ted)?\b|" + absentPredicate
                + @")|^no\b.+\b(?:required|needed|included|supported|introduced|implemented|created|added)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))));
    }

    private static bool ContainsApiSignal(string text)
    {
        var withoutCredentialNames = Regex.Replace(text, @"(?i)\bapi\s+keys?\b", string.Empty,
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return Regex.IsMatch(withoutCredentialNames,
                   @"(?i)\b(?:endpoints?|routes?|apis?|requests?|responses?|contracts?|openapi)\b",
                   RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
               || ContainsAny(withoutCredentialNames, " get /", " post /", " put /", " patch /", " delete /");
    }

    private static bool ContainsApiRequirementSignal(FeatureRequirement requirement)
    {
        var text = $" {requirement.Text} {requirement.AcceptanceCriteria}".ToLowerInvariant();
        if (!Surface(requirement.Surface, "frontend")) return ContainsApiSignal(text);
        return Regex.IsMatch(text,
            @"(?i)\b(?:endpoints?|apis?|openapi|route\s+handlers?|http)\b|\b(?:get|post|put|patch|delete)\s+/",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static bool ContainsIntegrationSignal(string text)
        => Regex.IsMatch(text,
               @"(?i)\b(?:integrat(?:e|es|ed|ing|ion)|cross-module|handoff)\b",
               RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
           || ContainsAny(text, "external system", "supertokens", "managed core", "google");

    private static bool ContainsObservabilityRequirementSignal(FeatureRequirement requirement)
    {
        if (Surface(requirement.Surface, "observability")) return true;
        var text = $" {requirement.Text} {requirement.AcceptanceCriteria}".ToLowerInvariant();
        text = Regex.Replace(text,
            @"(?i)\b(?:never|not|without|exclude(?:s|d)?|omit(?:s|ted)?)\b[^.;]{0,100}\b(?:logs?|metrics?|traces?|telemetry|diagnostics?)\b",
            string.Empty, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (Surface(requirement.Surface, "delivery")
            && ContainsAnyTerm(text, "logging", "log", "logs", "metric", "metrics", "trace", "traces",
                "telemetry", "diagnostic", "diagnostics", "alert", "alerts", "dashboard", "dashboards", "runbook", "runbooks"))
            return true;
        return Regex.IsMatch(text,
            @"(?i)\b(?:emit|record|provide|instrument|monitor|measure|alert|log|trace|diagnose|project|aggregate)(?:s|ed|ing)?\b[^.;]{0,100}\b(?:logs?|metrics?|traces?|telemetry|diagnostics?|alerts?|dashboards?|runbooks?)\b|\b(?:logs?|metrics?|traces?|telemetry|diagnostics?|alerts?|dashboards?|runbooks?)\b[^.;]{0,100}\b(?:emit|record|provide|instrument|monitor|measure|alert|log|trace|diagnose|project|aggregate)(?:s|ed|ing)?\b",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static bool IsPositiveDataRequirement(FeatureRequirement requirement)
    {
        if (Surface(requirement.Surface, "data")) return true;
        var text = $" {requirement.Text} {requirement.AcceptanceCriteria}".ToLowerInvariant();
        if (ContainsAny(text, "no repository", "no database", "never accesses a repository", "never accesses a database"))
            return false;
        if (Surface(requirement.Surface, "frontend")
            && !ContainsAny(text, "database", "repository", "persist", "stored", "storage"))
            return false;
        return Regex.IsMatch(text,
            @"(?i)\b(?:persist|persists|persisted|store|stores|stored|write|writes|lookup|resolve|resolves)\b[^.]{0,100}\b(?:actor|session|record|model|database|repository|hash)\b|\b(?:actor|session|record|model|database|repository|hash)\b[^.]{0,100}\b(?:persist|persists|persisted|store|stores|stored|write|writes|lookup|resolve|resolves)\b",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static bool ContainsSchemaMigrationSignal(FeatureRequirement requirement)
    {
        var text = $" {requirement.Text} {requirement.AcceptanceCriteria}".ToLowerInvariant();
        text = Regex.Replace(text,
            @"(?i)\bno\s+(?:new\s+)?(?:service\s+or\s+)?(?:(?:schema|database|sqlite)\s+)?migrations?\s+(?:is|are)\s+(?:required|needed)\b",
            string.Empty, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        text = Regex.Replace(text,
            @"(?i)\b(?:does\s+not|doesn't|will\s+not|won't)\s+require\s+(?:a\s+|any\s+)?(?:(?:schema|database|sqlite)\s+)?migrations?\b",
            string.Empty, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        text = Regex.Replace(text,
            @"(?i)\bwithout\s+(?:a\s+|any\s+)?(?:(?:schema|database|sqlite)\s+)?migrations?\b",
            string.Empty, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (ContainsAny(text, "schema migration", "database migration", "create table", "alter table",
                "new table", "new column", "database schema"))
            return true;

        var migration = Regex.IsMatch(text, @"\bmigrations?\b",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return migration && (Surface(requirement.Surface, "data", "backend", "delivery")
            || ContainsAny(text, "sqlite", "schema", "table", "column", "index", "constraint", "database object"));
    }

    private static bool ContainsLifecycleRequirementSignal(FeatureRequirement requirement)
    {
        var text = $" {requirement.Text} {requirement.AcceptanceCriteria}".ToLowerInvariant();
        if (ContainsAny(text, "lifecycle", "carry-forward", "carry forward", "conversion",
                "becomes a customer", "inherited", "archive", "ownership transfer",
                "permanently delete", "permanent deletion", "soft-delete", "soft delete"))
            return true;

        var restore = Regex.IsMatch(text, @"\brestore(?:d)?\b",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return restore && !ContainsAny(text, "focus", "backup", "database", "sqlite", "recovery");
    }

    private static bool HasActiveLifecycleSection(IReadOnlyDictionary<string, string> sections)
        => sections
            .Where(section => IsBehaviorSection(section.Key))
            .Where(section => section.Key.Contains("lifecycle", StringComparison.OrdinalIgnoreCase)
                || section.Key.Contains("carry-forward", StringComparison.OrdinalIgnoreCase)
                || section.Key.Contains("conversion", StringComparison.OrdinalIgnoreCase))
            .Select(section => Normalize(section.Value).Split('\n')
                .Where(line => !ContainsAny(line.ToLowerInvariant(),
                    "design", "wireframe", "renderer", "png", "manifest", "re-import", "regeneration",
                    "approval evidence", "test-case", "test case", "automation mapping"))
                .Aggregate(new StringBuilder(), (builder, line) => builder.AppendLine(line)).ToString())
            .Select(value => PositiveCapabilityText(value))
            .Any(value => value.Length > 0
                && !Regex.IsMatch(value, @"^(?:not applicable|n/?a|none)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                && ContainsLifecycleRequirementSignal(new FeatureRequirement(
                    "SECTION", value, value, null, false, false, false, "medium", null, null)));

    private static IReadOnlyList<string> ResolveTargets(FeatureSpec spec, string category)
    {
        if (spec.TargetRoles.Count == 0 || spec.TargetRoles.Values.All(roles => roles.Count == 0)) return spec.Targets;
        bool Matches(IReadOnlyList<string> roles) => category switch
        {
            "wireframe" or "design" or "frontend" => roles.Any(IsFrontendRole),
            "backend" or "data" or "database-migration" or "data-backfill" or "lifecycle" => roles.Any(IsBackendRole),
            "contract" => roles.Any(role => IsBackendRole(role) || IsFrontendRole(role)),
            "infrastructure" => roles.Any(role => role.Equals("infrastructure", StringComparison.OrdinalIgnoreCase)),
            _ => true,
        };
        var routed = spec.Targets.Where(target => spec.TargetRoles.TryGetValue(target, out var roles) && Matches(roles)).ToArray();
        return routed.Length > 0 ? routed : spec.Targets;
    }

    private static bool IsFrontendRole(string role)
        => role.Contains("frontend", StringComparison.OrdinalIgnoreCase)
           || role.Contains("mobile", StringComparison.OrdinalIgnoreCase)
           || role.Contains("native", StringComparison.OrdinalIgnoreCase)
           || role.Contains("game", StringComparison.OrdinalIgnoreCase);

    private static bool IsBackendRole(string role)
        => role.Contains("backend", StringComparison.OrdinalIgnoreCase)
           || role.Equals("database", StringComparison.OrdinalIgnoreCase)
           || role.Contains("worker", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadTargetRoles(
        string repository,
        IReadOnlyList<string> targets)
    {
        var result = targets.ToDictionary(target => target, _ => (IReadOnlyList<string>)[], StringComparer.OrdinalIgnoreCase);
        var workspacePath = Path.Combine(repository, ".cis", "workspace.yml");
        if (!File.Exists(workspacePath)) return result;

        string? id = null;
        string? path = null;
        void Flush()
        {
            if (id is null || path is null || !result.ContainsKey(id)) return;
            var targetRoot = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(repository, path));
            var manifestPath = Path.Combine(targetRoot, ".cis", "starter-manifest.yml");
            if (!File.Exists(manifestPath)) return;
            var roles = Regex.Matches(File.ReadAllText(manifestPath), @"(?ms)^\s+roles:\s*\n(?<items>(?:\s+-\s+[^\r\n]+\r?\n?)+)")
                .SelectMany(match => Normalize(match.Groups["items"].Value).Split('\n'))
                .Select(line => Regex.Match(line, @"^\s+-\s+(?<value>[^#]+)$"))
                .Where(match => match.Success)
                .Select(match => match.Groups["value"].Value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            result[id] = roles;
        }

        foreach (var line in Normalize(File.ReadAllText(workspacePath)).Split('\n'))
        {
            var idMatch = Regex.Match(line, @"^\s*-\s+id:\s*(?<value>.+?)\s*$", RegexOptions.CultureInvariant);
            if (idMatch.Success)
            {
                Flush();
                id = Unquote(idMatch.Groups["value"].Value);
                path = null;
                continue;
            }
            var pathMatch = Regex.Match(line, @"^\s+path:\s*(?<value>.+?)\s*$", RegexOptions.CultureInvariant);
            if (pathMatch.Success) path = Unquote(pathMatch.Groups["value"].Value);
        }
        Flush();
        return result;
    }

    private static string[] Impacts(IReadOnlyList<ImpactFinding> accepted, params string[] categories)
    {
        var matches = accepted.Where(item => categories.Contains(item.Category, StringComparer.OrdinalIgnoreCase)).Select(item => item.Id).ToArray();
        return matches.Length > 0 ? matches : accepted.Select(item => item.Id).ToArray();
    }

    private static string[] Dependencies(params string?[] values) => values.Where(value => value is not null).Cast<string>().ToArray();
    private static bool IsFrontendTarget(string value) => value.Equals("frontend", StringComparison.OrdinalIgnoreCase)
        || value.Equals("web", StringComparison.OrdinalIgnoreCase) || value.Equals("mobile", StringComparison.OrdinalIgnoreCase)
        || value.Equals("native", StringComparison.OrdinalIgnoreCase) || NormalizeFrontendType(value) is not null;
    private static bool Surface(string? value, params string[] expected) => value is not null
        && value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(item => expected.Contains(item, StringComparer.OrdinalIgnoreCase));
    private static bool ContainsAny(string text, params string[] values) => values.Any(text.Contains);
    private static bool ContainsAnyTerm(string text, params string[] values) => values.Any(value => ContainsTerm(text, value));
    private static bool ContainsTerm(string text, string value)
        => Regex.IsMatch(text, $@"(?i)(?<![a-z0-9]){Regex.Escape(value)}(?![a-z0-9])",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static bool ContainsFrontendSignal(string text)
        => Regex.IsMatch(text,
            @"(?i)(?<![a-z0-9])(?:screen|page|view|tab|frontend|responsive|accessib[a-z]*)(?![a-z0-9])",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static bool IsPlaceholder(string value) => string.IsNullOrWhiteSpace(value)
        || Regex.IsMatch(value, @"\b(?:TODO|TBD)\b|(?i:\bTO BE COMPLETED\b)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
        || Regex.IsMatch(value, @"^\s*<[^<>]+>\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static string[] SplitRow(string line) => line.Trim().Trim('|').Split('|').Select(value => value.Trim()).ToArray();
    private static int Find(string[] values, params string[] expected) => Array.FindIndex(values, value => expected.Contains(value, StringComparer.OrdinalIgnoreCase));
    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    private static string NormalizeHeading(string value) => Regex.Replace(value, @"^\d+(?:\.\d+)*\.?(?:\s+)", string.Empty).Trim();
    private static string Unquote(string value) => value.Trim().Trim('"', '\'');
    private static string CleanLine(string value) => Regex.Replace(value.Replace('|', '/').Trim(), @"^\d+\.\s+", string.Empty).Trim();

    [GeneratedRegex(@"^(?<number>\d+)\.\s+(?<text>.+)$")]
    private static partial Regex NumberedItemPattern();

    private sealed record FrontMatter(
        IReadOnlyDictionary<string, string> Scalars,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Lists);
    private sealed record FeatureSpec(
        string Title,
        string DocumentType,
        string RelativePath,
        string Sha256,
        string SignalText,
        IReadOnlyList<FeatureRequirement> Requirements,
        IReadOnlyList<string> Targets,
        IReadOnlyList<string> Stack,
        IReadOnlyList<string> References,
        IReadOnlyDictionary<string, string> Sections,
        IReadOnlyList<string> Exclusions,
        bool Frontend,
        bool Backend,
        bool Data,
        bool SchemaMigration,
        bool Api,
        bool Integration,
        bool Search,
        bool Lifecycle,
        IReadOnlyList<string> FrontendTypes,
        bool PublicEndpoints,
        IReadOnlyDictionary<string, IReadOnlyList<string>> TargetRoles);
    private sealed record FeatureRequirement(
        string Id,
        string Text,
        string AcceptanceCriteria,
        string? Surface,
        bool Frontend,
        bool Backend,
        bool HasContract,
        string Complexity,
        string? FrontendType,
        string? StoryId);
    private sealed record TaskInstance(Cis.Abstractions.CisTaskTypeDefinition Definition, string? FrontendType, string? StoryId);
    private sealed record FeatureParseResult(FeatureSpec? Spec, IReadOnlyList<string> Errors);
    private sealed record ManualTestCase(
        string Id,
        string Title,
        string Section,
        string Priority,
        string Type,
        string Preconditions,
        IReadOnlyList<string> Steps,
        string ExpectedResult,
        string RequirementId,
        string FrontendType,
        string AutomationStatus,
        IReadOnlyList<string> AutomatedTestReferences);
    private sealed record ManualTestCaseArtifacts(string Markdown, string Csv, int Count, int AutomatedCount);
}

internal sealed record FeatureIssuePack(
    PlanSource Source,
    IReadOnlyList<PlanWorkItem> WorkItems,
    IReadOnlyDictionary<string, string> TaskDocuments,
    string Title,
    string ManualTestCasesMarkdown,
    string ManualTestCasesCsv,
    int ManualTestCaseCount,
    int AutomatedManualTestCaseCount);

internal sealed record FeatureIssuePackBuildResult(
    FeatureIssuePack? Pack,
    IReadOnlyList<string> Errors,
    IReadOnlyList<TaskTypeCapabilityConflict> Conflicts);

internal sealed record ImportedTaskLists(IReadOnlyList<string> RequiredOutputs, IReadOnlyList<string> NegativeCriteria)
{
    public IReadOnlyList<Cis.Abstractions.CisTaskVerificationObligation>? VerificationObligations { get; init; }
}
