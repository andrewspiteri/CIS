namespace Cis.Modules.Repository;

internal sealed record ImplementationSkillDefinition(
    string Name,
    string Description,
    string Content);

internal sealed record SelectedImplementationSkillPack(
    string Id,
    string Title,
    string Reason,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<ImplementationSkillDefinition> Skills);

internal static class ImplementationSkillPackRegistry
{
    public static IReadOnlyList<SelectedImplementationSkillPack> Select(
        RepositoryClassification classification)
    {
        var selected = new List<SelectedImplementationSkillPack>();

        AddWhen(selected, classification, "dotnet-quality", ".NET quality",
            "C# implementation was detected.",
            component => component.Languages.Contains("csharp", StringComparer.Ordinal),
            [
                Skill("dotnet-test", "Build and test affected .NET projects with bounded commands and preserved evidence. Use when changing C# or .NET code.",
                    "Verify .NET changes without turning a targeted edit into an unbounded solution-wide run.",
                    ["Identify the affected project and its direct test projects.", "Run formatting or analyzers required by the repository.", "Build the narrowest project boundary with warnings visible.", "Run targeted tests, then widen only when impact or failures justify it.", "Record exact commands, results, skipped checks, and residual risk."],
                    "Do not report a build as proof that behavior or architecture tests passed."),
            ]);

        AddWhen(selected, classification, "core-testing", "Core testing",
            "Production implementation behavior was detected.",
            HasImplementation,
            [
                Skill("add-unit-tests", "Add focused unit tests that prove behavior and edge cases without coupling to implementation details. Use when changing deterministic logic.",
                    "Create fast tests that explain the contract and fail for meaningful regressions.",
                    ["Map acceptance criteria and invariants to observable cases.", "Follow the repository test framework, naming, and fixture conventions.", "Cover success, boundary, invalid, and authorization-sensitive behavior as applicable.", "Run the smallest affected test selection and inspect the failure mode.", "Record test evidence in the task or verification record."],
                    "Do not add tautological tests, sleep-based synchronization, or assertions on private implementation structure."),
                Skill("add-architecture-tests", "Add deterministic architecture tests for module boundaries and dependency rules. Use when a .NET change creates or crosses architectural seams.",
                    "Protect intended dependencies and ownership boundaries as executable policy.",
                    ["Read ADRs, module ownership, and existing architecture-test conventions.", "Express one stable boundary rule per test.", "Include a deliberate negative fixture when the framework permits it.", "Run the architecture-test project and preserve the command output.", "Link the rule to its governing decision or specification."],
                    "Do not encode transient namespaces as architecture without a canonical rule."),
                Skill("add-mutation-tests", "Use mutation testing to challenge important unit-test assertions. Use when risk or independent assurance requires evidence beyond line coverage.",
                    "Find behavior that existing tests execute but do not meaningfully assert.",
                    ["Select a bounded production scope and relevant test project.", "Reuse repository mutation configuration and baselines.", "Run the narrowest mutation job practical for the risk.", "Classify surviving mutants as test gaps, equivalent mutants, or accepted limitations.", "Preserve the report path and disposition evidence."],
                    "Do not run an unbounded mutation suite by default or hide surviving mutants."),
            ]);

        AddWhen(selected, classification, "business-assurance", "Business acceptance assurance",
            "Application or user-facing behavior was detected.",
            HasApplicationBehavior,
            [
                Skill("add-business-acceptance-tests", "Add acceptance-style tests for cross-step business outcomes and lifecycle workflows. Use when behavior spans commands, states, actors, or repositories.",
                    "Prove reviewed acceptance behavior in domain language without turning scenarios into low-level implementation scripts.",
                    ["Select approved acceptance criteria that cross more than one operation or state transition.", "Express scenarios using actors, preconditions, actions, outcomes, and denied paths.", "Keep step or fixture code thin and delegate setup through supported application boundaries.", "Use deterministic identities, clocks, and data while avoiding live human authentication.", "Run the focused acceptance suite and preserve scenario-level evidence."],
                    "Do not replace focused unit or integration tests with broad acceptance scenarios."),
            ]);

        AddWhen(selected, classification, "api-integration", "API integration",
            "A backend API producer was detected.",
            component => component.Roles.Contains("backend-api-producer", StringComparer.Ordinal),
            [
                Skill("add-api-integration-tests", "Add API integration tests for contracts, authorization, persistence boundaries, and errors. Use when changing a backend endpoint or API behavior.",
                    "Prove the externally observable API contract against the real application boundary.",
                    ["Start from the API dictionary, OpenAPI contract, permissions, and problem catalogue.", "Exercise success, validation, missing-resource, conflict, and authorization cases as applicable.", "Use the repository application factory or supported integration harness.", "Assert status, headers, schema, important values, and prohibited disclosure.", "Run deterministic contract validation with the targeted integration tests."],
                    "Do not bypass middleware or substitute controller-unit tests for API integration evidence."),
            ]);

        AddWhen(selected, classification, "data-persistence", "Data and persistence",
            "Persistence or database ownership was detected.",
            component => component.Capabilities.Contains("persistence", StringComparer.Ordinal)
                || component.Roles.Contains("database", StringComparer.Ordinal),
            [
                Skill("database-migration", "Design, implement, and verify reversible schema migrations and backfills. Use when persistent schema or stored data must change.",
                    "Deliver data changes with explicit compatibility, rollout, rollback, and recovery evidence.",
                    ["Compare the canonical data model with the current schema and usage.", "Separate schema migration from large or risky data backfill work.", "Define forward compatibility, rollback limits, locking, and dataset-size assumptions.", "Test apply and rollback or compensating recovery on a representative database.", "Record deployment ordering, monitoring, and reconciliation queries."],
                    "Do not combine irreversible destructive changes with application rollout without explicit human approval."),
                Skill("review-schema-sql", "Review schema and SQL changes for correctness, safety, performance, and operability. Use when queries, indexes, constraints, or migrations change.",
                    "Detect data correctness and production-operability risks before rollout.",
                    ["Trace each change to a data requirement and owning component.", "Review nullability, constraints, indexes, cardinality, and concurrency assumptions.", "Inspect representative query plans or explain output for performance-sensitive SQL.", "Check migration duration, locks, retries, rollback, and backfill observability.", "Record findings with exact file and evidence references."],
                    "Do not approve a migration solely because it applies to an empty local database."),
            ]);

        AddWhen(selected, classification, "real-dependency-testing", "Real dependency testing",
            "Persistence, authentication, eventing, or another service boundary was detected.",
            component => component.Capabilities.Any(capability => capability is "persistence" or "authentication" or "events")
                || component.Roles.Any(role => role is "database" or "event-producer" or "event-consumer"),
            [
                Skill("testcontainers-integration-test", "Add disposable real-dependency integration tests using the repository container harness. Use when service behavior cannot be proven with substitutes.",
                    "Verify integration behavior against a production-shaped dependency without shared mutable test state.",
                    ["Confirm that a real dependency is required and classify the test as component or integration, never unit.", "Reuse the repository container fixture and a pinned dependency image or module version.", "Create isolated state and deterministic seed data per test boundary.", "Wait on a service readiness signal instead of a fixed sleep.", "Exercise the real protocol, serialization, queries, constraints, or transaction behavior that motivated the container.", "Dispose resources and preserve the targeted test command plus container-runtime prerequisites."],
                    "Do not require a developer-owned shared service, silently fall back to a fake, leak credentials, or report the test as passed when container execution did not run."),
            ]);

        AddWhen(selected, classification, "frontend-delivery", "Frontend delivery",
            "A web, mobile, native, or game frontend was detected.",
            IsFrontend,
            [
                Skill("frontend-implementation", "Implement reviewed frontend behavior using the repository shell, design system, and platform conventions. Use after wireframes and designs are approved.",
                    "Translate approved designs into accessible, consistent, testable product behavior.",
                    ["Confirm the design gate is approved and read the effective UI framework profile.", "Reuse the application shell and shared controls before adding components.", "Implement loading, empty, error, permission, responsive, and navigation states.", "Preserve public, customer, and backoffice surface boundaries.", "Run platform-appropriate unit, interaction, rendering, and navigation checks."],
                    "Stop implementation when design approval is absent, rejected, stale, or hash-mismatched."),
                Skill("add-frontend-component-tests", "Add component and interaction tests for user-facing states, accessibility semantics, and emitted actions. Use when frontend components or screens change.",
                    "Prove component behavior below the browser-journey layer while preserving the platform's rendering and accessibility conventions.",
                    ["Map approved screen states and actions to focused component cases.", "Render with the repository framework and realistic typed inputs.", "Exercise user actions through accessible roles, labels, and platform interaction APIs.", "Cover loading, empty, validation, denied, failure, disabled, and responsive behavior as applicable.", "Assert observable output and emitted navigation or commands, then run the focused component suite."],
                    "Do not assert private component state, brittle DOM depth, or snapshots without behavioral assertions."),
                Skill("accessibility-review", "Review user-facing changes for keyboard, assistive technology, contrast, semantics, and motion. Use for web, mobile, native, or game UI delivery.",
                    "Make the reviewed interface usable beyond its default pointer-and-visual path.",
                    ["Map controls and states to semantic roles, labels, focus order, and announcements.", "Verify keyboard or platform navigation without hidden traps.", "Check contrast, text scaling, reduced motion, errors, and non-color cues.", "Run available automated checks and complete a manual interaction pass.", "Record limitations and ownership for unresolved accessibility risk."],
                    "Do not treat an automated accessibility scan as complete assurance."),
                Skill("frontend-observability", "Add privacy-safe frontend diagnostics, performance signals, and release-health evidence. Use when user-facing behavior needs production detection or diagnosis.",
                    "Make client failures and degraded journeys diagnosable without collecting sensitive interaction data.",
                    ["Identify critical journeys, failure states, performance thresholds, and owning teams.", "Reuse the repository telemetry provider, consent, sampling, and environment conventions.", "Capture stable error and navigation identities without secrets, tokens, form values, or sensitive URLs.", "Correlate client and server activity where supported and bound high-cardinality dimensions.", "Verify dashboards or queries, source maps, release identity, and alert ownership."],
                    "Do not add session capture, personal data, or verbose production logging without explicit privacy approval."),
            ]);

        AddWhen(selected, classification, "browser-assurance", "Browser assurance",
            "A web frontend framework was detected.",
            IsWebFrontend,
            [
                Skill("add-browser-regression-tests", "Add stable browser regression tests for approved user journeys and visual states. Use when web frontend behavior or rendering changes.",
                    "Prove high-value browser journeys while keeping selectors and visual evidence maintainable.",
                    ["Start from approved wireframes, design manifests, routes, and acceptance criteria.", "Use semantic selectors and repository fixtures rather than timing assumptions.", "Cover navigation, loading, empty, error, authorization, and responsive states as applicable.", "Keep visual snapshots bounded to reviewed components or screens.", "Run the supported browser matrix and preserve traces or screenshots for failures."],
                    "Do not update snapshots merely to make an unexplained difference pass."),
            ]);

        AddWhen(selected, classification, "secure-delivery", "Secure delivery",
            "Authorization or externally reachable API behavior was detected.",
            component => component.Capabilities.Contains("authorization", StringComparer.Ordinal)
                || component.Roles.Contains("backend-api-producer", StringComparer.Ordinal),
            [
                Skill("secure-feature", "Threat-model and verify authorization, exposure, validation, and secret-handling changes. Use when a feature crosses a trust boundary or handles sensitive data.",
                    "Make security requirements explicit and testable before completion is claimed.",
                    ["Identify actors, assets, trust boundaries, entry points, and abuse cases.", "Map permissions to server-side enforcement and negative tests.", "Check validation, output filtering, logging, secrets, caching, and rate limits.", "For public endpoints, verify cached projection access and no direct database dependency.", "Record security evidence, residual risk, and independent review requirements."],
                    "Do not rely on hidden UI controls or client-side checks as authorization."),
            ]);

        AddWhen(selected, classification, "infrastructure-delivery", "Infrastructure delivery",
            "Infrastructure or deployment assets were detected.",
            component => component.Roles.Contains("infrastructure", StringComparer.Ordinal)
                || component.Frameworks.Contains("terraform", StringComparer.Ordinal),
            [
                Skill("infrastructure-delivery", "Implement and validate infrastructure, configuration, secret, and pipeline changes. Use when delivery requires cloud, Terraform, queue, index, or CI/CD work.",
                    "Make environment changes reproducible, reviewable, least-privileged, and recoverable.",
                    ["Trace the change to an approved task and environment ownership.", "Reuse modules and naming, tagging, secret, and state conventions.", "Format, initialize without backend mutation when possible, validate, lint, and scan.", "Review the plan for replacement, privilege, cost, and data-loss risk.", "Document rollout, rollback, drift detection, and post-deployment verification."],
                    "Do not apply infrastructure or expose secret values as part of an implementation-only task."),
            ]);

        AddWhen(selected, classification, "operational-readiness", "Operational readiness",
            "A backend API, worker, event producer, or event consumer was detected.",
            component => component.Roles.Any(role => role is "backend-api-producer" or "worker" or "event-producer" or "event-consumer"),
            [
                Skill("observability-implementation", "Implement actionable logs, metrics, traces, alerts, dashboards, and runbook updates. Use when changed behavior needs operational detection or diagnosis.",
                    "Ensure operators can detect, explain, and recover from the changed behavior.",
                    ["Identify success, failure, latency, saturation, and business-health signals.", "Use stable event names and low-cardinality dimensions.", "Propagate correlation without logging secrets or sensitive payloads.", "Define alert intent, thresholds, ownership, and runbook actions.", "Verify telemetry locally or in a safe environment and preserve evidence."],
                    "Do not add noisy logs or metrics that have no operational consumer."),
            ]);

        AddWhen(selected, classification, "dependency-and-release", "Dependency and release",
            "Package, build, or deployment metadata was detected.",
            component => component.Capabilities.Contains("packages", StringComparer.Ordinal)
                || component.Capabilities.Contains("deployment", StringComparer.Ordinal)
                || component.Roles.Contains("package-producer", StringComparer.Ordinal),
            [
                Skill("dependency-upgrades", "Upgrade dependencies in bounded batches with compatibility, security, and verification evidence. Use when package versions or lockfiles change.",
                    "Reduce dependency risk without mixing unrelated upgrades or obscuring generated changes.",
                    ["Classify the upgrade as security, required compatibility, maintenance, or optional.", "Read release notes and identify breaking, runtime, license, and transitive changes.", "Update manifests and lockfiles with the repository package manager.", "Run affected build, tests, analyzers, and packaging checks.", "Record deferred upgrades and residual compatibility risk."],
                    "Do not bundle unrelated major upgrades into feature work without explicit scope."),
                Skill("release-rollout", "Plan and verify staged release, feature-flag, compatibility, rollback, and post-release checks. Use when a change affects deployment or published packages.",
                    "Turn implementation evidence into a controlled and reversible release path.",
                    ["Define artifacts, environments, compatibility window, and release ownership.", "Specify flags, staged exposure, migration ordering, and rollback criteria.", "Confirm observability and support readiness before increasing exposure.", "Run post-release validation against acceptance and operational signals.", "Record release evidence, incidents, rollback decisions, and follow-up work."],
                    "Do not infer production approval from plan or design approval."),
            ]);

        return selected.OrderBy(pack => pack.Id, StringComparer.Ordinal).ToArray();
    }

