using System.Text;

namespace Cis.Modules.Repository;

internal static class DefaultStandardRegistry
{
    public static IReadOnlyList<DefaultStandardDefinition> Select(RepositoryClassification classification)
        => Definitions.Where(definition => definition.Applies(classification)).ToArray();

    public static string Render(string repositoryId, DefaultStandardDefinition definition)
    {
        var builder = new StringBuilder()
            .AppendLine("---")
            .AppendLine($"title: \"{definition.Title}\"")
            .AppendLine("type: standard")
            .AppendLine("status: Active")
            .AppendLine("targets:");
        foreach (var target in definition.Targets)
            builder.AppendLine($"  - {target}");
        if (definition.Stacks.Count > 0)
        {
            builder.AppendLine("stacks:");
            foreach (var stack in definition.Stacks)
                builder.AppendLine($"  - {stack}");
        }
        builder
            .AppendLine("owner: Repository maintainer")
            .AppendLine("last_reviewed: 2026-08-15")
            .AppendLine("review_cadence: on change")
            .AppendLine("source_of_truth: This file")
            .AppendLine("provenance:")
            .AppendLine("  method: curated adaptation")
            .AppendLine("  source_repository: PARR")
            .AppendLine("  source_paths:");
        foreach (var path in definition.SourcePaths)
            builder.AppendLine($"    - {path}");
        builder
            .AppendLine("cis:")
            .AppendLine($"  stable_id: {repositoryId}:standard:{definition.Slug}")
            .AppendLine("---")
            .AppendLine()
            .AppendLine($"# {definition.Title}")
            .AppendLine()
            .AppendLine("## Purpose")
            .AppendLine()
            .AppendLine(definition.Purpose)
            .AppendLine()
            .AppendLine("This starter is a classification-safe adaptation of the listed PARR standards. Repository maintainers may strengthen it, record bounded exceptions, or supersede it through reviewed canonical changes.")
            .AppendLine()
            .AppendLine("## Scope")
            .AppendLine()
            .AppendLine($"Applies to: {string.Join(", ", definition.Targets)}.")
            .AppendLine()
            .AppendLine("## Normative language")
            .AppendLine()
            .AppendLine("`MUST` and `MUST NOT` are mandatory. `SHOULD` is the expected default and requires recorded rationale when not followed. `MAY` identifies an allowed option.")
            .AppendLine();
        if (!string.IsNullOrWhiteSpace(definition.Guidance))
            builder.AppendLine(definition.Guidance.Trim()).AppendLine();
        builder
            .AppendLine("## Rules")
            .AppendLine();
        foreach (var rule in definition.Rules)
            builder.AppendLine($"- **{rule.Id}** {rule.Statement}");
        builder
            .AppendLine()
            .AppendLine("## Verification")
            .AppendLine();
        foreach (var rule in definition.Rules)
            builder.AppendLine($"- `{rule.Id}`: {rule.Verification}");
        builder
            .AppendLine()
            .AppendLine("Run `cis standards validate --strict` after changing this standard or its conformance mappings.")
            .AppendLine()
            .AppendLine("## Exceptions")
            .AppendLine()
            .AppendLine("An exception requires the rule ID, human approver, rationale, bounded scope, review or expiry condition, and compensating controls. CIS and agents cannot approve exceptions.");
        return builder.ToString();
    }

    private static bool HasRole(RepositoryClassification classification, params string[] roles)
        => classification.Components.Any(component => roles.Any(role => component.Roles.Contains(role, StringComparer.Ordinal)));

    private static bool HasCapability(RepositoryClassification classification, params string[] capabilities)
        => classification.Components.Any(component => capabilities.Any(capability => component.Capabilities.Contains(capability, StringComparer.Ordinal)));

    private static bool HasFramework(RepositoryClassification classification, params string[] frameworks)
        => classification.Components.Any(component => frameworks.Any(framework => component.Frameworks.Contains(framework, StringComparer.Ordinal)));

    private static bool HasApplicationBehavior(RepositoryClassification classification)
        => HasRole(classification, "backend-api-producer", "worker", "frontend-consumer", "mobile-client", "native-frontend", "event-producer", "event-consumer");

    private static bool HasImplementation(RepositoryClassification classification)
        => classification.Components.Any(component =>
            component.Roles.Any(role => role != "test-automation")
            && component.Languages.Any(language => language is "csharp" or "typescript" or "javascript" or "swift" or "kotlin" or "gdscript" or "hcl"));

