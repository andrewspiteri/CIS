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
        IReadOnlyList<ImpactFinding> accepted,
        TaskTypeRegistry registry,
        IReadOnlyList<TaskTypeCapabilitySelection> selections,
        IReadOnlyList<PlanWorkItem> existingItems)
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
        var items = BuildItems(spec, accepted, resolution.Definitions, existingItems);
        var documents = items.ToDictionary(
            item => item.TaskPath!,
            item => RenderTask(spec, source, item, items, accepted),
            StringComparer.OrdinalIgnoreCase);
        return new(new FeatureIssuePack(source, items, documents, spec.Title), [], []);
    }

    private static IReadOnlyList<PlanWorkItem> BuildItems(
        FeatureSpec spec,
        IReadOnlyList<ImpactFinding> accepted,
        IReadOnlyList<Cis.Abstractions.CisTaskTypeDefinition> definitions,
        IReadOnlyList<PlanWorkItem> existingItems)
    {
        var allRequirements = spec.Requirements.Select(item => item.Id).ToArray();
        var allImpacts = accepted.Select(item => item.Id).ToArray();
        var applicable = definitions.ToArray();
        var instances = applicable.SelectMany(definition => IsFrontendCategory(definition.Category)
                ? spec.FrontendTypes.Select(frontendType => new TaskInstance(definition, frontendType))
                : [new TaskInstance(definition, null)])
            .ToArray();
        var existingByIdentity = existingItems
            .GroupBy(item => Identity(item.TaskTypeKey, item.FrontendType), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var orderCounts = instances.GroupBy(instance => instance.Definition.Order)
            .ToDictionary(group => group.Key, group => group.Count());
        var idByIdentity = instances.ToDictionary(instance => Identity(instance.Definition.Key, instance.FrontendType), instance =>
        {
            if (existingByIdentity.TryGetValue(Identity(instance.Definition.Key, instance.FrontendType), out var existing)) return existing.Id;
            var baseId = $"WORK-{instance.Definition.Order:000}";
            if (instance.FrontendType is not null) return $"{baseId}-{FrontendTypeSuffix(instance.FrontendType)}";
            return orderCounts[instance.Definition.Order] == 1 ? baseId : $"{baseId}-{TaskKeySuffix(instance.Definition.Key)}";
        }, StringComparer.OrdinalIgnoreCase);
        var idsByType = instances.GroupBy(instance => instance.Definition.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(instance =>
                (instance.FrontendType, idByIdentity[Identity(instance.Definition.Key, instance.FrontendType)])).ToArray(), StringComparer.OrdinalIgnoreCase);
        var coordinationId = idsByType["core.coordination.scope-guard"].Single().Item2;
        var items = new List<PlanWorkItem>();

        foreach (var instance in instances)
        {
            var definition = instance.Definition;
            var dependencies = ResolveDependencies(definition, instance.FrontendType, applicable, idsByType);
            var requirementIds = definition.CreationPolicy == "always"
                ? allRequirements
                : RequirementIds(spec, definition.Category, instance.FrontendType, definition.TriggerTerms.ToArray());
            var impactIds = definition.Category switch
            {
                "documentation" => Impacts(accepted, "documentation", "contract"),
                "verification" or "assurance" => Impacts(accepted, "verification", "implementation"),
                _ => allImpacts,
            };
            var identity = Identity(definition.Key, instance.FrontendType);
            var id = idByIdentity[identity];
            var existing = existingByIdentity.GetValueOrDefault(identity);
            var highParent = definition.Key == "core.coordination.scope-guard";
            var negative = spec.Exclusions.Count > 0
                ? spec.Exclusions
                : ["Do not expand scope beyond accepted impacts and the imported feature specification."];
            if (spec.PublicEndpoints && PublicEndpointPolicyCategory(definition.Category))
            {
                negative = negative.Concat(PublicEndpointNegativeCriteria).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            var acceptance = spec.PublicEndpoints && PublicEndpointPolicyCategory(definition.Category)
                ? definition.AcceptanceCriteria + " " + PublicEndpointAcceptance(definition.Category)
                : definition.AcceptanceCriteria;
            var validation = spec.PublicEndpoints && PublicEndpointPolicyCategory(definition.Category)
                ? definition.Validation + " " + PublicEndpointValidation(definition.Category)
                : definition.Validation;
            if (!spec.Frontend && definition.Category == "verification")
                validation = validation.Replace("browser journeys, ", string.Empty, StringComparison.OrdinalIgnoreCase);
            items.Add(new PlanWorkItem(
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
                [],
                negative,
                RequiredOutputs(definition.Category),
                instance.FrontendType) with
            {
                Status = existing?.Status ?? (highParent ? "decomposed" : "draft"),
            });
        }

        return items;
    }

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
    private static string Identity(string taskTypeKey, string? frontendType) => $"{taskTypeKey}|{frontendType ?? "none"}";

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
        if (definition.Category is "infrastructure" or "observability")
            return spec.Requirements.Any(requirement => MatchesCategory(
                requirement, definition.Category, definition.TriggerTerms));
        if (definition.Category is "data-backfill" or "rollout")
        {
            var requirementSignals = string.Join(' ', spec.Requirements.Select(requirement =>
                $"{requirement.Surface} {requirement.Text} {requirement.AcceptanceCriteria}"));
            return definition.TriggerTerms.Any(term =>
                requirementSignals.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        return definition.TriggerTerms.Any(term => spec.SignalText.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

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
        IReadOnlyList<Cis.Abstractions.CisTaskTypeDefinition> applicable,
        IReadOnlyDictionary<string, (string? FrontendType, string Id)[]> idsByType)
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

        return keys.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(idsByType.ContainsKey)
            .SelectMany(key => idsByType[key]
                .Where(candidate => frontendType is null || candidate.FrontendType is null
                    || candidate.FrontendType.Equals(frontendType, StringComparison.OrdinalIgnoreCase))
                .Select(candidate => candidate.Id))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> RequiredOutputs(string category) => category switch
    {
        "coordination" => ["Approved dependency ledger", "Coverage and gate inventory", "Final child disposition"],
        "wireframe" => ["wireframes.md", "Screen/action/path coverage", "Human wireframe approval"],
        "design" => ["Self-contained Sharp/SVG .mjs renderer", "PNG manifest", "design.md approval record"],
        "documentation" => ["Updated canonical specifications, contracts, decisions, and references"],
        "verification" => ["verification.md evidence ledger", "Requirement and negative-criteria coverage"],
        "assurance" => ["Independent findings and disposition", "Residual-risk record"],
        "delivery" => ["Planned-versus-actual report", "Reproducible handoff evidence"],
        _ => [$"Completed {category} artifacts", "Reproducible validation evidence"],
    };

    private static string RenderTask(
        FeatureSpec spec,
        PlanSource source,
        PlanWorkItem item,
        IReadOnlyList<PlanWorkItem> allItems,
        IReadOnlyList<ImpactFinding> accepted)
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
        builder.AppendLine($"targets: {JsonSerializer.Serialize(item.Targets ?? [])}");
        builder.AppendLine($"decision_ids: {JsonSerializer.Serialize(item.DecisionIds ?? [])}");
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
        foreach (var check in ValidationChecks(item.Category, spec.Frontend))
        {
            builder.AppendLine($"- [ ] {check}");
        }
        builder.AppendLine($"- [ ] {item.Validation}");

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
        var sectionLines = classificationScopedFrontend
            ? []
            : spec.Sections
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

    private static IReadOnlyList<string> ValidationChecks(string category, bool frontend) => category switch
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
        "verification" when frontend => ["Focused and affected automated suites pass.", "Browser regression uses repository page objects.", "Coverage and skipped or unrun checks are recorded."],
        "verification" => ["Focused and affected automated suites pass.", "Coverage and skipped or unrun checks are recorded."],
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
        var all = content.ToLowerInvariant();
        var targetRoles = ReadTargetRoles(repository, targets);
        var requirementSignal = string.Join(' ', requirements.Select(requirement =>
            $"{requirement.Surface} {requirement.Text} {requirement.AcceptanceCriteria}")).ToLowerInvariant();
        var frontendSignal = requirements.Any(requirement => Surface(requirement.Surface, "frontend", "full-stack", "mobile", "native"))
            || targetRoles.Values.SelectMany(value => value).Any(IsFrontendRole);
        var backend = requirements.Any(requirement => Surface(requirement.Surface, "backend", "full-stack", "api", "data", "security", "contract"))
            || targetRoles.Values.SelectMany(value => value).Any(IsBackendRole);
        var data = requirements.Any(IsPositiveDataRequirement);
        var schemaMigration = requirements.Any(ContainsSchemaMigrationSignal);
        var api = requirements.Any(requirement => Surface(requirement.Surface, "api", "contract")
            || ContainsApiSignal($" {requirement.Text} {requirement.AcceptanceCriteria}".ToLowerInvariant()));
        var integration = ContainsIntegrationSignal(requirementSignal);
        var search = ContainsAny(all, "search", "projection", "retrieval", "indexing", "document type");
        var lifecycle = requirements.Any(ContainsLifecycleRequirementSignal)
            || HasActiveLifecycleSection(sections);
        var classified = requirements.Select(requirement => Classify(requirement, frontendSignal, backend, title, targets)).ToArray();
        var frontend = classified.Any(requirement => requirement.Frontend);
        backend |= classified.Any(requirement => requirement.Backend);
        var frontendTypes = classified.Where(requirement => requirement.Frontend)
            .Select(requirement => requirement.FrontendType ?? "customer")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(FrontendTypeOrder)
            .ToArray();
        var publicEndpoints = requirements.Any(requirement =>
                NormalizeFrontendType(requirement.FrontendType) == "public"
                && (Surface(requirement.Surface, "backend", "api", "contract", "full-stack")
                    || ContainsApiSignal($" {requirement.Text} {requirement.AcceptanceCriteria}".ToLowerInvariant())))
            || ContainsAny(requirementSignal, "public application endpoint", "public endpoint", "public api",
                "unauthenticated endpoint", "without requiring authentication", "without authentication")
            || all.Contains("public-endpoint-cache", StringComparison.Ordinal);
        var exclusions = ParseListSection(sections, "non-goal", "out of scope", "exclusion");
        var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        return new(new FeatureSpec(title, type!, relative, hash, all, classified, targets, stack, references, sections,
            exclusions, frontend, backend, data, schemaMigration, api, integration, search, lifecycle, frontendTypes,
            publicEndpoints, targetRoles), []);
    }

    private static IReadOnlyList<FeatureRequirement> ParseStructuredRequirements(string content, out List<string> errors)
    {
        errors = [];
        var lines = Normalize(content).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var headers = SplitRow(lines[index]);
            var idAt = Find(headers, "ID");
            var requirementAt = Find(headers, "Requirement");
            var acceptanceAt = Find(headers, "Acceptance criteria");
            var surfaceAt = Find(headers, "Surface", "Affected surfaces");
            var frontendTypeAt = Find(headers, "Frontend type", "Frontend classification", "Audience");
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
                result.Add(new FeatureRequirement(id, requirement, acceptance, surface, false, false, false, "medium", frontendType));
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
        var goals = sections.FirstOrDefault(section => section.Key.Contains("goal", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(goals.Value)) yield break;
        var ordinal = 0;
        foreach (var line in Normalize(goals.Value).Split('\n'))
        {
            var match = NumberedItemPattern().Match(line.Trim());
            if (!match.Success || IsPlaceholder(match.Groups["text"].Value)) continue;
            ordinal++;
            var text = match.Groups["text"].Value.Trim();
            yield return new FeatureRequirement($"GOAL-{ordinal:000}", text, text, null, false, false, false, "medium", null);
        }
    }

    private static FeatureRequirement Classify(
        FeatureRequirement requirement,
        bool featureFrontend,
        bool featureBackend,
        string title,
        IReadOnlyList<string> targets)
    {
        var frontend = Surface(requirement.Surface, "frontend", "full-stack", "mobile", "native")
            || IsFrontendTarget(requirement.Surface ?? string.Empty)
            || ContainsFrontendSignal(requirement.Text);
        var backend = Surface(requirement.Surface, "backend", "full-stack", "api", "data", "security", "contract")
            || ContainsAny(requirement.Text.ToLowerInvariant(), "model", "api", "persist", "service", "event", "permission", "search", "audit")
            || (!frontend && featureBackend);
        if (!frontend && !backend)
        {
            frontend = featureFrontend && ContainsAny(requirement.Text.ToLowerInvariant(), "display", "show", "action", "navigate");
        }
        var contract = Surface(requirement.Surface, "api", "data", "security", "contract", "full-stack")
            || ContainsAny(requirement.Text.ToLowerInvariant(), "api", "permission", "event", "state", "search", "projection", "data");
        var score = (frontend && backend ? 2 : 0)
            + (requirement.Text.Length > 180 ? 1 : 0)
            + (ContainsAny(requirement.Text.ToLowerInvariant(), "integration", "migration", "security", "permission", "search", "workflow") ? 2 : 0);
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
        var text = requirement.Text.ToLowerInvariant();
        var explicitEndpoint = ContainsAny(text, "endpoint", " api", "http", "route handler");
        return requirement.FrontendType == "public"
            && (requirement.HasContract || Surface(requirement.Surface, "api", "contract", "full-stack") || explicitEndpoint);
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
        var body = new StringBuilder();
        void Flush()
        {
            if (heading is not null) result[NormalizeHeading(heading)] = body.ToString().Trim();
            body.Clear();
        }
        foreach (var line in Normalize(content).Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                heading = line[3..].Trim();
            }
            else if (heading is not null)
            {
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
        var text = $" {requirement.Text} {requirement.AcceptanceCriteria}".ToLowerInvariant();
        return category switch
        {
            "wireframe" or "design" or "frontend" => requirement.Frontend,
            "security" => Surface(requirement.Surface, "security") || ContainsAny(text,
                "auth", "session", "identity", "cookie", "secret", "permission", "cors", "csrf", "origin", "redirect", "sensitive", "exposure", "trust"),
            "data" => IsPositiveDataRequirement(requirement),
            "database-migration" => ContainsSchemaMigrationSignal(requirement),
            "data-backfill" => ContainsAny(text, "backfill", "existing data", "reindex", "reconcile data", "transform records"),
            "contract" => Surface(requirement.Surface, "api", "contract") || ContainsApiSignal(text),
            "backend" => requirement.Backend,
            "integration" => ContainsIntegrationSignal(text),
            "infrastructure" => Surface(requirement.Surface, "delivery") && ContainsAnyTerm(text,
                "configuration", "deployment", "runtime", "compose", "environment"),
            "observability" => ContainsAny(text, "logging", " log", "metric", "trace", "telemetry", "diagnostic", "alert", "dashboard", "runbook", "dependency health"),
            "lifecycle" => !requirement.Frontend
                && (string.IsNullOrWhiteSpace(requirement.Surface)
                    || Surface(requirement.Surface, "data", "backend", "full-stack"))
                && ContainsLifecycleRequirementSignal(requirement),
            "rollout" => ContainsAny(text, "rollout", "release", "canary", "feature flag", "compatibility window", "post-release"),
            _ => fallbackKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || (requirement.Surface?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)),
        };
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

    private static bool ContainsIntegrationSignal(string text)
        => Regex.IsMatch(text,
               @"(?i)\b(?:integrat(?:e|es|ed|ing|ion)|cross-module|handoff)\b",
               RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
           || ContainsAny(text, "external system", "supertokens", "managed core", "google");

    private static bool IsPositiveDataRequirement(FeatureRequirement requirement)
    {
        if (Surface(requirement.Surface, "data")) return true;
        var text = $" {requirement.Text} {requirement.AcceptanceCriteria}".ToLowerInvariant();
        if (ContainsAny(text, "no repository", "no database", "never accesses a repository", "never accesses a database"))
            return false;
        return Regex.IsMatch(text,
            @"(?i)\b(?:persist|persists|persisted|store|stores|stored|write|writes|lookup|resolve|resolves)\b[^.]{0,100}\b(?:actor|session|record|model|database|repository|hash)\b|\b(?:actor|session|record|model|database|repository|hash)\b[^.]{0,100}\b(?:persist|persists|persisted|store|stores|stored|write|writes|lookup|resolve|resolves)\b",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static bool ContainsSchemaMigrationSignal(FeatureRequirement requirement)
    {
        var text = $" {requirement.Text} {requirement.AcceptanceCriteria}".ToLowerInvariant();
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
            .Where(section => section.Key.Contains("lifecycle", StringComparison.OrdinalIgnoreCase)
                || section.Key.Contains("carry-forward", StringComparison.OrdinalIgnoreCase)
                || section.Key.Contains("conversion", StringComparison.OrdinalIgnoreCase))
            .Select(section => section.Value.Trim())
            .Any(value => value.Length > 0
                && !Regex.IsMatch(value, @"^(?:not applicable|n/?a|none)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));

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
        || value.Contains('<') && value.Contains('>');
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
        string? FrontendType);
    private sealed record TaskInstance(Cis.Abstractions.CisTaskTypeDefinition Definition, string? FrontendType);
    private sealed record FeatureParseResult(FeatureSpec? Spec, IReadOnlyList<string> Errors);
}

internal sealed record FeatureIssuePack(
    PlanSource Source,
    IReadOnlyList<PlanWorkItem> WorkItems,
    IReadOnlyDictionary<string, string> TaskDocuments,
    string Title);

internal sealed record FeatureIssuePackBuildResult(
    FeatureIssuePack? Pack,
    IReadOnlyList<string> Errors,
    IReadOnlyList<TaskTypeCapabilityConflict> Conflicts);