    private static void AddWhen(
        ICollection<SelectedImplementationSkillPack> selected,
        RepositoryClassification classification,
        string id,
        string title,
        string reason,
        Func<RepositoryComponentClassification, bool> predicate,
        IReadOnlyList<ImplementationSkillDefinition> skills)
    {
        var components = classification.Components.Where(predicate).ToArray();
        if (components.Length == 0)
        {
            return;
        }

        var evidence = components
            .SelectMany(component => component.Evidence.Select(item => $"{component.Id}: {item}"))
            .Distinct(StringComparer.Ordinal)
            .Take(12)
            .ToArray();
        selected.Add(new SelectedImplementationSkillPack(id, title, reason, evidence, skills));
    }

    private static bool IsFrontend(RepositoryComponentClassification component)
        => component.Roles.Any(role => role is "frontend-consumer" or "mobile-client" or "native-frontend")
            || component.Frameworks.Contains("godot", StringComparer.Ordinal);

    private static bool IsWebFrontend(RepositoryComponentClassification component)
        => component.Roles.Contains("frontend-consumer", StringComparer.Ordinal)
            && component.Frameworks.Any(framework => framework is "angular" or "nextjs" or "react" or "vue" or "nuxt");

    private static bool HasImplementation(RepositoryComponentClassification component)
        => component.Roles.Any(role => role != "test-automation")
            && component.Languages.Any(language => language is "csharp" or "typescript" or "javascript" or "swift" or "kotlin" or "gdscript");

    private static bool HasApplicationBehavior(RepositoryComponentClassification component)
        => component.Roles.Any(role => role is "backend-api-producer" or "worker" or "frontend-consumer" or "mobile-client" or "native-frontend" or "event-producer" or "event-consumer")
            || component.Frameworks.Contains("godot", StringComparer.Ordinal);

    private static ImplementationSkillDefinition Skill(
        string name,
        string description,
        string purpose,
        IReadOnlyList<string> workflow,
        string guardrail)
    {
        var steps = string.Join("\n", workflow.Select((step, index) => $"{index + 1}. {step}"));
        var content = $"""
            ---
            name: cis-{name}
            description: {description}
            ---

            # CIS {ToTitle(name)}

            ## Purpose

            {purpose}

            ## Workflow

            {steps}

            ## Completion evidence

            Preserve affected scope, exact commands, outcomes, generated artifacts, skipped checks, and residual risk in the canonical task or verification record.

            ## Guardrails

            {guardrail}
            """;
        return new ImplementationSkillDefinition(name, description, content + "\n");
    }

    private static string ToTitle(string value)
        => string.Join(' ', value.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}