    private static bool HasFrontend(RepositoryClassification classification)
        => HasRole(classification, "frontend-consumer", "mobile-client", "native-frontend");

    private static readonly DefaultStandardDefinition[] Definitions =
    [
        new(
            "agent-documentation-compliance",
            "Agent Documentation Compliance Standard",
            "Keep agent-authored changes aligned with canonical repository documentation and make conflicts visible to human reviewers.",
            ["documentation", "repository-governance"],
            [],
            ["docs/generic/standards/agent-documentation-compliance-standard.md"],
            _ => true,
            [
                Rule("AGENT-DOC-001", "Agents MUST resolve and read the canonical specifications, standards, references, decisions, and instructions applicable to a change before modifying implementation.", "Review the recorded context package or tool evidence."),
                Rule("AGENT-DOC-002", "Agents MUST treat repository documentation as canonical when derived graph, index, or model output disagrees with it.", "Review conflict handling and canonical citations."),
                Rule("AGENT-DOC-003", "Agents MUST report conflicts between a request and canonical repository governance instead of silently bypassing the documented rule.", "Review change evidence and decision records."),
                Rule("AGENT-DOC-004", "Completion evidence MUST identify the governing documents used and any documentation changed with the implementation.", "Review final delivery evidence and documentation traceability."),
            ]),
        new(
            "testing",
            "Testing and Verification Standard",
            "Require a risk-proportionate layered test strategy and auditable evidence rather than treating a successful build or one test layer as proof of complete behavior.",
            ["testing", "verification"],
            [],
            [
                "docs/generic/standards/testing-standard.md",
                ".github/instructions/testing-pyramid.instructions.md",
                ".github/instructions/tests.instructions.md",
                ".github/instructions/testcontainers.instructions.md",
            ],
            HasImplementation,
            [
                Rule("TEST-001", "Every change MUST map its acceptance criteria, affected boundaries, and material risks to the applicable test layers before completion is claimed.", "Inspect the feature or task verification matrix and trace each selected layer to acceptance or risk."),
                Rule("TEST-002", "Changed deterministic domain, application, validation, mapping, or policy behavior MUST have focused unit tests covering expected results, boundaries, invalid inputs, and material denied paths.", "Execute the narrow unit suite and review assertions for observable behavior rather than implementation mirroring."),
                Rule("TEST-003", "Tests classified as unit tests MUST remain fast and deterministic and MUST NOT require network access, external processes, containers, or shared mutable services.", "Inspect unit fixtures and execute the suite without external service or container prerequisites."),
                Rule("TEST-004", "API, persistence, migration, authentication, messaging, serialization, and other integration boundaries MUST have component or integration tests against representative infrastructure whenever substitutes cannot reproduce the relevant semantics.", "Execute the targeted component or integration suite and review its real-boundary evidence."),
                Rule("TEST-005", "Testcontainers or an equivalent disposable harness SHOULD provision real containerized dependencies when their protocol or runtime semantics materially affect correctness; it is a provisioning mechanism, not a separate test layer.", "Review pinned dependency versions, readiness, isolation, cleanup, runtime prerequisites, and actual container-backed execution evidence."),
                Rule("TEST-006", "Cross-step workflows, lifecycle transitions, and actor-visible business outcomes MUST have acceptance-style tests when focused unit and integration cases do not prove the complete outcome.", "Execute the focused business or acceptance scenarios and confirm they use domain language and thin fixtures."),
                Rule("TEST-007", "Architecture rules claimed as enforceable MUST have a deterministic architecture test or an explicit conformance mapping naming the manual evidence owner.", "Run structural checks or inspect the conformance matrix and recorded manual evidence."),
                Rule("TEST-008", "Frontend changes MUST use component or interaction tests for local behavior and browser or platform journey tests for critical composed workflows; one layer MUST NOT be reported as the other.", "Execute the applicable component and journey suites and inspect accessible selectors, state coverage, and failure artifacts."),
                Rule("TEST-009", "Bug fixes MUST include a regression test that fails for the reproduced defect when technically feasible.", "Review the preserved reproduction and confirm the new or changed test distinguishes broken from corrected behavior."),
                Rule("TEST-010", "High-value domain, authorization, lifecycle, calculation, migration, or historically fragile logic SHOULD receive mutation testing or another recorded independent assurance technique beyond line coverage.", "Review bounded mutation, security, architecture, property, or independent-review evidence and disposition surviving risks."),
                Rule("TEST-011", "Testable new or materially changed production behavior SHOULD maintain at least 95 percent line coverage unless the repository defines a stronger threshold or records a bounded human-approved exception; coverage MUST NOT replace behavior assertions.", "Review changed-scope coverage, exclusions, meaningful assertions, and any approved exception."),
                Rule("TEST-012", "CI and completion evidence MUST distinguish every applicable layer that passed, failed, was skipped, or could not run, including Docker, browser, credential, and environment limitations.", "Inspect exact commands, results, artifacts, unrun checks, environmental limits, and residual risks."),
                Rule("TEST-013", "Test execution MUST preserve a bounded, redacted, attempt-specific live log, including partial output on timeout or cancellation. Runtime harnesses SHOULD add sanitized suite diagnostics beneath .cis/local/testing/diagnostics/<suite-id>/<run-id>/attempt-<number>/; reconciliation MUST hash and correlate retained evidence to its run, attempt, suite, component, repository revision, and test identities without erasing earlier attempts.", "Inspect the first failing attempt log before any rerun; confirm secrets are redacted, partial timeout output survives, artifact limits are enforced, and reconciled hashes retain run and suite correlation."),
            ],
            Guidance: """
                ## Layered test model

                Use distinct layers with distinct claims:

                1. **Unit tests** verify isolated deterministic logic without external processes or services.
                2. **Architecture and policy tests** enforce structural dependency, ownership, naming, and repository standards.
                3. **Component and integration tests** verify API hosts, persistence, migrations, authentication, messaging, serialization, and real service boundaries. Testcontainers may provision a disposable dependency for this layer.
                4. **Business acceptance tests** verify cross-step workflows and actor-visible outcomes in domain language.
                5. **Frontend component and accessibility tests** verify local rendering, interaction, state, semantics, and emitted actions.
                6. **Browser or platform journey tests** verify critical composed user workflows across deployed application boundaries.
                7. **Mutation and independent assurance** challenge the strength of the preceding evidence for selected high-risk behavior.

                Documentation, contract, schema, generated-artifact, and policy drift checks accompany these layers. They are deterministic verification but do not replace behavioral tests.

                ## Change-to-test mapping

                | Change type | Minimum expected evidence |
                | --- | --- |
                | Domain rule, validator, mapper, or value object | Unit tests |
                | Lifecycle or cross-step workflow | Unit tests plus business acceptance tests |
                | Repository, migration, or real service adapter | Unit tests where useful plus component/integration tests against representative infrastructure |
                | API endpoint or contract | Unit/application tests plus API integration and contract-drift tests |
                | Authentication, authorization, permission, or visibility | Unit tests plus negative integration tests; architecture and journey tests where structural or user-visible |
                | Event, cache, projection, retry, or replay behavior | Unit tests plus integration tests for failure, idempotency, and recovery |
                | Frontend component or screen | Component/accessibility tests plus browser or platform journeys for critical composed flows |
                | Architecture or standards enforcement | Architecture test, deterministic evaluator, or explicit manual conformance mapping |
                | Documentation-only change | Documentation and policy compliance checks; code tests only when executable behavior changes |

                ## Test authoring and execution

                - Prefer focused tests with one primary reason to fail, clear arrange/act/assert structure, deterministic data, and observable contract or invariant assertions.
                - Treat AI-generated tests as candidates. Reject tautological tests, excessive mocks, presence-only interaction checks, and snapshot updates with no explained behavioral change.
                - Run the smallest meaningful affected layer during iteration, then broaden according to impact and release risk.
                - Pin container images or modules, wait for service readiness rather than fixed sleeps, isolate state, dispose resources, and never silently replace a required real dependency with a fake.
                - Keep test framework and folder conventions repository-specific. The layered strategy is portable; PARR-specific xUnit, ReqNroll, Stryker.NET, Playwright for .NET, and PostgreSQL choices are examples rather than universal CIS defaults.
                """),
        new(
            "secure-feature-implementation",
            "Secure Feature Implementation Standard",
            "Make authorization, data exposure, secrets, validation, and security verification explicit parts of application delivery.",
            ["security", "backend", "frontend"],
            [],
            ["docs/generic/standards/secure-feature-implementation-standard.md"],
            classification => HasApplicationBehavior(classification) || HasCapability(classification, "authorization"),
            [
                Rule("SEC-FEAT-001", "Every externally reachable operation MUST declare its authentication and authorization expectations.", "Inspect contract and permission references."),
                Rule("SEC-FEAT-002", "Authorization MUST be enforced at the trusted server or platform boundary and MUST NOT rely only on hidden or disabled user-interface controls.", "Test denied access at the trusted enforcement boundary."),
                Rule("SEC-FEAT-003", "Caller-controlled identifiers MUST NOT expand tenant, customer, object, or privilege scope beyond the validated security context.", "Test cross-scope and object-level access attempts."),
                Rule("SEC-FEAT-004", "Sensitive values MUST NOT be written to source, generated documentation, logs, model prompts, or user-visible errors.", "Run secret scanning and inspect logging/error paths."),
                Rule("SEC-FEAT-005", "Untrusted input MUST be bounded and validated before it reaches domain, persistence, command, rendering, or external-service boundaries.", "Test invalid, oversized, and malicious input cases."),
                Rule("SEC-FEAT-006", "Security-significant changes MUST include explicit abuse cases and verification evidence.", "Review security tasks, tests, and independent assurance."),
            ]),
        new(
            "api-controller",
            "HTTP API Boundary Standard",
            "Keep HTTP endpoints thin, secure, compatible, observable, and represented by an authoritative machine-readable contract.",
            ["backend", "api", "security"],
            ["http"],
            ["docs/generic/standards/api-controller-standard.md"],
            classification => HasRole(classification, "backend-api-producer"),
            [
                Rule("API-BOUNDARY-001", "Every endpoint MUST declare its host or exposure class, authentication, authorization, and trusted scope source.", "Inspect the API dictionary and generated contract."),
                Rule("API-BOUNDARY-002", "Endpoint handlers MUST remain transport adapters and MUST delegate business behavior and persistence access to application boundaries.", "Run architecture checks or manually review endpoint dependencies."),
                Rule("API-BOUNDARY-003", "Externally reachable routes MUST use resource-oriented HTTP semantics and an explicit compatibility or versioning policy.", "Validate the route inventory and supported-version profile."),
                Rule("API-BOUNDARY-004", "Every governed operation MUST appear in the authoritative OpenAPI document with stable operation identity, request, response, error, and security metadata.", "Run OpenAPI generation and documentation drift checks."),
                Rule("API-BOUNDARY-005", "Object- and property-level authorization MUST be enforced before returning or mutating protected data.", "Execute denied cross-object and over-posting tests."),
                Rule("API-BOUNDARY-006", "Errors MUST use the repository problem contract without exposing stack traces, secrets, or protected resource existence.", "Inspect Problem Details mappings and negative-path tests."),
                Rule("API-BOUNDARY-007", "Retryable mutations MUST define idempotency and concurrency behavior.", "Inspect contracts and execute replay/concurrency tests."),
                Rule("API-BOUNDARY-008", "Unauthenticated endpoints MUST comply with the repository public-endpoint caching and direct-database-isolation policy.", "Run public endpoint architecture and cache behavior checks."),
            ]),
        new(
            "repository-transaction",
            "Repository and Transaction Boundary Standard",
            "Keep persistence responsibilities, transaction scope, caching, concurrency, and external side effects explicit.",
            ["backend", "data", "persistence"],
            [],
            ["docs/generic/standards/repository-standard.md"],
            classification => HasCapability(classification, "persistence"),
            [
                Rule("REPO-R-001", "Repositories MUST act as persistence boundaries and MUST NOT contain domain workflow policy.", "Review repository responsibilities and architecture dependencies."),
                Rule("REPO-R-002", "One business command MUST have one explicit transaction scope for its authoritative state changes.", "Test transaction commit and rollback behavior."),
                Rule("REPO-R-003", "Repositories MUST NOT independently commit or invoke external side effects inside an authoritative database transaction.", "Run architecture checks and failure-path integration tests."),
                Rule("REPO-R-004", "Mutable data SHOULD use an explicit concurrency strategy appropriate to its collision risk.", "Review concurrency tokens, locking, or documented rationale."),
                Rule("REPO-R-005", "Derived caches, indexes, and projections MUST NOT become the authoritative system of record.", "Review ownership references and rebuild behavior."),
                Rule("REPO-R-006", "Cache invalidation MUST be driven by committed change and MUST fail safely without corrupting authoritative state.", "Test invalidation failure and stale-read behavior."),
            ]),
        new(
            "outbox-projection",
            "Outbox and Projection Standard",
            "Make durable event propagation, retry safety, projection freshness, and rebuild behavior explicit.",
            ["backend", "events", "projections"],
            [],
            ["docs/generic/standards/outbox-projection-standard.md"],
            classification => HasRole(classification, "event-producer", "event-consumer"),
            [
                Rule("OBX-R-001", "Authoritative state and its outbox record MUST commit atomically before external publication.", "Execute transaction and dispatch failure integration tests."),
                Rule("OBX-R-002", "Every published event MUST have stable identity, type, version, ownership, and compatibility expectations.", "Validate the event dictionary and serialized contracts."),
                Rule("OBX-R-003", "Event handlers MUST be retry-safe and MUST use an explicit idempotency mechanism for material effects.", "Execute duplicate-delivery and retry tests."),
                Rule("OBX-R-004", "Projection freshness, failure, replay, and rebuild behavior MUST be observable and documented.", "Review projection references, metrics, and runbooks."),
                Rule("OBX-R-005", "Poison messages MUST have bounded retry and a recoverable failure route without blocking unrelated delivery indefinitely.", "Test terminal retry handling and operational recovery."),
            ]),
        new(
            "frontend-interaction",
            "Frontend Interaction and Component Standard",
            "Keep application shells, navigation, actions, forms, tables, states, and reusable controls predictable across public, customer, and backoffice surfaces.",
            ["frontend", "design"],
            [],
            [
                "docs/generic/standards/ui-ux-standard.md",
                "docs/generic/standards/action-placement-standard.md",
                "docs/generic/standards/cards-and-panels-standard.md",
                "docs/generic/standards/empty-loading-error-states-standard.md",
                "docs/generic/standards/forms-and-assignment-controls-standard.md",
                "docs/generic/standards/navigation-and-filtering-standard.md",
                "docs/generic/standards/status-badge-standard.md",
                "docs/generic/standards/table-standard.md",
            ],
            HasFrontend,
            [
                Rule("UI-001", "Every screen MUST use the application shell and existing shared components before introducing page-specific equivalents.", "Review the wireframe, design provenance, and component inventory."),
                Rule("UI-002", "Frontend requirements, wireframes, designs, and implementation tasks MUST be classified as public, customer, or backoffice.", "Validate task and design classifications."),
                Rule("UI-003", "Navigation MUST change destination or section, while filters MUST only narrow the current result set.", "Review route behavior and interaction tests."),
                Rule("UI-004", "A page SHOULD expose at most one dominant primary action, labelled with a clear verb and object.", "Review rendered design and interaction hierarchy."),
                Rule("UI-005", "Destructive or materially irreversible actions MUST explain their impact and require confirmation.", "Execute destructive-action interaction tests."),
                Rule("UI-006", "Forms MUST use labelled controls, adjacent validation, predictable save/cancel placement, and human-readable selection values.", "Review component accessibility and form tests."),
                Rule("UI-007", "Loading, empty, filtered-empty, permission, error, and success states MUST be explicitly designed where applicable.", "Review state coverage in designs and tests."),
                Rule("UI-008", "Data tables MUST define column behavior, sorting, empty values, responsive priority, loading behavior, and row-action placement.", "Inspect table definitions and sorting/responsive tests."),
                Rule("UI-009", "Status badges MUST use shared domain vocabulary and MUST communicate meaning with text rather than colour alone.", "Review the design system and accessibility evidence."),
                Rule("UI-010", "Reusable buttons, inputs, dropdowns, pickers, cards, panels, tables, and feedback states MUST preserve shared tokens and interaction behavior.", "Review component-template reuse and visual regression evidence."),
            ]),
        new(
            "accessibility-responsive-regression",
            "Accessibility, Responsive, and Visual Regression Standard",
            "Keep user interfaces keyboard-accessible, assistive-technology readable, responsive, and visually stable.",
            ["frontend", "accessibility", "testing"],
            [],
            ["docs/generic/standards/accessibility-responsive-regression-standard.md", "docs/generic/standards/browser-regression-standard.md"],
            HasFrontend,
            [
                Rule("A11Y-001", "Interactive controls MUST be keyboard reachable and operable with a visible focus state.", "Run keyboard navigation checks on affected screens."),
                Rule("A11Y-002", "Inputs and controls MUST have accessible names; placeholder text MUST NOT be their only label.", "Run automated accessibility checks and manual name inspection."),
                Rule("A11Y-003", "Dialogs MUST manage focus, provide an accessible name, and restore focus when closed.", "Execute dialog keyboard and focus tests."),
                Rule("A11Y-004", "Meaning MUST NOT depend on colour, position, or animation alone.", "Review contrast and non-visual semantics."),
                Rule("RESP-001", "Layouts, tables, filters, cards, and panels MUST define behavior for narrow and wide viewports.", "Render and review the approved viewport set."),
                Rule("REG-UI-001", "Core changed screens SHOULD have deterministic screenshot or visual-regression coverage using the repository renderer.", "Validate PNG manifests, hashes, and visual evidence."),
                Rule("REG-UI-002", "State-changing controls MUST be tested through their observable mutation or resulting state, not only for visibility.", "Review interaction-to-result regression tests."),
            ]),
        new(
            "observability",
            "Application Observability Standard",
            "Provide actionable, privacy-safe evidence for failures, performance, dependencies, and operational health.",
            ["backend", "frontend", "operations"],
            [],
            ["docs/generic/standards/backend-observability-logging-standard.md", "docs/generic/standards/frontend-observability-standard.md", "docs/generic/standards/observability-telemetry-operational-diagnostics-standard.md"],
            HasApplicationBehavior,
            [
                Rule("OBS-001", "Material operations MUST emit structured telemetry with stable event identity, outcome, and correlation context.", "Inspect emitted logs, traces, or events in representative tests."),
                Rule("OBS-002", "Telemetry MUST NOT contain secrets, credentials, raw tokens, or unnecessary sensitive personal or business data.", "Run sensitive-data checks and inspect representative telemetry."),
                Rule("OBS-003", "Failures MUST preserve enough bounded context to diagnose the operation without exposing protected internals to callers.", "Review error responses and internal diagnostic evidence."),
                Rule("OBS-004", "External dependencies and asynchronous processing MUST expose latency, failure, retry, and saturation evidence where operationally material.", "Review metrics, traces, alerts, and failure tests."),
                Rule("OBS-005", "Operationally significant features MUST define health signals, alert ownership, and a recovery or support procedure.", "Review dashboards, alerts, ownership, and runbooks."),
                Rule("OBS-006", "Frontend telemetry MUST distinguish route, interaction, rendering, network, and handled-error failures without recording sensitive field contents.", "Review client telemetry schema and privacy tests."),
            ]),
        new(
            "edge-security-header",
            "Edge Security Header Standard",
            "Assign one clear owner for browser security headers, caching directives, and environment-specific edge exceptions.",
            ["frontend", "backend", "infrastructure", "security"],
            ["http"],
            ["docs/generic/standards/edge-security-header-standard.md"],
            classification => HasRole(classification, "frontend-consumer", "backend-api-producer") || HasFramework(classification, "aspnet-core", "nextjs"),
            [
                Rule("EDGE-HDR-001", "Each security or caching header MUST have one authoritative configuration owner for a deployed route.", "Inspect edge, host, and application configuration for duplicate ownership."),
                Rule("EDGE-HDR-002", "Conflicting duplicate security headers MUST be rejected by deterministic deployment or integration validation.", "Run deployed-header validation against representative routes."),
                Rule("EDGE-HDR-003", "HSTS MUST use a staged rollout and MUST NOT be enabled for an environment before HTTPS behavior is verified.", "Review environment rollout evidence and HTTPS probes."),
                Rule("EDGE-HDR-004", "Cache-control behavior MUST preserve the repository policy for authenticated, sensitive, and unauthenticated responses.", "Execute response-header and cache-isolation tests."),
                Rule("EDGE-HDR-005", "Environment exceptions MUST identify an owner, rationale, bounded scope, and removal or review condition.", "Review exception records and deployed configuration."),
            ]),
    ];

    private static DefaultStandardRule Rule(string id, string statement, string verification)
        => new(id, statement, verification);
}

internal sealed record DefaultStandardDefinition(
    string Slug,
    string Title,
    string Purpose,
    IReadOnlyList<string> Targets,
    IReadOnlyList<string> Stacks,
    IReadOnlyList<string> SourcePaths,
    Func<RepositoryClassification, bool> Applies,
    IReadOnlyList<DefaultStandardRule> Rules,
    string? Guidance = null);

internal sealed record DefaultStandardRule(string Id, string Statement, string Verification);
