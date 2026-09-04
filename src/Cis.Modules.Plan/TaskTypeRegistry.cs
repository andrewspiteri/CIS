using Cis.Abstractions;
using System.Text.RegularExpressions;

namespace Cis.Modules.Plan;

public sealed class TaskTypeRegistry
{
    private readonly IReadOnlyList<CisTaskTypeDefinition> _definitions;

    public TaskTypeRegistry(IEnumerable<ICisTaskTypeProvider> providers)
    {
        var providerArray = providers.OrderBy(provider => provider.ProviderKey, StringComparer.Ordinal).ToArray();
        var duplicateProvider = providerArray.GroupBy(provider => provider.ProviderKey, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateProvider is not null)
            throw new InvalidOperationException($"Task type provider key is registered more than once: {duplicateProvider.Key}");

        var definitions = providerArray
            .SelectMany(provider => provider.GetTaskTypes().Select(definition => definition with
            {
                ProviderKey = provider.ProviderKey,
                CapabilityKey = string.IsNullOrWhiteSpace(definition.CapabilityKey)
                    ? definition.Key
                    : definition.CapabilityKey,
            }))
            .OrderBy(definition => definition.Order)
            .ThenBy(definition => definition.Key, StringComparer.Ordinal)
            .ToArray();
        var duplicate = definitions.GroupBy(definition => definition.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Task type key is registered more than once: {duplicate.Key}");
        }

        var keys = definitions.Select(definition => definition.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.ProviderKey)
                || string.IsNullOrWhiteSpace(definition.Key)
                || string.IsNullOrWhiteSpace(definition.Version)
                || string.IsNullOrWhiteSpace(definition.CapabilityKey))
                throw new InvalidOperationException("Task type provider, key, version, and capability are required.");
            if (!definition.ProviderKey.Equals("cis.core", StringComparison.OrdinalIgnoreCase)
                && definition.Key.StartsWith("core.", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Extension provider '{definition.ProviderKey}' cannot register reserved core task type '{definition.Key}'.");
            if (definition.ProviderKey.Equals("cis.core", StringComparison.OrdinalIgnoreCase)
                && !definition.Key.StartsWith("core.", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Core provider task type must use the core namespace: {definition.Key}");
            foreach (var dependency in definition.DependsOnTypeKeys)
            {
                if (dependency.Equals(definition.Key, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Task type cannot depend on itself: {definition.Key}");
                if (!keys.Contains(dependency))
                    throw new InvalidOperationException($"Task type '{definition.Key}' references unavailable dependency '{dependency}'.");
            }
            foreach (var replaced in definition.ReplacesTypeKeys ?? [])
            {
                if (replaced.StartsWith("core.", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Core task types are non-replaceable: {replaced}");
                if (replaced.Equals(definition.Key, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Task type cannot replace itself: {definition.Key}");
            }
        }
        if (HasDependencyCycle(definitions))
            throw new InvalidOperationException("Registered task type dependencies contain a cycle.");

        _definitions = definitions;
    }

    public IReadOnlyList<CisTaskTypeDefinition> Definitions => _definitions;

    public CisTaskTypeDefinition? Find(string key)
        => _definitions.FirstOrDefault(definition => definition.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public TaskTypeResolution ResolveApplicable(
        IReadOnlyList<CisTaskTypeDefinition> applicable,
        IReadOnlyList<TaskTypeCapabilitySelection> selections)
    {
        var errors = new List<string>();
        var conflicts = new List<TaskTypeCapabilityConflict>();
        var selected = new List<CisTaskTypeDefinition>();
        foreach (var selection in selections.Where(selection => Find(selection.SelectedTypeKey) is null))
            errors.Add($"Capability '{selection.CapabilityKey}' selects unavailable task type '{selection.SelectedTypeKey}'. Load the provider or record a new human selection.");
        foreach (var group in applicable.GroupBy(definition => definition.CapabilityKey, StringComparer.OrdinalIgnoreCase))
        {
            var candidates = group.OrderBy(definition => definition.Key, StringComparer.Ordinal).ToArray();
            if (candidates.Length == 1)
            {
                selected.Add(candidates[0]);
                continue;
            }
            if (candidates.Any(candidate => candidate.Key.StartsWith("core.", StringComparison.OrdinalIgnoreCase)))
            {
                conflicts.Add(new TaskTypeCapabilityConflict(group.Key, candidates.Select(candidate => candidate.Key).ToArray(),
                    "A core task type cannot be replaced or suppressed by an extension capability selection."));
                continue;
            }
            var selection = selections.FirstOrDefault(item =>
                item.CapabilityKey.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
            if (selection is null)
            {
                conflicts.Add(new TaskTypeCapabilityConflict(group.Key, candidates.Select(candidate => candidate.Key).ToArray(),
                    "Several applicable providers supply the same capability and no canonical repository selection exists."));
                continue;
            }
            var chosen = candidates.FirstOrDefault(candidate =>
                candidate.Key.Equals(selection.SelectedTypeKey, StringComparison.OrdinalIgnoreCase));
            if (chosen is null)
            {
                errors.Add($"Capability '{group.Key}' selects unavailable or inapplicable task type '{selection.SelectedTypeKey}'.");
                continue;
            }
            selected.Add(chosen);
        }

        foreach (var selection in selections)
        {
            var chosen = selected.FirstOrDefault(definition =>
                definition.Key.Equals(selection.SelectedTypeKey, StringComparison.OrdinalIgnoreCase));
            if (chosen is null) continue;
            foreach (var replaced in selection.ReplacesTypeKeys)
            {
                if (!(chosen.ReplacesTypeKeys ?? []).Contains(replaced, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"Selection for '{selection.CapabilityKey}' claims unsupported replacement '{replaced}'.");
                    continue;
                }
                selected.RemoveAll(definition => definition.Key.Equals(replaced, StringComparison.OrdinalIgnoreCase));
            }
        }

        foreach (var definition in selected.ToArray())
        {
            var conflict = selected.FirstOrDefault(candidate => candidate.Key != definition.Key
                && ((definition.ConflictsWithTypeKeys ?? []).Contains(candidate.Key, StringComparer.OrdinalIgnoreCase)
                    || (candidate.ConflictsWithTypeKeys ?? []).Contains(definition.Key, StringComparer.OrdinalIgnoreCase)));
            if (conflict is not null && !conflicts.Any(item => item.CandidateTypeKeys.Contains(definition.Key, StringComparer.OrdinalIgnoreCase)
                                                               && item.CandidateTypeKeys.Contains(conflict.Key, StringComparer.OrdinalIgnoreCase)))
                conflicts.Add(new TaskTypeCapabilityConflict(definition.CapabilityKey,
                    [definition.Key, conflict.Key], "Applicable task types declare a mutual-exclusion conflict."));
        }

        return new TaskTypeResolution(selected.OrderBy(definition => definition.Order).ThenBy(definition => definition.Key, StringComparer.Ordinal).ToArray(),
            conflicts, errors);
    }

    public static TaskTypeRegistry CreateDefault()
        => new([new CoreTaskTypeProvider()]);

    private static bool HasDependencyCycle(IReadOnlyList<CisTaskTypeDefinition> definitions)
    {
        var dependencies = definitions.ToDictionary(definition => definition.Key,
            definition => definition.DependsOnTypeKeys, StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Visit(string key)
        {
            if (visiting.Contains(key)) return true;
            if (!visited.Add(key)) return false;
            visiting.Add(key);
            foreach (var dependency in dependencies[key])
                if (dependencies.ContainsKey(dependency) && Visit(dependency)) return true;
            visiting.Remove(key);
            return false;
        }
        return definitions.Any(definition => Visit(definition.Key));
    }
}

public sealed class CoreTaskTypeProvider : ICisTaskTypeProvider
{
    public string ProviderKey => "cis.core";

    public IReadOnlyList<CisTaskTypeDefinition> GetTaskTypes() =>
    [
        Define("core.coordination.scope-guard", "coordination", "Feature delivery coordination and scope guard", 0, "always", [], [], "high",
            "Maintain scope, task coverage, dependencies, review gates, and final child disposition.",
            "Every requirement, accepted impact, exclusion, and applicable task type is covered without hidden scope or unresolved disposition.",
            "Validate task identities, coverage, dependencies, gates, lifecycle, and planned-versus-actual scope.", "human-final-acceptance"),
        Define("core.design.wireframe", "wireframe", "Define textual screens, states, actions, and paths", 10, "conditional",
            ["frontend", "screen", "page", "view", "tab", "route", "user interface", "mobile", "native", "swiftui", "compose", "nextjs", "angular"],
            ["core.coordination.scope-guard"], "medium",
            "Create the validated textual behavioral and navigation contract in wireframes.md.",
            "Every affected screen, state, action, condition, side effect, and destination path is explicit and structurally valid.",
            "Validate screen/action identity, path resolution, state and requirement coverage; exact human authority is recorded with design approval unless an earlier checkpoint is requested.",
            approval: "none", version: "1.1"),
        Define("core.design.visual", "design", "Render and approve the visual design pack", 20, "conditional",
            ["frontend", "screen", "page", "view", "tab", "route", "user interface", "mobile", "native", "swiftui", "compose", "nextjs", "angular"],
            ["core.design.wireframe"], "medium",
            "Generate the self-contained Sharp/SVG renderer and PNG pack inside the reusable application shell and component system.",
            "The exact validated wireframe digest, guideline-conformant renderer, and PNG manifest are explicitly approved together before any downstream work.",
            "Validate wireframe behavior, guideline provenance, shell/component templates, Sharp/SVG execution, PNG coverage, hashes, and combined approval.",
            approval: "global-design-approval", version: "1.1"),
        Define("core.documentation.contracts", "documentation", "Align specifications, contracts, and references", 30, "always", [], [], "low",
            "Keep governed requirements, decisions, contracts, dictionaries, and references aligned.",
            "All affected canonical documentation agrees with approved behavior and exclusions.",
            "Run strict documentation and applicable contract/reference drift validation."),
        Define("core.security.permissions", "security", "Implement security, permissions, and exposure boundaries", 40, "conditional",
            ["security", "permission", "authoriz", "authenticat", "sensitive", "internal-only", "visibility", "exposure", "trust boundary", "secret"], [], "medium",
            "Own authorization, sensitive-data, trust-boundary, and prohibited-exposure behavior.",
            "Positive and negative access paths enforce the approved security and visibility model.",
            "Run policy, authorization, data-exposure, secret, abuse-case, and security regression checks."),
        Define("core.data.persistence", "data", "Implement data model and persistence", 50, "conditional",
            ["data model", "persist", "database", "repository", "entity", "record", "audit", "storage"], [], "medium",
            "Implement the approved data model, constraints, persistence, audit, and retention behavior.",
            "Persistence behavior and invariants match the approved model without excluded entities or storage.",
            "Run model, repository, constraint, audit, and retention tests."),
        Define("core.data.database-migration", "database-migration", "Implement database schema migration", 60, "conditional",
            ["schema", "migration", "table", "column", "index", "constraint", "database object"], ["core.data.persistence"], "medium",
            "Apply forward schema changes with a recorded rollback or roll-forward position.",
            "Schema changes are repeatable, compatible, validated, and operationally recoverable.",
            "Run migration, idempotency, schema, compatibility, and rollback-status checks."),
        Define("core.data.backfill", "data-backfill", "Migrate, backfill, reconcile, or reindex existing data", 70, "conditional",
            ["backfill", "existing data", "transform records", "reconcile data", "reindex", "data migration"], ["core.data.database-migration"], "medium",
            "Transform existing data separately from structural schema change with restart and reconciliation safety.",
            "Existing records are transformed completely, idempotently, observably, and reversibly where required.",
            "Run dry-run, idempotency, batching, reconciliation, failure-recovery, and production-volume checks."),
        Define("core.api.contract", "contract", "Implement consumed contracts", 80, "conditional",
            [" api", "endpoint", "dto", "openapi", "contract", "request", "response", "etag"], [], "medium",
            "Implement governed HTTP, message, CLI, process, JSON, or event contracts with explicit exposure, scope, authorization, validation, compatibility, errors, cancellation, concurrency, and telemetry semantics where applicable.",
            "The declared contract baseline, permissions, errors, consumers, and implementation agree for compatible positive and negative behavior; HTTP inventory and OpenAPI evidence are required only for HTTP APIs.",
            "Run adapter, authorization, abuse, validator, serialization, compatibility, integration, schema/output, and contract-drift checks; run OpenAPI diff only when an HTTP API is affected."),
        Define("core.backend.behavior", "backend", "Implement backend domain and application behavior", 90, "conditional",
            ["backend", "domain", "service", "handler", "command", "workflow", "business rule", "state transition"], [], "medium",
            "Implement approved domain and application workflows and invariants.",
            "Backend behavior satisfies positive, negative, state-transition, and concurrency requirements.",
            "Run focused domain, application, integration, concurrency, and regression tests."),
        Define("core.frontend.implementation", "frontend", "Implement the approved frontend experience", 100, "conditional",
            ["frontend", "screen", "page", "view", "tab", "route", "user interface", "mobile", "native", "swiftui", "compose", "nextjs", "angular"], [], "medium",
            "Implement the approved screen pack and behavior using repository UI conventions.",
            "Production UI matches approved behavior and visual direction across required states and platforms.",
            "Run component, interaction, accessibility, lint, type, build, and browser checks."),
        Define("core.integration.handoff", "integration", "Implement cross-module integration and handoff", 110, "conditional",
            ["integration", "handoff", "cross-module", "event", "publishes", "subscribes", "linked task", "external system"], [], "medium",
            "Implement authoritative cross-boundary workflows without parallel subsystems.",
            "Handoffs preserve provenance, authorization, state, idempotency, and failure recovery.",
            "Run contract, integration, event, idempotency, timeout, and recovery tests."),
        Define("core.infrastructure.deployment", "infrastructure", "Implement infrastructure and deployment changes", 120, "conditional",
            ["terraform", "infrastructure", "cloud resource", "queue", "topic", "deployment", "pipeline", "feature flag", "configuration", "secret"], [], "medium",
            "Provision and deploy required resources, configuration, secrets, and pipeline changes safely.",
            "Infrastructure is validated, least-privileged, reproducible, and has an explicit rollback position.",
            "Run formatting, validation, policy, plan, deployment-smoke, configuration, and rollback checks."),
        Define("core.operations.observability", "observability", "Implement observability and operational readiness", 130, "conditional",
            ["logging", "metric", "trace", "alert", "dashboard", "runbook", "operational", "support"], [], "medium",
            "Provide logs, metrics, traces, alerts, dashboards, and support procedures proportional to risk.",
            "Operators can detect, diagnose, and respond to expected failures without exposing sensitive data.",
            "Validate telemetry emission, redaction, alert behavior, dashboards, runbooks, and failure drills."),
        Define("core.lifecycle.carry-forward", "lifecycle", "Implement lifecycle and carry-forward behavior", 140, "conditional",
            ["lifecycle", "conversion", "carry-forward", "carry forward", "inherited", "archive", "restore", "retention", "history"], [], "medium",
            "Preserve identity, provenance, visibility, and audit behavior through lifecycle transitions.",
            "Transitions avoid duplication and preserve approved access, history, and canonical ownership.",
            "Run transition, carry-forward, non-duplication, audit, permission, and retention tests."),
        Define("core.release.rollout", "rollout", "Plan rollout, release, and rollback", 150, "conditional",
            ["rollout", "release", "canary", "feature flag", "compatibility window", "rollback", "staged", "post-release"], [], "medium",
            "Control activation, compatibility, rollback, communication, and post-release validation.",
            "Release and rollback criteria, ownership, signals, and user/consumer communication are explicit.",
            "Validate flags, staged activation, compatibility, rollback rehearsal, release notes, and post-release checks."),
        Define("core.verification", "verification", "Complete targeted and regression verification", 160, "always", [], [], "medium",
            "Prove every requirement and prohibited behavior through proportionate deterministic evidence.",
            "Every stable manual case is reflected by recognized automated coverage, and all acceptance and negative criteria have reproducible passing evidence or approved residual risk.",
            "Trace TC-* identities into automated tests, refresh the catalogue, then run focused and affected suites, contract/migration checks, browser journeys, and coverage gates."),
        Define("core.assurance.independent", "assurance", "Perform independent assurance", 170, "always", [], ["core.verification"], "medium",
            "Challenge implementation and verification evidence independently and proportionally to risk.",
            "Independent review records findings, disposition, residual risk, and any required rework.",
            "Run applicable mutation, security, architecture, accessibility, or second-agent assurance."),
        Define("core.delivery.final-sweep", "delivery", "Run final delivery sweep and handoff", 180, "always", [], ["core.assurance.independent"], "low",
            "Reconcile planned versus actual scope, evidence, documentation, deferrals, and handoff readiness.",
            "Every child has a valid disposition and the final outcome is reproducible without converting blockers into passes.",
            "Run the repository completion gate, strict docs validation, final affected checks, and evidence audit."),
    ];

    private static CisTaskTypeDefinition Define(
        string key,
        string category,
        string title,
        int order,
        string policy,
        IReadOnlyList<string> triggers,
        IReadOnlyList<string> dependencies,
        string complexity,
        string purpose,
        string acceptance,
        string validation,
        string approval = "none",
        string version = "1.0")
        => new(key, version, category, title, order, policy, triggers, dependencies,
            complexity, purpose, acceptance, validation, approval);
}
